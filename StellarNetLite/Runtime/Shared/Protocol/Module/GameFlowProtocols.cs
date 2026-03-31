using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    // 房主请求开始游戏。
    [NetMsg(500, NetScope.Room, NetDir.C2S)]
    public sealed class C2S_StartGame
    {
    }

    // 服务端广播开局时间。
    [NetMsg(501, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_GameStarted
    {
        // UTC 秒时间戳，客户端可用于表现同步。
        public long StartUnixTime;
    }

    // 房主请求结束游戏。
    [NetMsg(502, NetScope.Room, NetDir.C2S)]
    public sealed class C2S_EndGame
    {
    }

    // 服务端广播结算与录像信息。
    [NetMsg(503, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_GameEnded
    {
        // 当前实现里可表示胜者，也可表示结束原因。
        public string WinnerSessionId;

        // 刚生成的录像 Id，供大厅或结算页继续处理。
        public string ReplayId;
    }
}
