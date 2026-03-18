using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    [NetMsg(500, NetScope.Room, NetDir.C2S)]
    public sealed class C2S_StartGame
    {
    }

    [NetMsg(501, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_GameStarted
    {
        public long StartUnixTime;
    }

    [NetMsg(502, NetScope.Room, NetDir.C2S)]
    public sealed class C2S_EndGame
    {
    }

    [NetMsg(503, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_GameEnded
    {
        public string WinnerSessionId;

        // 核心新增：将刚刚落盘的录像 ID 下发给客户端，供房主重命名使用
        public string ReplayId;
    }
}