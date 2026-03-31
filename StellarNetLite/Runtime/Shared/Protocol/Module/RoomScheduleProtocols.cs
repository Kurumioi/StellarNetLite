using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    // 房间调度协议。
    // 负责建房、加房、离房和房间装配握手。

    [NetMsg(200, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_CreateRoom
    {
        // 房间展示名。
        public string RoomName;
        // 客户端选中的组件模板。
        public int[] ComponentIds;
        public int MaxMembers; // 客户端请求的配置字段

        public string Password; // 客户端请求的密码
        // 架构说明：底层网络已实现基于 Seq 的防重放机制，业务层不再需要手动传递 Token 保证幂等性。
    }

    [NetMsg(201, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_CreateRoomResult
    {
        public bool Success;
        public string RoomId;
        public int[] ComponentIds;
        public string Reason;
    }

    [NetMsg(202, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_JoinRoom
    {
        public string RoomId;
        public string Password; // 加入时携带密码进行校验
    }

    [NetMsg(203, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_JoinRoomResult
    {
        public bool Success;
        public string RoomId;
        public int[] ComponentIds;
        public string Reason;
    }

    [NetMsg(204, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_LeaveRoom
    {
    }

    [NetMsg(205, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_LeaveRoomResult
    {
        public bool Success;
    }

    [NetMsg(206, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_RoomSetupReady
    {
        // 客户端本地装配完成的目标房间。
        public string RoomId;
    }
}
