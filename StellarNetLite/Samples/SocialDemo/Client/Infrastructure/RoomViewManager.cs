using StellarNet.UI;
using StellarNet.View;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Game.Client.Components;
using StellarNet.Lite.Client.Components.Views;
using StellarNet.Lite.Game.Client.Views;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;

namespace StellarNet.Lite.Game.Client.Infrastructure
{
    /// <summary>
    /// 房间表现层总枢纽
    /// 监听房间进入事件，根据底层 Model (RoomComponent) 的能力，动态装配对应的 View (Spawner/Router)
    /// </summary>
    public class RoomViewManager : MonoSingleton<RoomViewManager>
    {
        /// <summary>
        /// 是否已注册全局进房事件。
        /// </summary>
        private bool _isInitialized;

        /// <summary>
        /// 当前房间表现层根节点。
        /// </summary>
        private GameObject _currentRoomViewRoot;

        /// <summary>
        /// 当前已绑定的房间实例。
        /// 用于检测回放房间切换。
        /// </summary>
        private ClientRoom _boundRoom;

        /// <summary>
        /// 注册房间表现层需要的全局事件。
        /// </summary>
        public void Init()
        {
            if (_isInitialized) return;
            GlobalTypeNetEvent.Register<S2C_RoomSetupResult>(OnRoomSetupResult).UnRegisterWhenGameObjectDestroyed(gameObject);
            GlobalTypeNetEvent.Register<S2C_ReconnectResult>(OnReconnectResult).UnRegisterWhenGameObjectDestroyed(gameObject);
            _isInitialized = true;
        }

        /// <summary>
        /// 监听房间状态，按需创建或销毁表现层根节点。
        /// </summary>
        private void Update()
        {
            if (_currentRoomViewRoot == null &&
                NetClient.State == ClientAppState.SandboxRoom &&
                NetClient.CurrentRoom != null)
            {
                BuildRoomView(NetClient.CurrentRoom);
                return;
            }

            if (_currentRoomViewRoot != null &&
                NetClient.State == ClientAppState.SandboxRoom &&
                NetClient.CurrentRoom != null &&
                !ReferenceEquals(_boundRoom, NetClient.CurrentRoom))
            {
                BuildRoomView(NetClient.CurrentRoom);
                return;
            }

            if (_currentRoomViewRoot != null && NetClient.State == ClientAppState.InLobby)
            {
                CleanupCurrentRoomView();
            }
        }

        /// <summary>
        /// 在线房间初始化成功后创建对应表现层。
        /// </summary>
        private void OnRoomSetupResult(S2C_RoomSetupResult evt)
        {
            if (evt == null || !evt.Success || NetClient.CurrentRoom == null) return;
            BuildRoomView(NetClient.CurrentRoom);
        }

        /// <summary>
        /// 重连成功后重新装配当前房间表现层。
        /// </summary>
        private void OnReconnectResult(S2C_ReconnectResult evt)
        {
            if (evt == null || !evt.Success || NetClient.CurrentRoom == null) return;
            BuildRoomView(NetClient.CurrentRoom);
        }

        /// <summary>
        /// 根据房间已挂载的业务组件动态创建表现层服务与路由。
        /// </summary>
        private void BuildRoomView(ClientRoom room)
        {
            if (room == null) return;

            CleanupCurrentRoomView();
            _boundRoom = room;

            // 1. 创建表现层根节点
            _currentRoomViewRoot = new GameObject($"[View] Room_{room.RoomId}");
            DontDestroyOnLoad(_currentRoomViewRoot);

            // 2. 装配通用表现层服务 (实体生成器)
            if (room.GetComponent<ClientObjectSyncComponent>() != null)
            {
                var spawner = _currentRoomViewRoot.AddComponent<ObjectSpawnerView>();
                spawner.Init(room);
            }

            // 3. 动态装配各业务组件的 UI 路由与控制器
            bool isOnline = NetClient.State == ClientAppState.OnlineRoom;

            var settingsComp = room.GetComponent<ClientRoomSettingsComponent>();
            if (settingsComp != null)
            {
                if (isOnline)
                {
                    var router = _currentRoomViewRoot.AddComponent<ClientRoomSettingsOnlineUIRouter>();
                    router.Bind(settingsComp);
                }
                else
                {
                    var router = _currentRoomViewRoot.AddComponent<ClientRoomSettingsReplayUIRouter>();
                    router.Bind(settingsComp);
                }
            }

            var socialComp = room.GetComponent<ClientSocialRoomComponent>();
            if (socialComp != null)
            {
                if (isOnline)
                {
                    var inputCtrl = _currentRoomViewRoot.AddComponent<SocialRoomInputController>();
                    inputCtrl.Init(room);

                    var router = _currentRoomViewRoot.AddComponent<SocialOnlineUIRouter>();
                    router.Bind(socialComp);
                }
                else
                {
                    var router = _currentRoomViewRoot.AddComponent<SocialReplayUIRouter>();
                    router.Bind(socialComp);
                }
            }
        }

        /// <summary>
        /// 销毁当前房间表现层，并清理相关房间 UI。
        /// </summary>
        private void CleanupCurrentRoomView()
        {
            // 销毁所有动态挂载的表现层组件 (Spawner, Routers, InputController)
            if (_currentRoomViewRoot != null)
            {
                Destroy(_currentRoomViewRoot);
                _currentRoomViewRoot = null;
            }

            _boundRoom = null;

            // 兜底清理所有房间面板
            UIKit.ClosePanel<Panel_StellarNetRoom>();
            UIKit.ClosePanel<Panel_SocialRoomView>();
            UIKit.ClosePanel<Panel_StellarNetGameOver>();
        }
    }
}
