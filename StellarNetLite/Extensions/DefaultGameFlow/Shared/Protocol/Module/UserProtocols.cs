using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    /// <summary>
    /// 登录请求。
    /// </summary>
    [NetMsg(100, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_Login
    {
        /// <summary>
        /// 客户端账号 Id。
        /// </summary>
        public string AccountId;

        /// <summary>
        /// 客户端版本号。
        /// </summary>
        public string ClientVersion;
    }

    /// <summary>
    /// 登录结果。
    /// </summary>
    [NetMsg(101, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_LoginResult
    {
        /// <summary>
        /// 是否登录成功。
        /// </summary>
        public bool Success;

        /// <summary>
        /// 服务端分配的 SessionId。
        /// </summary>
        public string SessionId;

        /// <summary>
        /// 是否存在可恢复房间。
        /// </summary>
        public bool HasReconnectRoom;

        /// <summary>
        /// 失败原因。
        /// </summary>
        public string Reason;
    }

    /// <summary>
    /// 顶号或强制下线通知。
    /// </summary>
    [NetMsg(102, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_KickOut
    {
        /// <summary>
        /// 踢下线原因。
        /// </summary>
        public string Reason;
    }

    /// <summary>
    /// 重连确认请求。
    /// </summary>
    [NetMsg(103, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_ConfirmReconnect
    {
        /// <summary>
        /// 是否接受重连。
        /// </summary>
        public bool Accept;
    }

    /// <summary>
    /// 重连结果。
    /// </summary>
    [NetMsg(104, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_ReconnectResult
    {
        /// <summary>
        /// 是否重连成功。
        /// </summary>
        public bool Success;

        /// <summary>
        /// 可恢复的房间 Id。
        /// </summary>
        public string RoomId;

        /// <summary>
        /// 重连后需要装配的组件清单。
        /// </summary>
        public int[] ComponentIds;

        /// <summary>
        /// 失败原因。
        /// </summary>
        public string Reason;
    }

    /// <summary>
    /// 客户端重连装配完成确认。
    /// </summary>
    [NetMsg(105, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_ReconnectReady
    {
    }
}
