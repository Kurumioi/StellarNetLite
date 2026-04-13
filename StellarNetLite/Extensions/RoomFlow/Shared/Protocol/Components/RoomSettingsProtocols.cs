using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    /// <summary>
    /// 房间成员信息。
    /// </summary>
    public sealed class MemberInfo
    {
        /// <summary>
        /// 成员 SessionId。
        /// </summary>
        public string SessionId;

        /// <summary>
        /// 成员账号 Id。
        /// </summary>
        public string AccountId;

        /// <summary>
        /// 成员显示名。
        /// </summary>
        public string DisplayName;

        /// <summary>
        /// 成员是否已准备。
        /// </summary>
        public bool IsReady;

        /// <summary>
        /// 成员是否为房主。
        /// </summary>
        public bool IsOwner;
    }

    /// <summary>
    /// 房间全量快照。
    /// </summary>
    [NetMsg(300, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_RoomSnapshot
    {
        /// <summary>
        /// 房间名。
        /// </summary>
        public string RoomName;

        /// <summary>
        /// 房间最大人数。
        /// </summary>
        public int MaxMembers;

        /// <summary>
        /// 是否为私有房。
        /// </summary>
        public bool IsPrivate;

        /// <summary>
        /// 当前成员列表。
        /// </summary>
        public MemberInfo[] Members;
    }

    /// <summary>
    /// 成员加入通知。
    /// </summary>
    [NetMsg(301, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_MemberJoined
    {
        /// <summary>
        /// 刚加入的成员信息。
        /// </summary>
        public MemberInfo Member;
    }

    /// <summary>
    /// 成员离开通知。
    /// </summary>
    [NetMsg(302, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_MemberLeft
    {
        /// <summary>
        /// 离开的成员 SessionId。
        /// </summary>
        public string SessionId;
    }

    /// <summary>
    /// 设置准备状态请求。
    /// </summary>
    [NetMsg(303, NetScope.Room, NetDir.C2S)]
    public sealed class C2S_SetReady
    {
        /// <summary>
        /// 目标准备状态。
        /// </summary>
        public bool IsReady;
    }

    /// <summary>
    /// 成员准备状态变更通知。
    /// </summary>
    [NetMsg(304, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_MemberReadyChanged
    {
        /// <summary>
        /// 变更成员 SessionId。
        /// </summary>
        public string SessionId;

        /// <summary>
        /// 最新准备状态。
        /// </summary>
        public bool IsReady;
    }
}
