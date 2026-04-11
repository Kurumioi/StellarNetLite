using StellarNet.UI;
using StellarNet.View;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Game.Client.Components;
using StellarNet.Lite.Client.Components.Views;
using StellarNet.Lite.Game.Client.Views;
using UnityEngine;

namespace StellarNet.Lite.Game.Client.Infrastructure
{
    /// <summary>
    /// 房间表现层总枢纽
    /// 监听房间进入事件，根据底层 Model (RoomComponent) 的能力，动态装配对应的 View (Spawner/Router)
    /// </summary>
    public class RoomViewManager : MonoSingleton<RoomViewManager>
    {
        private bool _isInitialized;
        private GameObject _currentRoomViewRoot;

        public void Init()
        {
            if (_isInitialized) return;
            GlobalTypeNetEvent.Register<Local_RoomEntered>(OnRoomEntered).UnRegisterWhenGameObjectDestroyed(gameObject);
            GlobalTypeNetEvent.Register<Local_RoomLeft>(OnRoomLeft).UnRegisterWhenGameObjectDestroyed(gameObject);
            _isInitialized = true;
        }

        private void OnRoomEntered(Local_RoomEntered evt)
        {
            if (evt.Room == null) return;

            // 1. 创建表现层根节点
            _currentRoomViewRoot = new GameObject($"[View] Room_{evt.Room.RoomId}");
            DontDestroyOnLoad(_currentRoomViewRoot);

            // 2. 装配通用表现层服务 (实体生成器)
            if (evt.Room.GetComponent<ClientObjectSyncComponent>() != null)
            {
                var spawner = _currentRoomViewRoot.AddComponent<ObjectSpawnerView>();
                spawner.Init(evt.Room);
            }

            // 3. 动态装配各业务组件的 UI 路由与控制器
            bool isOnline = NetClient.State == ClientAppState.OnlineRoom;

            var settingsComp = evt.Room.GetComponent<ClientRoomSettingsComponent>();
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

            var socialComp = evt.Room.GetComponent<ClientSocialRoomComponent>();
            if (socialComp != null)
            {
                if (isOnline)
                {
                    var inputCtrl = _currentRoomViewRoot.AddComponent<SocialRoomInputController>();
                    inputCtrl.Init(evt.Room);

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

        private void OnRoomLeft(Local_RoomLeft evt)
        {
            // 销毁所有动态挂载的表现层组件 (Spawner, Routers, InputController)
            if (_currentRoomViewRoot != null)
            {
                Destroy(_currentRoomViewRoot);
                _currentRoomViewRoot = null;
            }

            // 兜底清理所有房间面板
            UIKit.ClosePanel<Panel_StellarNetRoom>();
            UIKit.ClosePanel<Panel_SocialRoomView>();
            UIKit.ClosePanel<Panel_StellarNetGameOver>();
        }
    }
}