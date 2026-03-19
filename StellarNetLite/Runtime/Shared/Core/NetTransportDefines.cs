using System;

namespace StellarNet.Lite.Shared.Core
{
    /// <summary>
    /// 极简底层传输封套 (高性能值类型版)。
    /// 核心重构：引入 PayloadOffset 配合 ArraySegment 实现真正的 0GC 解析。
    /// </summary>
    public struct Packet
    {
        public uint Seq;
        public int MsgId;
        public NetScope Scope;
        public string RoomId;
        public byte[] Payload;
        public int PayloadOffset;
        public int PayloadLength;

        public Packet(uint seq, int msgId, NetScope scope, string roomId, byte[] payload, int payloadOffset, int payloadLength)
        {
            Seq = seq;
            MsgId = msgId;
            Scope = scope;
            RoomId = roomId ?? string.Empty;
            Payload = payload;
            PayloadOffset = payloadOffset;
            PayloadLength = payloadLength;
        }

        public Packet(uint seq, int msgId, NetScope scope, string roomId, byte[] payload, int payloadLength)
        {
            Seq = seq;
            MsgId = msgId;
            Scope = scope;
            RoomId = roomId ?? string.Empty;
            Payload = payload;
            PayloadOffset = 0;
            PayloadLength = payloadLength;
        }
    }
}