using System.Collections.Generic;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Core
{
    /// <summary>
    /// 客户端房间实例。
    /// </summary>
    public sealed class ClientRoom
    {
        /// <summary>
        /// 当前房间 Id。
        /// </summary>
        public string RoomId { get; }

        /// <summary>
        /// 当前房间消息分发器。
        /// </summary>
        public ClientRoomDispatcher Dispatcher { get; }

        /// <summary>
        /// 当前房间事件总线。
        /// </summary>
        public RoomNetEventSystem NetEventSystem { get; }

        private readonly List<ClientRoomComponent> _components = new List<ClientRoomComponent>();

        // 暴露只读组件列表，供回放系统遍历寻找 Consumer
        public IReadOnlyList<ClientRoomComponent> Components => _components;

        private bool _isDestroyed;

        private ClientRoom(string roomId)
        {
            RoomId = roomId;
            Dispatcher = new ClientRoomDispatcher(roomId);
            NetEventSystem = new RoomNetEventSystem(roomId);
        }

        public static ClientRoom Create(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("ClientRoom", "创建失败: roomId 为空");
                return null;
            }

            NetLogger.LogInfo("ClientRoom", $"客户端房间创建成功。RoomId:{roomId}");
            return new ClientRoom(roomId);
        }

        public void AddComponent(ClientRoomComponent component)
        {
            if (_isDestroyed)
            {
                NetLogger.LogError("ClientRoom", $"添加组件失败: 房间已销毁, RoomId:{RoomId}, Component:{component?.GetType().FullName ?? "null"}");
                return;
            }

            if (component == null)
            {
                NetLogger.LogError("ClientRoom", $"添加组件失败: component 为空, RoomId:{RoomId}");
                return;
            }

            component.Room = this;
            _components.Add(component);
        }

        public T GetComponent<T>() where T : ClientRoomComponent
        {
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] is T target)
                {
                    return target;
                }
            }

            return null;
        }

        public void InitializeComponents()
        {
            if (_isDestroyed)
            {
                NetLogger.LogError("ClientRoom", $"初始化失败: 房间已销毁, RoomId:{RoomId}");
                return;
            }

            for (int i = 0; i < _components.Count; i++)
            {
                ClientRoomComponent component = _components[i];
                if (component == null)
                {
                    NetLogger.LogError("ClientRoom", $"初始化失败: 第 {i} 个组件为空, RoomId:{RoomId}");
                    continue;
                }

                component.OnInit();
            }
        }

        public void Destroy()
        {
            if (_isDestroyed)
            {
                return;
            }

            _isDestroyed = true;
            for (int i = 0; i < _components.Count; i++)
            {
                ClientRoomComponent component = _components[i];
                if (component == null)
                {
                    continue;
                }

                component.OnDestroy();
            }

            _components.Clear();
            Dispatcher.Clear();
            NetEventSystem.Clear();
            NetLogger.LogInfo("ClientRoom", $"客户端房间已销毁。RoomId:{RoomId}");
        }
    }
}
