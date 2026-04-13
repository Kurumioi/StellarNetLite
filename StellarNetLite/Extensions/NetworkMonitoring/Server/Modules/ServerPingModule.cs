using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Server.Modules
{
    /// <summary>
    /// 服务端 Ping 模块。
    /// 负责原样回包 Pong。
    /// </summary>
    [ServerModule("ServerPingModule", "全局延迟心跳模块")]
    public sealed class ServerPingModule
    {
        private readonly ServerApp _app;

        public ServerPingModule(ServerApp app)
        {
            _app = app;
        }

        [NetHandler]
        public void OnC2S_Ping(Session session, C2S_Ping msg)
        {
            _app.SendMessageToSession(session, new S2C_Pong { ClientTime = msg.ClientTime });
        }
    }
}
