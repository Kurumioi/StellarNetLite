using System;

namespace StellarNet.Lite.Shared.Core
{
    /// <summary>
    /// 极简底层传输封套 (高性能值类型版)。
    /// 核心重构：引入 PayloadOffset 配合 ArraySegment 实现真正的 0GC 解析。
    /// </summary>
    public struct Packet
    {
        // 会话内自增包序号。
        public uint Seq;
        // 协议 Id。
        public int MsgId;
        // 消息作用域。
        public NetScope Scope;
        // 房间消息附带的目标 RoomId。
        public string RoomId;
        // 原始载荷数组。
        public byte[] Payload;
        // 载荷起始偏移。
        public int PayloadOffset;
        // 载荷有效长度。
        public int PayloadLength;

        // 完整构造，支持带偏移的 ArraySegment 载荷。
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

        // 简化构造，默认载荷从 0 偏移开始。
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
