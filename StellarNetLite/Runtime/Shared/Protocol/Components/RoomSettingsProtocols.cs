using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    // 房间成员展示信息。
    public sealed class MemberInfo
    {
        // 成员所属会话 Id。
        public string SessionId;

        // 业务账号 Id。
        public string Uid;
        // UI 展示名。
        public string DisplayName;

        // 是否已准备。
        public bool IsReady;
        // 是否为当前房主。
        public bool IsOwner;
    }

    // 房间全量快照。
    [NetMsg(300, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_RoomSnapshot
    {
        // 房间名。
        public string RoomName;
        // 最大人数。
        public int MaxMembers;
        // 是否私有。
        public bool IsPrivate;
        // 当前所有成员信息。
        public MemberInfo[] Members;
    }

    // 成员加入通知。
    [NetMsg(301, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_MemberJoined
    {
        // 新加入成员的完整信息。
        public MemberInfo Member;
    }

    // 成员离开通知。
    [NetMsg(302, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_MemberLeft
    {
        // 离开成员的会话 Id。
        public string SessionId;
    }

    // 客户端切换自己的准备状态。
    [NetMsg(303, NetScope.Room, NetDir.C2S)]
    public sealed class C2S_SetReady
    {
        // 目标准备状态。
        public bool IsReady;
    }

    // 成员准备状态变化通知。
    [NetMsg(304, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_MemberReadyChanged
    {
        // 状态变化的成员。
        public string SessionId;
        // 最新准备状态。
        public bool IsReady;
    }
}
