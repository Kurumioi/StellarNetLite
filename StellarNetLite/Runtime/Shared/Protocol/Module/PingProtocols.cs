using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    [NetMsg(10, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_Ping
    {
        public float ClientTime;
    }

    [NetMsg(11, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_Pong
    {
        public float ClientTime;
    }
}