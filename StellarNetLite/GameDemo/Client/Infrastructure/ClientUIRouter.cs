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
    public class ClientUIRouter : MonoSingleton<ClientUIRouter>
    {
        private ClientAppState _lastClientState = ClientAppState.InLobby;
        private bool _isInitialized = false;
        private IUnRegister _roomGameEndedToken;

        public void Init()
        {
            if (_isInitialized) return;

            GlobalTypeNetEvent.Register<Local_RoomEntered>(OnRoomEntered).UnRegisterWhenGameObjectDestroyed(gameObject);
            GlobalTypeNetEvent.Register<Local_RoomLeft>(OnRoomLeft).UnRegisterWhenGameObjectDestroyed(gameObject);
            GlobalTypeNetEvent.Register<S2C_LoginResult>(OnLoginResult).UnRegisterWhenGameObjectDestroyed(gameObject);
            GlobalTypeNetEvent.Register<S2C_ReconnectResult>(OnReconnectResult).UnRegisterWhenGameObjectDestroyed(gameObject);
            GlobalTypeNetEvent.Register<S2C_DownloadReplayResult>(OnReplayDownloaded).UnRegisterWhenGameObjectDestroyed(gameObject);
            GlobalTypeNetEvent.Register<S2C_KickOut>(OnKickOut).UnRegisterWhenGameObjectDestroyed(gameObject);

            _isInitialized = true;
            NetLogger.LogInfo("ClientUIRouter", "统一 UI 路由中心初始化完毕，已接管全局跳转");
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _roomGameEndedToken?.UnRegister();
            _roomGameEndedToken = null;
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
                NetLogger.LogWarning("ClientUIRouter", "检测到网络状态跌落，执行 UI 路由回退");
                UIKit.ClosePanel<Panel_StellarNetRoom>();
                UIKit.ClosePanel<Panel_StellarNetGameOver>();
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
            if (NetClient.State == ClientAppState.OnlineRoom)
            {
                UIKit.ClosePanel<Panel_StellarNetLogin>();
                UIKit.ClosePanel<Panel_StellarNetLobby>();
                UIKit.ClosePanel<Panel_SetRoomConfig>();
                UIKit.ClosePanel<Panel_StellarNetGameOver>();
                UIKit.OpenPanel<Panel_StellarNetRoom>();

                _roomGameEndedToken?.UnRegister();
                if (evt.Room != null)
                {
                    _roomGameEndedToken = evt.Room.NetEventSystem.Register<S2C_GameEnded>(OnGameEnded);
                }
            }
        }

        private void OnGameEnded(S2C_GameEnded msg)
        {
            if (NetClient.State == ClientAppState.OnlineRoom)
            {
                UIKit.OpenPanel<Panel_StellarNetGameOver>(msg);
            }
        }

        private void OnRoomLeft(Local_RoomLeft evt)
        {
            _roomGameEndedToken?.UnRegister();
            _roomGameEndedToken = null;

            // 核心修复：如果是静默离开（如回放重置），直接阻断，不执行任何 UI 调整
            if (evt.IsSilent) return;

            if (!evt.IsSuspended)
            {
                NetLogger.LogInfo("ClientUIRouter", "离开房间，执行全局 UI 路由");
                UIKit.ClosePanel<Panel_StellarNetRoom>();
                UIKit.ClosePanel<Panel_StellarNetGameOver>();
                UIKit.ClosePanel<Panel_StellarNetReplay>();

                // 核心修复：增加强制兜底。如果已登录回大厅，否则回登录，杜绝空场景
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
                try
                {
                    var replayFile = Newtonsoft.Json.JsonConvert.DeserializeObject<ReplayFile>(msg.ReplayFileData);
                    if (replayFile != null)
                    {
                        UIKit.ClosePanel<Panel_StellarNetLobby>();
                        UIKit.OpenPanel<Panel_StellarNetReplay>(replayFile);
                    }
                }
                catch (System.Exception e)
                {
                    NetLogger.LogError("ClientUIRouter", $"录像解析异常: {e.Message}");
                }
            }
        }
    }
}