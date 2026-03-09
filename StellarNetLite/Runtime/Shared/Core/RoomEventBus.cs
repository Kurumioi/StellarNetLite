using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarNet.Lite.Shared.Core
{
    /// <summary>
    /// 房间内事件标识接口。
    /// </summary>
    public interface IRoomEvent
    {
    }

    /// <summary>
    /// 房间作用域事件总线 (实例级)。
    /// 职责：实现事件的房间级物理隔离，解决回放房间与在线房间共存、多房间切换时的事件串线问题。
    /// </summary>
    public sealed class RoomEventBus
    {
        private readonly Dictionary<Type, Delegate> _eventHandlers = new Dictionary<Type, Delegate>();
        private readonly string _ownerRoomId;

        public RoomEventBus(string ownerRoomId)
        {
            _ownerRoomId = ownerRoomId;
        }

        public void Subscribe<T>(Action<T> handler) where T : struct, IRoomEvent
        {
            if (handler == null)
            {
                Debug.LogError($"[RoomEventBus] 订阅失败: 传入的 handler 为空。RoomId: {_ownerRoomId}");
                return;
            }

            Type eventType = typeof(T);
            if (_eventHandlers.TryGetValue(eventType, out Delegate existingDelegate))
            {
                _eventHandlers[eventType] = Delegate.Combine(existingDelegate, handler);
            }
            else
            {
                _eventHandlers[eventType] = handler;
            }
        }

        public void Unsubscribe<T>(Action<T> handler) where T : struct, IRoomEvent
        {
            if (handler == null) return;

            Type eventType = typeof(T);
            if (_eventHandlers.TryGetValue(eventType, out Delegate existingDelegate))
            {
                Delegate currentDelegate = Delegate.Remove(existingDelegate, handler);
                if (currentDelegate == null)
                {
                    _eventHandlers.Remove(eventType);
                }
                else
                {
                    _eventHandlers[eventType] = currentDelegate;
                }
            }
        }

        public void Fire<T>(T evt) where T : struct, IRoomEvent
        {
            Type eventType = typeof(T);
            if (_eventHandlers.TryGetValue(eventType, out Delegate existingDelegate))
            {
                var action = existingDelegate as Action<T>;
                action?.Invoke(evt);
            }
        }

        public void Clear()
        {
            _eventHandlers.Clear();
        }
    }
}