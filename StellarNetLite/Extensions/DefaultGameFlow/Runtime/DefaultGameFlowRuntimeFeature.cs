using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Runtime;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Server.Infrastructure;
using StellarNet.Lite.Server.Modules;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;

namespace StellarNet.Lite.Extensions.DefaultGameFlow.Runtime
{
    /// <summary>
    /// 默认游戏流程运行时桥。
    /// 负责把登录重连链与大厅在线列表刷新从 Runtime 主体中下沉到扩展层。
    /// </summary>
    public sealed class DefaultGameFlowRuntimeFeature : RuntimeFeatureBridgeBase
    {
        public override void OnServerAppCreated(StellarNetAppManager appManager, ServerApp serverApp)
        {
            if (serverApp == null)
            {
                return;
            }

            serverApp.RoomMembershipNotifier = DefaultRoomMembershipNotifier.Instance;
        }

        public override void OnServerSessionStateChanged(StellarNetAppManager appManager, ServerApp serverApp, Session session)
        {
            if (serverApp == null)
            {
                return;
            }

            ServerLobbyModule.BroadcastOnlinePlayerList(serverApp);
        }

        public override bool TryNotifyServerSessionKick(StellarNetAppManager appManager, ServerApp serverApp, Session session, string reason)
        {
            if (serverApp == null || session == null || !session.IsOnline)
            {
                return false;
            }

            serverApp.SendMessageToSession(session, new S2C_KickOut { Reason = reason ?? string.Empty });
            return true;
        }

        public override void OnClientAppCreated(StellarNetAppManager appManager, ClientApp clientApp)
        {
            if (clientApp == null)
            {
                return;
            }

            clientApp.RegisterWeakNetBypassProtocol<C2S_Login>();
            clientApp.RegisterWeakNetBypassProtocol<C2S_ConfirmReconnect>();
            clientApp.RegisterWeakNetBypassProtocol<C2S_ReconnectReady>();
        }

        public override void OnClientConnected(StellarNetAppManager appManager, ClientApp clientApp)
        {
            if (clientApp == null || !clientApp.Session.IsReconnecting)
            {
                return;
            }

            if (string.IsNullOrEmpty(clientApp.Session.AccountId))
            {
                return;
            }

            clientApp.Session.IsPhysicalOnline = true;
            clientApp.SendMessage(new C2S_Login
            {
                AccountId = clientApp.Session.AccountId,
                ClientVersion = Application.version
            });
        }
    }
}
