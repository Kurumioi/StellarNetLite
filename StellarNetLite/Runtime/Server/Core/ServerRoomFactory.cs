using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Core
{
    public static class ServerRoomFactory
    {
        public static Action<RoomComponent, RoomDispatcher> ComponentBinder;

        private static readonly Dictionary<int, Func<RoomComponent>> _registry =
            new Dictionary<int, Func<RoomComponent>>();

        public static void Register(int componentId, Func<RoomComponent> componentBuilder)
        {
            if (componentBuilder == null)
            {
                NetLogger.LogError("[ServerRoomFactory]", $"注册失败: 传入的构造器为空, ComponentId: {componentId}");
                return;
            }

            if (_registry.ContainsKey(componentId))
            {
                NetLogger.LogError("[ServerRoomFactory]", $"注册失败: ComponentId {componentId} 已存在，禁止重复注册");
                return;
            }

            _registry[componentId] = componentBuilder;
        }

        /// <summary>
        /// 原子化装配服务端房间组件。
        /// 架构意图：采用两阶段提交策略，彻底杜绝半残的权威房间实例产生。
        /// 规范约束：严禁使用 try-catch 掩盖业务初始化异常，必须 Fail-Fast 暴露问题。
        /// </summary>
        public static bool BuildComponents(Room room, int[] componentIds)
        {
            if (room == null)
            {
                NetLogger.LogError("[ServerRoomFactory]", "装配失败: 传入的 room 为空");
                return false;
            }

            if (componentIds == null || componentIds.Length == 0)
            {
                NetLogger.LogWarning("[ServerRoomFactory]", $"装配警告: 房间 {room.RoomId} 的组件清单为空");
                return true;
            }

            // 阶段一：全量校验与实例化 (All or Nothing)
            var pendingComponents = new List<RoomComponent>(componentIds.Length);
            foreach (int id in componentIds)
            {
                if (_registry.TryGetValue(id, out var builder))
                {
                    pendingComponents.Add(builder.Invoke());
                }
                else
                {
                    NetLogger.LogError("[ServerRoomFactory]", $"装配致命阻断: 未知的 ComponentId {id}，拒绝创建残缺房间");
                    return false;
                }
            }

            // 阶段二与阶段三：装配与激活 (移除 try-catch，让异常直接抛出)
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