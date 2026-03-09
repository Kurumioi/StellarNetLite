using System;
using System.Collections.Generic;
using UnityEngine;

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
                Debug.LogError($"[ServerRoomFactory] 注册失败: 传入的构造器为空, ComponentId: {componentId}");
                return;
            }

            if (_registry.ContainsKey(componentId))
            {
                Debug.LogError($"[ServerRoomFactory] 注册失败: ComponentId {componentId} 已存在，禁止重复注册");
                return;
            }

            _registry[componentId] = componentBuilder;
        }

        /// <summary>
        /// 原子化装配服务端房间组件。
        /// 架构意图：采用两阶段提交策略，彻底杜绝半残的权威房间实例产生。
        /// 修复了 OnInit 生命周期早于网络 Handler 绑定的时序倒置问题。
        /// </summary>
        public static bool BuildComponents(Room room, int[] componentIds)
        {
            if (room == null)
            {
                Debug.LogError("[ServerRoomFactory] 装配失败: 传入的 room 为空");
                return false;
            }

            if (componentIds == null || componentIds.Length == 0)
            {
                Debug.LogWarning($"[ServerRoomFactory] 装配警告: 房间 {room.RoomId} 的组件清单为空");
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
                    Debug.LogError($"[ServerRoomFactory] 装配致命阻断: 未知的 ComponentId {id}，拒绝创建残缺房间");
                    return false;
                }
            }

            // 阶段二：全量挂载与绑定
            foreach (var comp in pendingComponents)
            {
                room.AddComponent(comp);
                ComponentBinder?.Invoke(comp, room.Dispatcher);
            }

            // 阶段三：统一激活生命周期
            room.InitializeComponents();

            return true;
        }
    }
}