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
        /// <summary>
        /// 服务端创建完成后挂入默认房间成员通知器。
        /// </summary>
        public override void OnServerAppCreated(StellarNetAppManager appManager, ServerApp serverApp)
        {
            if (serverApp == null)
            {
                return;
            }

            serverApp.RoomMembershipNotifier = DefaultRoomMembershipNotifier.Instance;
        }

        /// <summary>
        /// 服务端会话状态变化后刷新大厅在线玩家列表。
        /// </summary>
        public override void OnServerSessionStateChanged(StellarNetAppManager appManager, ServerApp serverApp, Session session)
        {
            if (serverApp == null)
            {
                return;
            }

            ServerLobbyModule.BroadcastOnlinePlayerList(serverApp);
        }

        /// <summary>
        /// 服务端踢人前向客户端发送踢下线通知。
        /// </summary>
        public override bool TryNotifyServerSessionKick(StellarNetAppManager appManager, ServerApp serverApp, Session session, string reason)
        {
            if (serverApp == null || session == null || !session.IsOnline)
            {
                return false;
            }

            serverApp.SendMessageToSession(session, new S2C_KickOut { Reason = reason ?? string.Empty });
            return true;
        }

        /// <summary>
        /// 客户端创建完成后注册默认流程必须放行的弱网豁免协议。
        /// </summary>
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

        /// <summary>
        /// 客户端断线恢复后自动补发登录请求。
        /// </summary>
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
