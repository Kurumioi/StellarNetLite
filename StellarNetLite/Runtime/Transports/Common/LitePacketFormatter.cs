using System;
using System.Text;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Transports.Common
{
    /// <summary>
    /// Packet 的轻量二进制编解码器。
    /// </summary>
    public static class LitePacketFormatter
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static int GetSerializedLength(Packet packet)
        {
            int roomIdByteLength = 0;
            if (!string.IsNullOrEmpty(packet.RoomId))
            {
                roomIdByteLength = Utf8NoBom.GetByteCount(packet.RoomId);
                if (roomIdByteLength > 255)
                {
                    NetLogger.LogError("LitePacketFormatter", $"序列化失败: RoomId 字节长度超过 255 限制。Length:{roomIdByteLength}");
                    throw new ArgumentOutOfRangeException(nameof(packet.RoomId), "RoomId byte length cannot exceed 255.");
                }
            }

            // Seq(4) + MsgId(4) + Scope(1) + RoomIdLength(1) + RoomId + Payload
            return 10 + roomIdByteLength + Math.Max(0, packet.PayloadLength);
        }

        public static int Serialize(Packet packet, byte[] buffer, int startOffset = 0)
        {
            int offset = startOffset;

            buffer[offset++] = (byte)(packet.Seq);
            buffer[offset++] = (byte)(packet.Seq >> 8);
            buffer[offset++] = (byte)(packet.Seq >> 16);
            buffer[offset++] = (byte)(packet.Seq >> 24);

            buffer[offset++] = (byte)(packet.MsgId);
            buffer[offset++] = (byte)(packet.MsgId >> 8);
            buffer[offset++] = (byte)(packet.MsgId >> 16);
            buffer[offset++] = (byte)(packet.MsgId >> 24);

            buffer[offset++] = (byte)packet.Scope;

            if (string.IsNullOrEmpty(packet.RoomId))
            {
                buffer[offset++] = 0;
            }
            else
            {
                int strLen = Utf8NoBom.GetByteCount(packet.RoomId);
                if (strLen > 255)
                {
                    NetLogger.LogError("LitePacketFormatter", $"序列化失败: RoomId 字节长度超过 255 限制。Length:{strLen}");
                    throw new ArgumentOutOfRangeException(nameof(packet.RoomId), "RoomId byte length cannot exceed 255.");
                }

                buffer[offset++] = (byte)strLen;
                Utf8NoBom.GetBytes(packet.RoomId, 0, packet.RoomId.Length, buffer, offset);
                offset += strLen;
            }

            if (packet.PayloadLength > 0 && packet.Payload != null)
            {
                Buffer.BlockCopy(packet.Payload, packet.PayloadOffset, buffer, offset, packet.PayloadLength);
                offset += packet.PayloadLength;
            }

            return offset - startOffset;
        }

        public static bool TryDeserialize(byte[] data, int startOffset, int length, out Packet packet)
        {
            packet = default;

            if (data == null || length < 10) return false;

            int offset = startOffset;
            int end = startOffset + length;

            uint seq = (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
            offset += 4;

            int msgId = (int)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
            offset += 4;

            NetScope scope = (NetScope)data[offset++];

            int roomIdLen = data[offset++];
            string roomId = string.Empty;

            if (roomIdLen > 0)
            {
                if (offset + roomIdLen > end) return false;
                roomId = Utf8NoBom.GetString(data, offset, roomIdLen);
                offset += roomIdLen;
            }

            int payloadLen = end - offset;
            packet = new Packet(seq, msgId, scope, roomId, data, offset, payloadLen);

            return true;
        }
    }
}
