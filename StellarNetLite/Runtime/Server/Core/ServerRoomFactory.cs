using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Core
{
    public static class ServerRoomFactory
    {
        public static Action<RoomComponent, RoomDispatcher> ComponentBinder;

        private static readonly Dictionary<int, Func<RoomComponent>> Registry =
            new Dictionary<int, Func<RoomComponent>>();

        public static void Register(int componentId, Func<RoomComponent> componentBuilder)
        {
            if (componentBuilder == null)
            {
                NetLogger.LogError("ServerRoomFactory", $"注册失败: componentBuilder 为空, ComponentId:{componentId}");
                return;
            }

            if (Registry.ContainsKey(componentId))
            {
                NetLogger.LogError("ServerRoomFactory", $"注册失败: ComponentId 重复, ComponentId:{componentId}");
                return;
            }

            Registry[componentId] = componentBuilder;
        }

        public static void Clear()
        {
            Registry.Clear();
        }

        public static bool BuildComponents(Room room, int[] componentIds)
        {
            if (room == null)
            {
                NetLogger.LogError("ServerRoomFactory", "装配失败: room 为空");
                return false;
            }

            if (componentIds == null || componentIds.Length == 0)
            {
                NetLogger.LogWarning("ServerRoomFactory", $"装配警告: 组件清单为空, RoomId:{room.RoomId}");
                return true;
            }

            var pendingComponents = new List<RoomComponent>(componentIds.Length);

            for (int i = 0; i < componentIds.Length; i++)
            {
                int id = componentIds[i];

                if (!Registry.TryGetValue(id, out Func<RoomComponent> builder) || builder == null)
                {
                    NetLogger.LogError("ServerRoomFactory", $"装配失败: 未注册的 ComponentId, RoomId:{room.RoomId}, ComponentId:{id}");
                    return false;
                }

                RoomComponent component = builder.Invoke();
                if (component == null)
                {
                    NetLogger.LogError("ServerRoomFactory", $"装配失败: builder 返回 null, RoomId:{room.RoomId}, ComponentId:{id}");
                    return false;
                }

                pendingComponents.Add(component);
            }

            for (int i = 0; i < pendingComponents.Count; i++)
            {
                RoomComponent component = pendingComponents[i];
                if (component == null)
                {
                    NetLogger.LogError("ServerRoomFactory", $"装配失败: pendingComponents 中存在空组件, RoomId:{room.RoomId}, Index:{i}");
                    return false;
                }

                room.AddComponent(component);

                if (ComponentBinder != null)
                {
                    ComponentBinder.Invoke(component, room.Dispatcher);
                }
            }

            room.InitializeComponents();
            return true;
        }
    }
}