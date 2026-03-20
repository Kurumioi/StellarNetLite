using StellarFramework;
using StellarFramework.UI;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;

namespace StellarNet.Lite.Game.Client.Infrastructure
{
    public class GlobalUIRouter : MonoSingleton<GlobalUIRouter>
    {
        private ClientAppState _lastClientState = ClientAppState.InLobby;
        private bool _isInitialized;

        public void Init()
        {
            if (_isInitialized)
            {
                return;
            }


            GlobalTypeNetEvent.Register<Local_RoomEntered>(OnRoomEntered).UnRegisterWhenGameObjectDestroyed(gameObject);
            GlobalTypeNetEvent.Register<Local_RoomLeft>(OnRoomLeft).UnRegisterWhenGameObjectDestroyed(gameObject);
            GlobalTypeNetEvent.Register<S2C_LoginResult>(OnLoginResult).UnRegisterWhenGameObjectDestroyed(gameObject);
            GlobalTypeNetEvent.Register<S2C_ReconnectResult>(OnReconnectResult).UnRegisterWhenGameObjectDestroyed(gameObject);
            GlobalTypeNetEvent.Register<S2C_KickOut>(OnKickOut).UnRegisterWhenGameObjectDestroyed(gameObject);
            GlobalTypeNetEvent.Register<S2C_DownloadReplayResult>(OnReplayDownloaded).UnRegisterWhenGameObjectDestroyed(gameObject);

            _isInitialized = true;
            NetLogger.LogInfo("GlobalUIRouter", "全局 UI 路由初始化完成");
        }


        protected override void OnDestroy()
        {
            base.OnDestroy();


            _isInitialized = false;
            _lastClientState = ClientAppState.InLobby;
        }

        private void Update()
        {
            if (NetClient.App == null)
            {
                return;
            }

            ClientAppState currentState = NetClient.State;
            bool isDroppedFromRoom =
                (_lastClientState == ClientAppState.OnlineRoom || _lastClientState == ClientAppState.ConnectionSuspended) &&
                currentState == ClientAppState.InLobby;

            if (isDroppedFromRoom)
            {
                NetLogger.LogWarning("GlobalUIRouter", "检测到状态跌落，执行 UI 回退");
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
            if (msg == null)
            {
                NetLogger.LogError("GlobalUIRouter", "处理登录结果失败: msg 为空");
                return;
            }

            if (!msg.Success)
            {
                return;
            }

            if (msg.HasReconnectRoom)
            {
                return;
            }

            UIKit.ClosePanel<Panel_StellarNetLogin>();
            UIKit.OpenPanel<Panel_StellarNetLobby>(new Panel_StellarNetLobbyData
            {
                uid = NetClient.Session != null ? NetClient.Session.SessionId : string.Empty
            });
        }

        private void OnReconnectResult(S2C_ReconnectResult msg)
        {
            if (msg == null)
            {
                NetLogger.LogError("GlobalUIRouter", "处理重连结果失败: msg 为空");
                return;
            }

            if (msg.Success)
            {
                return;
            }

            UIKit.ClosePanel<Panel_StellarNetLogin>();
            UIKit.OpenPanel<Panel_StellarNetLobby>(new Panel_StellarNetLobbyData
            {
                uid = NetClient.Session != null ? NetClient.Session.SessionId : string.Empty
            });
        }

        private void OnKickOut(S2C_KickOut msg)
        {
            if (msg == null)
            {
                NetLogger.LogError("GlobalUIRouter", "处理踢下线失败: msg 为空");
                return;
            }

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
            if (evt.IsSilent)
            {
                return;
            }

            if (evt.IsSuspended)
            {
                return;
            }

            NetLogger.LogInfo("GlobalUIRouter", "离开房间，回退至大厅");
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

        private void OnReplayDownloaded(S2C_DownloadReplayResult msg)
        {
            if (msg == null)
            {
                NetLogger.LogError("GlobalUIRouter", "处理录像下载结果失败: msg 为空");
                return;
            }

            if (!msg.Success || string.IsNullOrEmpty(msg.ReplayFileData))
            {
                return;
            }

            UIKit.ClosePanel<Panel_StellarNetLobby>();
            UIKit.OpenPanel<Panel_StellarNetReplay>(msg.ReplayFileData);
        }
    }
}