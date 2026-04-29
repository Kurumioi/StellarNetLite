using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    /// <summary>
    /// 客户端发起的 Ping 请求。
    /// </summary>
    [NetMsg(10, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_Ping
    {
        /// <summary>
        /// 客户端发送 Ping 时的本地时间。
        /// </summary>
        public float ClientTime;
    }

    /// <summary>
    /// 服务端返回的 Pong 响应。
    /// </summary>
    [NetMsg(11, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_Pong
    {
        /// <summary>
        /// 原样回传的客户端时间戳。
        /// 客户端据此估算 RTT。
        /// </summary>
        public float ClientTime;
    }
}
