using System.Collections.Generic;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Core
{
    /// <summary>
    /// 客户端单个房间实例。
    /// 挂载房间组件、房间分发器和房间事件系统。
    /// </summary>
    public sealed class ClientRoom
    {
        // 房间唯一 Id。
        public string RoomId { get; }
        // 房间域协议分发器。
        public ClientRoomDispatcher Dispatcher { get; }
        // 房间级事件系统，在线态和回放态都独立隔离。
        public RoomNetEventSystem NetEventSystem { get; }

        // 当前房间已装配的组件列表。
        private readonly List<ClientRoomComponent> _components = new List<ClientRoomComponent>();
        private bool _isDestroyed;

        private ClientRoom(string roomId)
        {
            // 房间实例创建时同步准备好分发器和事件总线。
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

            // 组件加入房间时会自动挂上 Room 上下文。
            component.Room = this;
            _components.Add(component);
        }

        public T GetComponent<T>() where T : ClientRoomComponent
        {
            // 所有组件都挂好后统一初始化。
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

            // 销毁时统一调用组件 OnDestroy，再清掉房间级设施。
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
        }
    }
}
