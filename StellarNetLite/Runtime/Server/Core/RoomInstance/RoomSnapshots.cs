using System;
using System.Collections.Generic;

namespace StellarNet.Lite.Server.Core
{
    /// <summary>
    /// 房间运行时轻量快照。
    /// 供大厅列表、GC 与监控读取，避免直接并发访问活跃房间对象。
    /// </summary>
    public sealed class RoomRuntimeSnapshot
    {
        public string RoomId { get; set; } = string.Empty;
        public string RoomName { get; set; } = string.Empty;
        public RoomState State { get; set; } = RoomState.Waiting;
        public int MemberCount { get; set; }
        public int OnlineMemberCount { get; set; }
        public int MaxMembers { get; set; }
        public bool IsPrivate { get; set; }
        public int CurrentTick { get; set; }
        public bool IsRecording { get; set; }
        public string LastReplayId { get; set; } = string.Empty;
        public int[] ComponentIds { get; set; } = Array.Empty<int>();
        public DateTime CreateTimeUtc { get; set; }
        public DateTime EmptySinceUtc { get; set; }
        public int AssignedWorkerId { get; set; } = -1;
        public double WorkerAverageTickMs { get; set; }
    }

    /// <summary>
    /// 房间成员快照。
    /// </summary>
    public sealed class RoomMemberSnapshot
    {
        public string SessionId { get; set; } = string.Empty;
        public string AccountId { get; set; } = string.Empty;
        public int ConnectionId { get; set; }
        public bool IsOnline { get; set; }
        public bool IsRoomReady { get; set; }
        public float LastActiveRealtime { get; set; }
        public float LastRoomActiveRealtime { get; set; }
    }

    /// <summary>
    /// 房间详情快照。
    /// 供无头模式和编辑器监控查看详细状态。
    /// </summary>
    public sealed class RoomDetailedSnapshot
    {
        public RoomRuntimeSnapshot Runtime { get; set; } = new RoomRuntimeSnapshot();
        public int CustomPropertyCount { get; set; }
        public KeyValuePair<string, string>[] CustomProperties { get; set; } = Array.Empty<KeyValuePair<string, string>>();
        public RoomMemberSnapshot[] Members { get; set; } = Array.Empty<RoomMemberSnapshot>();
    }
}
