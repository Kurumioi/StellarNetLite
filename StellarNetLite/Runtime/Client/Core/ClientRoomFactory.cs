using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Core
{
    public static class ClientRoomFactory
    {
        public static Action<ClientRoomComponent, ClientRoomDispatcher> ComponentBinder;

        private static readonly Dictionary<int, Func<ClientRoomComponent>> _registry =
            new Dictionary<int, Func<ClientRoomComponent>>();

        public static void Register(int componentId, Func<ClientRoomComponent> componentBuilder)
        {
            if (componentBuilder == null)
            {
                NetLogger.LogError("ClientRoomFactory", $"注册失败: 传入的构造器为空, ComponentId: {componentId}");
                return;
            }

            if (_registry.ContainsKey(componentId))
            {
                NetLogger.LogError("ClientRoomFactory", $"注册失败: ComponentId {componentId} 已存在，禁止重复注册");
                return;
            }

            _registry[componentId] = componentBuilder;
        }

        // 核心修复 P0-4：补充 Clear 机制，防止编辑器下静态数据残留
        public static void Clear()
        {
            _registry.Clear();
        }

        public static bool BuildComponents(ClientRoom room, int[] componentIds)
        {
            if (room == null)
            {
                NetLogger.LogError("ClientRoomFactory", "装配阻断: 传入的 room 为空");
                return false;
            }

            if (componentIds == null || componentIds.Length == 0)
            {
                NetLogger.LogWarning("ClientRoomFactory", $"装配警告: 房间 {room.RoomId} 的组件清单为空");
                return true;
            }

            var pendingComponents = new List<ClientRoomComponent>(componentIds.Length);

            foreach (int id in componentIds)
            {
                if (_registry.TryGetValue(id, out var builder))
                {
                    pendingComponents.Add(builder.Invoke());
                }
                else
                {
                    NetLogger.LogError("ClientRoomFactory", $"装配致命失败: 本地未注册 ComponentId {id}。客户端版本可能过旧，拒绝进入残缺房间");
                    return false;
                }
            }

            foreach (var comp in pendingComponents)
            {
                room.AddComponent(comp);
                ComponentBinder?.Invoke(comp, room.Dispatcher);
            }

            room.InitializeComponents();
            return true;
        }
    }
}