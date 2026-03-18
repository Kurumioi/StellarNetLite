using UnityEngine;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Components.Views;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Game.Client.Components;
using StellarNet.Lite.Client.Core.Events;

namespace StellarNet.Lite.Game.Client.Views
{
    /// <summary>
    /// 交友房间场景生命周期入口 (View层)
    /// 核心修复 P1-5：拆分 Controller 与 View，严格遵循 MSV。
    /// </summary>
    public class SocialRoomEntry : MonoBehaviour
    {
        [Header("核心表现层组件")] 
        public ObjectSpawnerView SpawnerView;
        public SocialRoomView RoomView;
        
        [Header("核心控制层组件")]
        public SocialRoomInputController InputController;

        private ClientRoom _boundRoom;

        private void Start()
        {
            if (SpawnerView == null) SpawnerView = gameObject.AddComponent<ObjectSpawnerView>();
            if (RoomView == null) RoomView = gameObject.AddComponent<SocialRoomView>();
            if (InputController == null) InputController = gameObject.AddComponent<SocialRoomInputController>();

            GlobalTypeNetEvent.Register<Local_RoomEntered>(OnRoomEntered).UnRegisterWhenGameObjectDestroyed(gameObject);
            GlobalTypeNetEvent.Register<Local_RoomLeft>(OnRoomLeft).UnRegisterWhenGameObjectDestroyed(gameObject);
        }

        private void OnRoomEntered(Local_RoomEntered evt)
        {
            if (evt.Room == null) return;
            if (evt.Room.GetComponent<ClientSocialRoomComponent>() == null) return;

            _boundRoom = evt.Room;
            NetLogger.LogInfo("SocialRoomEntry", $"检测到进入交友房间 {_boundRoom.RoomId}，执行表现与控制层初始化");

            SpawnerView.Init(_boundRoom);
            RoomView.Init(_boundRoom);
            InputController.Init(_boundRoom);
        }

        private void OnRoomLeft(Local_RoomLeft evt)
        {
            if (_boundRoom != null)
            {
                _boundRoom = null;
                SpawnerView.Clear();
                RoomView.Clear(evt.IsSuspended);
                InputController.Clear();
            }
        }
    }
}
