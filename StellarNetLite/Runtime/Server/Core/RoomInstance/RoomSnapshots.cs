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
        /// <summary>
        /// 房间唯一 Id。
        /// </summary>
        public string RoomId { get; set; } = string.Empty;

        /// <summary>
        /// 房间显示名。
        /// </summary>
        public string RoomName { get; set; } = string.Empty;

        /// <summary>
        /// 当前房间状态。
        /// </summary>
        public RoomState State { get; set; } = RoomState.Waiting;

        /// <summary>
        /// 当前成员总数。
        /// </summary>
        public int MemberCount { get; set; }

        /// <summary>
        /// 当前在线成员数。
        /// </summary>
        public int OnlineMemberCount { get; set; }

        /// <summary>
        /// 房间最大人数。
        /// </summary>
        public int MaxMembers { get; set; }

        /// <summary>
        /// 是否为私有房。
        /// </summary>
        public bool IsPrivate { get; set; }

        /// <summary>
        /// 当前房间 Tick。
        /// </summary>
        public int CurrentTick { get; set; }

        /// <summary>
        /// 当前是否正在录制录像。
        /// </summary>
        public bool IsRecording { get; set; }

        /// <summary>
        /// 最近一次生成的录像 Id。
        /// </summary>
        public string LastReplayId { get; set; } = string.Empty;

        /// <summary>
        /// 房间挂载的组件 Id 列表。
        /// </summary>
        public int[] ComponentIds { get; set; } = Array.Empty<int>();

        /// <summary>
        /// 房间创建时间。
        /// </summary>
        public DateTime CreateTimeUtc { get; set; }

        /// <summary>
        /// 房间变为空房的时间。
        /// </summary>
        public DateTime EmptySinceUtc { get; set; }

        /// <summary>
        /// 当前分配的工作线程 Id。
        /// </summary>
        public int AssignedWorkerId { get; set; } = -1;

        /// <summary>
        /// 当前工作线程平均 Tick 耗时。
        /// </summary>
        public double WorkerAverageTickMs { get; set; }
    }

    /// <summary>
    /// 房间成员快照。
    /// </summary>
    public sealed class RoomMemberSnapshot
    {
        /// <summary>
        /// 成员 SessionId。
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 成员账号 Id。
        /// </summary>
        public string AccountId { get; set; } = string.Empty;

        /// <summary>
        /// 当前连接 Id。
        /// </summary>
        public int ConnectionId { get; set; }

        /// <summary>
        /// 当前是否在线。
        /// </summary>
        public bool IsOnline { get; set; }

        /// <summary>
        /// 当前是否已完成房间准备。
        /// </summary>
        public bool IsRoomReady { get; set; }

        /// <summary>
        /// 最后一次任意业务活跃时间。
        /// </summary>
        public float LastActiveRealtime { get; set; }

        /// <summary>
        /// 最后一次房间业务活跃时间。
        /// </summary>
        public float LastRoomActiveRealtime { get; set; }
    }

    /// <summary>
    /// 房间详情快照。
    /// 供无头模式和编辑器监控查看详细状态。
    /// </summary>
    public sealed class RoomDetailedSnapshot
    {
        /// <summary>
        /// 房间运行时摘要。
        /// </summary>
        public RoomRuntimeSnapshot Runtime { get; set; } = new RoomRuntimeSnapshot();

        /// <summary>
        /// 自定义属性数量。
        /// </summary>
        public int CustomPropertyCount { get; set; }

        /// <summary>
        /// 自定义属性列表。
        /// </summary>
        public KeyValuePair<string, string>[] CustomProperties { get; set; } = Array.Empty<KeyValuePair<string, string>>();

        /// <summary>
        /// 当前成员快照列表。
        /// </summary>
        public RoomMemberSnapshot[] Members { get; set; } = Array.Empty<RoomMemberSnapshot>();
    }
}
