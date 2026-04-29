using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Core
{
    /// <summary>
    /// 客户端房间组件工厂。
    /// 按组件 Id 负责创建、绑定和初始化组件。
    /// </summary>
    public static class ClientRoomFactory
    {
        /// <summary>
        /// 自动生成的组件消息绑定器。
        /// </summary>
        public static Action<ClientRoomComponent, ClientRoomDispatcher> ComponentBinder;

        /// <summary>
        /// 组件 Id 到构造函数的注册表。
        /// </summary>
        private static readonly Dictionary<int, Func<ClientRoomComponent>> Registry =
            new Dictionary<int, Func<ClientRoomComponent>>();

        /// <summary>
        /// 注册一个客户端房间组件构造器。
        /// </summary>
        public static void Register(int componentId, Func<ClientRoomComponent> componentBuilder)
        {
            if (componentBuilder == null)
            {
                NetLogger.LogError("ClientRoomFactory", $"注册失败: componentBuilder 为空, ComponentId:{componentId}");
                return;
            }

            if (Registry.ContainsKey(componentId))
            {
                NetLogger.LogError("ClientRoomFactory", $"注册失败: ComponentId 重复, ComponentId:{componentId}");
                return;
            }

            Registry[componentId] = componentBuilder;
        }

        /// <summary>
        /// 清空组件构造器注册表。
        /// </summary>
        public static void Clear()
        {
            Registry.Clear();
        }

        /// <summary>
        /// 按组件 Id 列表装配房间组件。
        /// </summary>
        public static bool BuildComponents(ClientRoom room, int[] componentIds)
        {
            if (room == null)
            {
                NetLogger.LogError("ClientRoomFactory", "装配失败: room 为空");
                return false;
            }

            if (componentIds == null || componentIds.Length == 0)
            {
                NetLogger.LogWarning("ClientRoomFactory", $"装配警告: 组件清单为空, RoomId:{room.RoomId}");
                return true;
            }

            // 先批量创建组件，确保中途失败时可以整体回滚。
            var pendingComponents = new List<ClientRoomComponent>(componentIds.Length);

            for (int i = 0; i < componentIds.Length; i++)
            {
                int id = componentIds[i];

                if (!Registry.TryGetValue(id, out Func<ClientRoomComponent> builder) || builder == null)
                {
                    NetLogger.LogError("ClientRoomFactory", $"装配失败: 未注册的 ComponentId, RoomId:{room.RoomId}, ComponentId:{id}");
                    return false;
                }

                ClientRoomComponent component = builder.Invoke();
                if (component == null)
                {
                    NetLogger.LogError("ClientRoomFactory", $"装配失败: builder 返回 null, RoomId:{room.RoomId}, ComponentId:{id}");
                    return false;
                }

                pendingComponents.Add(component);
            }

            for (int i = 0; i < pendingComponents.Count; i++)
            {
                ClientRoomComponent component = pendingComponents[i];
                if (component == null)
                {
                    NetLogger.LogError("ClientRoomFactory", $"装配失败: pendingComponents 中存在空组件, RoomId:{room.RoomId}, Index:{i}");
                    return false;
                }

                // 组件加入房间后，再绑定自动生成的消息处理器。
                room.AddComponent(component);

                if (ComponentBinder != null)
                {
                    ComponentBinder.Invoke(component, room.Dispatcher);
                }
            }

            // 全部组件都挂好后，最后统一初始化。
            room.InitializeComponents();
            return true;
        }
    }
}
