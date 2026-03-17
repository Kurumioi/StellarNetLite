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
    /// 职责：感知房间状态机的切换，动态为 ObjectSpawnerView 注入当前房间上下文。
    /// </summary>
    public class SocialRoomEntry : MonoBehaviour
    {
        [Header("核心表现层组件")] 
        [Tooltip("负责根据网络协议自动实例化与销毁 Prefab")]
        public ObjectSpawnerView SpawnerView;
        
        [Tooltip("负责采集输入与处理局内 UI (聊天气泡等)")] 
        public SocialRoomView RoomView;

        private ClientRoom _boundRoom;

        private void Start()
        {
            if (SpawnerView == null)
            {
                SpawnerView = gameObject.AddComponent<ObjectSpawnerView>();
            }

            if (RoomView == null)
            {
                RoomView = gameObject.AddComponent<SocialRoomView>();
            }

            // 核心修复：彻底废弃 Update 轮询，改为监听全局事件，确保在回放快进的同一帧内完成绑定
            GlobalTypeNetEvent.Register<Local_RoomEntered>(OnRoomEntered)
                .UnRegisterWhenGameObjectDestroyed(gameObject);
                
            GlobalTypeNetEvent.Register<Local_RoomLeft>(OnRoomLeft)
                .UnRegisterWhenGameObjectDestroyed(gameObject);
        }

        private void OnRoomEntered(Local_RoomEntered evt)
        {
            if (evt.Room == null) return;
            
            // 玩法嗅探：如果当前房间没有交友组件，说明是其他玩法（如 DemoGame），直接忽略
            if (evt.Room.GetComponent<ClientSocialRoomComponent>() == null) return;

            _boundRoom = evt.Room;
            NetLogger.LogInfo("[SocialRoomEntry]", $"检测到进入交友房间 {_boundRoom.RoomId}，执行表现层初始化");
            
            // 同步注入，确保在回放快进前已就绪
            SpawnerView.Init(_boundRoom);
            RoomView.Init(_boundRoom);
        }

        private void OnRoomLeft(Local_RoomLeft evt)
        {
            if (_boundRoom != null)
            {
                _boundRoom = null;
                SpawnerView.Clear();
                RoomView.Clear(evt.IsSuspended);
            }
        }
    }
}
