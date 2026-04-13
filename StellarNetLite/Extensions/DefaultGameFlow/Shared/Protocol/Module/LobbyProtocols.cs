using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    /// <summary>
    /// 单个房间的大厅摘要信息。
    /// </summary>
    public class RoomBriefInfo
    {
        /// <summary>
        /// 房间唯一 Id。
        /// </summary>
        public string RoomId;

        /// <summary>
        /// 房间展示名。
        /// </summary>
        public string RoomName;

        /// <summary>
        /// 当前成员数。
        /// </summary>
        public int MemberCount;

        /// <summary>
        /// 房间最大成员数。
        /// </summary>
        public int MaxMembers;

        /// <summary>
        /// 是否为私有房。
        /// </summary>
        public bool IsPrivate;

        /// <summary>
        /// 房间状态值。
        /// </summary>
        public int State;
    }

    /// <summary>
    /// 在线玩家摘要信息。
    /// </summary>
    public sealed class OnlinePlayerInfo
    {
        /// <summary>
        /// 玩家会话 Id。
        /// </summary>
        public string SessionId;

        /// <summary>
        /// 玩家账号 Id。
        /// </summary>
        public string AccountId;

        /// <summary>
        /// 玩家显示名。
        /// </summary>
        public string DisplayName;

        /// <summary>
        /// 玩家当前是否在线。
        /// </summary>
        public bool IsOnline;

        /// <summary>
        /// 玩家当前是否在房间中。
        /// </summary>
        public bool IsInRoom;

        /// <summary>
        /// 玩家所在房间 Id。
        /// </summary>
        public string RoomId;
    }

    /// <summary>
    /// 请求房间列表。
    /// </summary>
    [NetMsg(210, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_GetRoomList
    {
    }

    /// <summary>
    /// 房间列表响应。
    /// </summary>
    [NetMsg(211, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_RoomListResponse
    {
        /// <summary>
        /// 当前可展示的房间列表。
        /// </summary>
        public RoomBriefInfo[] Rooms;
    }

    /// <summary>
    /// 在线玩家全量同步。
    /// </summary>
    [NetMsg(212, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_OnlinePlayerListSync
    {
        /// <summary>
        /// 当前在线玩家列表。
        /// </summary>
        public OnlinePlayerInfo[] Players;
    }

    /// <summary>
    /// 单个玩家状态增量同步。
    /// </summary>
    [NetMsg(213, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_GlobalPlayerStateIncrementalSync
    {
        /// <summary>
        /// 是否为移除事件。
        /// </summary>
        public bool IsRemoved;

        /// <summary>
        /// 发生变化的玩家信息。
        /// </summary>
        public OnlinePlayerInfo Player;
    }

    /// <summary>
    /// 全局公告广播。
    /// </summary>
    [NetMsg(214, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_GlobalAnnouncement
    {
        /// <summary>
        /// 公告标题。
        /// </summary>
        public string Title;

        /// <summary>
        /// 公告正文。
        /// </summary>
        public string Content;

        /// <summary>
        /// 公告发布时间戳。
        /// </summary>
        public long PublishUnixTime;
    }

    /// <summary>
    /// 发送大厅全局聊天。
    /// </summary>
    [NetMsg(215, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_GlobalChat
    {
        /// <summary>
        /// 聊天内容。
        /// </summary>
        public string Content;
    }

    /// <summary>
    /// 大厅全局聊天同步。
    /// </summary>
    [NetMsg(216, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_GlobalChatSync
    {
        /// <summary>
        /// 发送者会话 Id。
        /// </summary>
        public string SenderSessionId;

        /// <summary>
        /// 发送者显示名。
        /// </summary>
        public string SenderDisplayName;

        /// <summary>
        /// 聊天内容。
        /// </summary>
        public string Content;

        /// <summary>
        /// 发送时间戳。
        /// </summary>
        public long SendUnixTime;
    }
}
