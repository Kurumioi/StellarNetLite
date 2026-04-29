using System;

namespace StellarNet.Lite.Shared.Core
{
    /// <summary>
    /// 极简底层传输封套 (高性能值类型版)。
    /// 核心重构：引入 PayloadOffset 配合 ArraySegment 实现真正的 0GC 解析。
    /// </summary>
    public struct Packet
    {
        /// <summary>
        /// 会话内自增包序号。
        /// </summary>
        public uint Seq;

        /// <summary>
        /// 协议 Id。
        /// </summary>
        public int MsgId;

        /// <summary>
        /// 消息作用域。
        /// </summary>
        public NetScope Scope;

        /// <summary>
        /// 房间消息附带的目标 RoomId。
        /// </summary>
        public string RoomId;

        /// <summary>
        /// 原始载荷数组。
        /// </summary>
        public byte[] Payload;

        /// <summary>
        /// 载荷起始偏移。
        /// </summary>
        public int PayloadOffset;

        /// <summary>
        /// 载荷有效长度。
        /// </summary>
        public int PayloadLength;

        /// <summary>
        /// 完整构造，支持带偏移的 ArraySegment 载荷。
        /// </summary>
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

        /// <summary>
        /// 简化构造，默认载荷从 0 偏移开始。
        /// </summary>
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
