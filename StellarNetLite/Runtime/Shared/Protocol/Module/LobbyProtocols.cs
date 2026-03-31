using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    // 大厅协议。
    // 负责房间列表和在线玩家列表同步。

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

    public sealed class OnlinePlayerInfo
    {
        // 在线玩家唯一会话 Id。
        public string SessionId;
        public string Uid;
        public string DisplayName;
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

    [NetMsg(212, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_OnlinePlayerListSync
    {
        public OnlinePlayerInfo[] Players;
    }
}
