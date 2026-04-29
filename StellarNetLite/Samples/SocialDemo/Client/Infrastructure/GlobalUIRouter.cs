using StellarNet.UI;
using StellarNet.View;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;

namespace StellarNet.Lite.Game.Client.Infrastructure
{
    /// <summary>
    /// Demo 全局 UI 路由。
    /// </summary>
    public class GlobalUIRouter : MonoSingleton<GlobalUIRouter>
    {
        /// <summary>
        /// 上一帧记录的客户端状态。
        /// 用于检测是否从房间态跌回大厅态。
        /// </summary>
        private ClientAppState _lastClientState = ClientAppState.InLobby;

        /// <summary>
        /// 是否已经完成一次事件注册。
        /// </summary>
        private bool _isInitialized;

        /// <summary>
        /// 注册全局 UI 相关的网络事件。
        /// </summary>
        public void Init()
        {
            if (_isInitialized)
            {
                return;
            }

            GlobalTypeNetEvent.Register<S2C_RoomSetupResult>(OnRoomSetupResult).UnRegisterWhenGameObjectDestroyed(gameObject);
            GlobalTypeNetEvent.Register<S2C_LoginResult>(OnLoginResult).UnRegisterWhenGameObjectDestroyed(gameObject);
            GlobalTypeNetEvent.Register<S2C_ReconnectResult>(OnReconnectResult)
                .UnRegisterWhenGameObjectDestroyed(gameObject);
            GlobalTypeNetEvent.Register<S2C_KickOut>(OnKickOut).UnRegisterWhenGameObjectDestroyed(gameObject);
            GlobalTypeNetEvent.Register<S2C_DownloadReplayResult>(OnReplayDownloaded)
                .UnRegisterWhenGameObjectDestroyed(gameObject);

            _isInitialized = true;
            NetLogger.LogInfo("GlobalUIRouter", "全局 UI 路由初始化完成");
        }

        /// <summary>
        /// 重置路由器本地状态。
        /// </summary>
        protected override void OnDestroy()
        {
            base.OnDestroy();
            _isInitialized = false;
            _lastClientState = ClientAppState.InLobby;
        }

        /// <summary>
        /// 监听客户端状态变化，并在房间退出后回退到正确面板。
        /// </summary>
        private void Update()
        {
            if (NetClient.App == null)
            {
                return;
            }

            ClientAppState currentState = NetClient.State;

            bool isDroppedFromRoom =
                (_lastClientState == ClientAppState.OnlineRoom ||
                 _lastClientState == ClientAppState.ConnectionSuspended ||
                 _lastClientState == ClientAppState.SandboxRoom) &&
                currentState == ClientAppState.InLobby;

            if (isDroppedFromRoom)
            {
                NetLogger.LogWarning("GlobalUIRouter", "检测到状态跌落，执行 UI 回退");
                UIKit.ClosePanel<Panel_StellarNetReplay>();
                UIKit.ClosePanel<Panel_SetRoomConfig>();
                UIKit.ClosePanel<Panel_StellarNetRoom>();
                UIKit.ClosePanel<Panel_StellarNetGameOver>();

                if (NetClient.Session != null && NetClient.Session.IsLoggedIn)
                {
                    UIKit.OpenPanel<Panel_StellarNetLobby>(new Panel_StellarNetLobbyData
                        { accountId = NetClient.Session.AccountId });
                }
                else
                {
                    UIKit.ClosePanel<Panel_StellarNetLobby>();
                    UIKit.OpenPanel<Panel_StellarNetLogin>();
                }
            }

            _lastClientState = currentState;
        }

        /// <summary>
        /// 处理物理断线后的 UI 回退。
        /// </summary>
        public void HandlePhysicalDisconnect()
        {
            UIKit.CloseAllPanels();
            UIKit.OpenPanel<Panel_GlobalNetMonitor>();
            UIKit.OpenPanel<Panel_StellarNetLogin>();
        }

        /// <summary>
        /// 登录成功后切入大厅界面。
        /// </summary>
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
                accountId = NetClient.Session != null ? NetClient.Session.AccountId : string.Empty
            });
        }

        /// <summary>
        /// 处理重连确认结果，成功时保持房间流转，失败时回到大厅。
        /// </summary>
        private void OnReconnectResult(S2C_ReconnectResult msg)
        {
            if (msg == null)
            {
                NetLogger.LogError("GlobalUIRouter", "处理重连结果失败: msg 为空");
                return;
            }

            if (msg.Success)
            {
                UIKit.ClosePanel<Panel_StellarNetLogin>();
                UIKit.ClosePanel<Panel_StellarNetLobby>();
                UIKit.ClosePanel<Panel_SetRoomConfig>();
                return;
            }

            UIKit.ClosePanel<Panel_StellarNetLogin>();
            UIKit.OpenPanel<Panel_StellarNetLobby>(new Panel_StellarNetLobbyData
            {
                accountId = NetClient.Session != null ? NetClient.Session.AccountId : string.Empty
            });
        }

        /// <summary>
        /// 被服务端踢下线后回到登录页。
        /// </summary>
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

        /// <summary>
        /// 建房或进房确认成功后关闭大厅侧面板。
        /// </summary>
        private void OnRoomSetupResult(S2C_RoomSetupResult evt)
        {
            if (evt == null || !evt.Success)
            {
                return;
            }

            UIKit.ClosePanel<Panel_StellarNetLogin>();
            UIKit.ClosePanel<Panel_StellarNetLobby>();
            UIKit.ClosePanel<Panel_SetRoomConfig>();
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
