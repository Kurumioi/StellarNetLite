using System;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;
using Mirror;
using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Infrastructure
{
    public interface INetSerializer
    {
        byte[] Serialize(object obj);

        int Serialize(object obj, byte[] buffer);

        // 核心修复 P0-1：强制要求传入 offset 和 length，完美适配 ArraySegment 的底层切片
        object Deserialize(byte[] data, int offset, int length, Type type);
    }

    public interface ILiteNetSerializable
    {
        void Serialize(BinaryWriter writer);
        void Deserialize(BinaryReader reader);
    }

    public sealed class LiteNetSerializer : INetSerializer
    {
        public byte[] Serialize(object obj)
        {
            if (obj == null) return new byte[0];
            if (obj is ILiteNetSerializable serializable)
            {
                using (var ms = new MemoryStream())
                using (var writer = new BinaryWriter(ms))
                {
                    serializable.Serialize(writer);
                    return ms.ToArray();
                }
            }

            try
            {
                string json = JsonConvert.SerializeObject(obj);
                return System.Text.Encoding.UTF8.GetBytes(json);
            }
            catch (Exception e)
            {
                NetLogger.LogError("[LiteNetSerializer]", $"JSON序列化异常: {e.Message}");
                return new byte[0];
            }
        }

        public int Serialize(object obj, byte[] buffer)
        {
            if (obj == null || buffer == null) return 0;

            if (obj is ILiteNetSerializable serializable)
            {
                using (var ms = new MemoryStream(buffer))
                using (var writer = new BinaryWriter(ms))
                {
                    serializable.Serialize(writer);
                    return (int)ms.Position;
                }
            }

            try
            {
                string json = JsonConvert.SerializeObject(obj);
                return System.Text.Encoding.UTF8.GetBytes(json, 0, json.Length, buffer, 0);
            }
            catch (Exception e)
            {
                NetLogger.LogError("[LiteNetSerializer]", $"JSON 0GC序列化异常: {e.Message}");
                return 0;
            }
        }

        public object Deserialize(byte[] data, int offset, int length, Type type)
        {
            if (data == null || length == 0 || type == null) return null;

            if (typeof(ILiteNetSerializable).IsAssignableFrom(type))
            {
                try
                {
                    var obj = (ILiteNetSerializable)Activator.CreateInstance(type);
                    // 严格应用 offset 与 length
                    using (var ms = new MemoryStream(data, offset, length))
                    using (var reader = new BinaryReader(ms))
                    {
                        obj.Deserialize(reader);
                    }

                    return obj;
                }
                catch (Exception e)
                {
                    NetLogger.LogError("[LiteNetSerializer]", $"二进制反序列化异常: {type.Name}, 错误: {e.Message}");
                    return null;
                }
            }

            try
            {
                // 核心修复 P0-1：从 offset 开始读取，彻底避开 Mirror 缓冲区的头部脏数据
                string json = System.Text.Encoding.UTF8.GetString(data, offset, length);
                return JsonConvert.DeserializeObject(json, type);
            }
            catch (Exception e)
            {
                NetLogger.LogError("[LiteNetSerializer]", $"JSON反序列化异常: 目标类型 {type.Name}, 错误: {e.Message}");
                return null;
            }
        }
    }

    public struct MirrorPacketMsg : NetworkMessage
    {
        public uint Seq;
        public int MsgId;
        public byte Scope;
        public string RoomId;
        public ArraySegment<byte> Payload;

        public MirrorPacketMsg(Packet packet)
        {
            Seq = packet.Seq;
            MsgId = packet.MsgId;
            Scope = (byte)packet.Scope;
            RoomId = packet.RoomId ?? string.Empty;
            Payload = new ArraySegment<byte>(packet.Payload, packet.PayloadOffset, packet.PayloadLength);
        }

        public Packet ToPacket()
        {
            // 核心修复 P0-2：必须提取 Payload.Offset，否则将读到 Mirror 的包头
            return new Packet(Seq, MsgId, (NetScope)Scope, RoomId, Payload.Array, Payload.Offset, Payload.Count);
        }
    }
}