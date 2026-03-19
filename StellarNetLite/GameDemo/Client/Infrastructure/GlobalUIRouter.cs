using UnityEngine;
using StellarFramework;
using StellarFramework.UI;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Game.Client.Infrastructure
{
    public class GlobalUIRouter : MonoSingleton<GlobalUIRouter>
    {
        private ClientAppState _lastClientState = ClientAppState.InLobby;
        private bool _isInitialized = false;

        public void Init()
        {
            if (_isInitialized) return;

            GlobalTypeNetEvent.Register<Local_RoomEntered>(OnRoomEntered).UnRegisterWhenGameObjectDestroyed(gameObject);
            GlobalTypeNetEvent.Register<Local_RoomLeft>(OnRoomLeft).UnRegisterWhenGameObjectDestroyed(gameObject);
            GlobalTypeNetEvent.Register<S2C_LoginResult>(OnLoginResult).UnRegisterWhenGameObjectDestroyed(gameObject);
            GlobalTypeNetEvent.Register<S2C_ReconnectResult>(OnReconnectResult).UnRegisterWhenGameObjectDestroyed(gameObject);
            GlobalTypeNetEvent.Register<S2C_KickOut>(OnKickOut).UnRegisterWhenGameObjectDestroyed(gameObject);
            GlobalTypeNetEvent.Register<S2C_DownloadReplayResult>(OnReplayDownloaded).UnRegisterWhenGameObjectDestroyed(gameObject);

            _isInitialized = true;
            NetLogger.LogInfo("GlobalUIRouter", "全局 UI 路由中心初始化完毕，已接管大厅与登录流转");
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _isInitialized = false;
            _lastClientState = ClientAppState.InLobby;
        }

        private void Update()
        {
            if (NetClient.App == null) return;

            var currentState = NetClient.State;

            bool isDroppedFromRoom = (_lastClientState == ClientAppState.OnlineRoom || _lastClientState == ClientAppState.ConnectionSuspended)
                                     && currentState == ClientAppState.InLobby;

            if (isDroppedFromRoom)
            {
                NetLogger.LogWarning("GlobalUIRouter", "检测到网络状态跌落，执行全局 UI 路由回退");
                UIKit.ClosePanel<Panel_SetRoomConfig>();

                if (NetClient.Session != null && NetClient.Session.IsLoggedIn)
                {
                    UIKit.OpenPanel<Panel_StellarNetLobby>(new Panel_StellarNetLobbyData { uid = NetClient.Session.SessionId });
                }
                else
                {
                    UIKit.ClosePanel<Panel_StellarNetLobby>();
                    UIKit.OpenPanel<Panel_StellarNetLogin>();
                }
            }

            _lastClientState = currentState;
        }

        public void HandlePhysicalDisconnect()
        {
            UIKit.CloseAllPanels();
            UIKit.OpenPanel<Panel_GlobalNetMonitor>();
            UIKit.OpenPanel<Panel_StellarNetLogin>();
        }

        private void OnLoginResult(S2C_LoginResult msg)
        {
            if (!msg.Success) return;

            if (!msg.HasReconnectRoom)
            {
                UIKit.ClosePanel<Panel_StellarNetLogin>();
                UIKit.OpenPanel<Panel_StellarNetLobby>(new Panel_StellarNetLobbyData { uid = NetClient.Session.SessionId });
            }
        }

        private void OnReconnectResult(S2C_ReconnectResult msg)
        {
            if (!msg.Success)
            {
                UIKit.ClosePanel<Panel_StellarNetLogin>();
                UIKit.OpenPanel<Panel_StellarNetLobby>(new Panel_StellarNetLobbyData { uid = NetClient.Session.SessionId });
            }
        }

        private void OnKickOut(S2C_KickOut msg)
        {
            UIKit.CloseAllPanels();
            UIKit.OpenPanel<Panel_GlobalNetMonitor>();
            UIKit.OpenPanel<Panel_StellarNetLogin>();
        }

        private void OnRoomEntered(Local_RoomEntered evt)
        {
            UIKit.ClosePanel<Panel_StellarNetLogin>();
            UIKit.ClosePanel<Panel_StellarNetLobby>();
            UIKit.ClosePanel<Panel_SetRoomConfig>();
        }

        private void OnRoomLeft(Local_RoomLeft evt)
        {
            if (evt.IsSilent) return;

            if (!evt.IsSuspended)
            {
                NetLogger.LogInfo("GlobalUIRouter", "离开房间，执行全局 UI 路由回退至大厅");

                // 修复：防御性关闭回放面板，确保任何异常离房都能清理 UI 栈
                UIKit.ClosePanel<Panel_StellarNetReplay>();

                if (NetClient.Session != null && NetClient.Session.IsLoggedIn)
                {
                    UIKit.OpenPanel<Panel_StellarNetLobby>(new Panel_StellarNetLobbyData { uid = NetClient.Session.SessionId });
                }
                else
                {
                    UIKit.OpenPanel<Panel_StellarNetLogin>();
                }
            }
        }

        private void OnReplayDownloaded(S2C_DownloadReplayResult msg)
        {
            if (msg.Success && !string.IsNullOrEmpty(msg.ReplayFileData))
            {
                UIKit.ClosePanel<Panel_StellarNetLobby>();
                UIKit.OpenPanel<Panel_StellarNetReplay>(msg.ReplayFileData);
            }
        }
    }
}