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

        /// <summary>
        /// 当前房间已挂载的组件列表。
        /// </summary>
        private readonly List<ClientRoomComponent> _components = new List<ClientRoomComponent>();

        /// <summary>
        /// 暴露只读组件列表，供回放系统遍历查找组件。
        /// </summary>
        public IReadOnlyList<ClientRoomComponent> Components => _components;

        /// <summary>
        /// 当前房间是否已销毁。
        /// </summary>
        private bool _isDestroyed;

        /// <summary>
        /// 创建一个客户端房间实例。
        /// </summary>
        private ClientRoom(string roomId)
        {
            RoomId = roomId;
            Dispatcher = new ClientRoomDispatcher(roomId);
            NetEventSystem = new RoomNetEventSystem(roomId);
        }

        /// <summary>
        /// 创建一个新的客户端房间对象。
        /// </summary>
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

        /// <summary>
        /// 挂载一个客户端房间组件。
        /// </summary>
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

        /// <summary>
        /// 按类型获取当前房间中的组件实例。
        /// </summary>
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

        /// <summary>
        /// 依次初始化当前房间的全部组件。
        /// </summary>
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

        /// <summary>
        /// 销毁当前房间及其全部组件。
        /// </summary>
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
