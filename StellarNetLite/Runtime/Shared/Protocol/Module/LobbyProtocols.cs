using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    // 大厅协议。
    // 负责房间列表、在线玩家列表同步、大厅聊天与公告。

    /// <summary>
    /// 房间简要信息数据结构 (DTO)
    /// </summary>
    public class RoomBriefInfo
    {
        public string RoomId;
        public string RoomName;
        public int MemberCount;
        public int MaxMembers; // 扁平化透传给大厅 UI
        public bool IsPrivate; // 仅告诉客户端是否有密码，绝不传输真实密码
        public int State; // 0: Waiting, 1: Playing, 2: Finished
    }

    /// <summary>
    /// 全局玩家状态信息
    /// </summary>
    public sealed class OnlinePlayerInfo
    {
        // 在线玩家唯一会话 Id。
        public string SessionId;
        public string Uid;
        public string DisplayName;
        public bool IsOnline; // 是否物理在线（false 表示处于断线挂起状态）
        public bool IsInRoom;
        public string RoomId;
    }

    /// <summary>
    /// 客户端请求获取大厅房间列表
    /// </summary>
    [NetMsg(210, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_GetRoomList
    {
    }

    /// <summary>
    /// 服务端下发大厅房间列表
    /// </summary>
    [NetMsg(211, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_RoomListResponse
    {
        public RoomBriefInfo[] Rooms;
    }

    /// <summary>
    /// 服务端下发全量玩家列表（仅在初次登录或重连大厅时下发）
    /// </summary>
    [NetMsg(212, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_OnlinePlayerListSync
    {
        public OnlinePlayerInfo[] Players;
    }

    /// <summary>
    /// 服务端下发单点玩家状态增量同步（状态变更或被 GC 删除）
    /// </summary>
    [NetMsg(213, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_GlobalPlayerStateIncrementalSync
    {
        public bool IsRemoved; // 当服务端触发 Session GC 彻底删除玩家时，此值为 true
        public OnlinePlayerInfo Player;
    }

    /// <summary>
    /// 服务端主动下发全局公告
    /// </summary>
    [NetMsg(214, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_GlobalAnnouncement
    {
        public string Title;
        public string Content;
        public long PublishUnixTime;
    }

    /// <summary>
    /// 客户端发起大厅聊天请求
    /// </summary>
    [NetMsg(215, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_GlobalChat
    {
        public string Content;
    }

    /// <summary>
    /// 服务端广播大厅聊天消息
    /// </summary>
    [NetMsg(216, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_GlobalChatSync
    {
        public string SenderSessionId;
        public string SenderDisplayName;
        public string Content;
        public long SendUnixTime;
    }
}