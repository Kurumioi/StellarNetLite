using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    /// <summary>
    /// 用户域协议。
    /// 负责登录、顶号通知和断线恢复握手。
    /// </summary>
    [NetMsg(100, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_Login
    {
        // 客户端账号 Id。
        public string AccountId;

        // 核心新增 (Point 18)：强制要求客户端上报版本号
        public string ClientVersion;
    }

    [NetMsg(101, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_LoginResult
    {
        // 是否登录成功。
        public bool Success;
        // 服务端正式 SessionId。
        public string SessionId;
        // 是否存在可恢复房间。
        public bool HasReconnectRoom;
        public string Reason;
    }

    [NetMsg(102, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_KickOut
    {
        public string Reason;
    }

    [NetMsg(103, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_ConfirmReconnect
    {
        public bool Accept;
    }

    [NetMsg(104, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_ReconnectResult
    {
        public bool Success;
        public string RoomId;
        public int[] ComponentIds;
        public string Reason;
    }

    [NetMsg(105, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_ReconnectReady
    {
    }
}
