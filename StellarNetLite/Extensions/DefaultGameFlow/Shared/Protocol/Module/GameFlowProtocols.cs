using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    [NetMsg(500, NetScope.Room, NetDir.C2S)]
    /// <summary>
    /// 房主请求开始游戏。
    /// </summary>
    public sealed class C2S_StartGame
    {
    }

    [NetMsg(501, NetScope.Room, NetDir.S2C)]
    /// <summary>
    /// 服务端广播游戏开始。
    /// </summary>
    public sealed class S2C_GameStarted
    {
        /// <summary>
        /// 开局时间戳。
        /// </summary>
        public long StartUnixTime;
    }

    [NetMsg(502, NetScope.Room, NetDir.C2S)]
    /// <summary>
    /// 房主请求结束游戏。
    /// </summary>
    public sealed class C2S_EndGame
    {
    }

    [NetMsg(503, NetScope.Room, NetDir.S2C)]
    /// <summary>
    /// 服务端广播结算结果。
    /// </summary>
    public sealed class S2C_GameEnded
    {
        /// <summary>
        /// 当前胜者或结束原因标识。
        /// </summary>
        public string WinnerSessionId;

        /// <summary>
        /// 刚生成的录像 Id。
        /// </summary>
        public string ReplayId;
    }
}
