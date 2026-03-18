using System.Collections.Generic;

namespace StellarNet.Lite.Shared.Core
{
    /// <summary>
    /// 极简底层传输封套 (高性能值类型版)。
    /// </summary>
    public struct Packet
    {
        public uint Seq;
        public int MsgId;
        public NetScope Scope;
        public string RoomId;
        public byte[] Payload;

        public Packet(uint seq, int msgId, NetScope scope, string roomId, byte[] payload)
        {
            Seq = seq;
            MsgId = msgId;
            Scope = scope;
            RoomId = roomId ?? string.Empty;
            Payload = payload;
        }

        public Packet(int msgId, NetScope scope, string roomId, byte[] payload)
        {
            Seq = 0;
            MsgId = msgId;
            Scope = scope;
            RoomId = roomId ?? string.Empty;
            Payload = payload;
        }
    }

    /// <summary>
    /// 独立回放帧结构 (高性能值类型版)。
    /// </summary>
    public struct ReplayFrame
    {
        public int Tick;
        public int MsgId;
        public byte[] Payload;
        public string RoomId;

        public ReplayFrame(int tick, int msgId, byte[] payload, string roomId)
        {
            Tick = tick;
            MsgId = msgId;
            Payload = payload;
            RoomId = roomId ?? string.Empty;
        }
    }

    /// <summary>
    /// 完整的回放文件结构。
    /// </summary>
    public sealed class ReplayFile
    {
        public string ReplayId;

        // 核心新增：录像展示名称
        public string DisplayName;
        public string RoomId;
        public int[] ComponentIds;
        public List<ReplayFrame> Frames = new List<ReplayFrame>();
    }
}