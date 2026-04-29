using System;
using System.Collections.Generic;

namespace StellarNet.Lite.Client.Core.Events
{
    /// <summary>
    /// 房间级事件系统。
    /// </summary>
    public sealed class RoomNetEventSystem
    {
        /// <summary>
        /// 当前房间内的类型到委托映射。
        /// </summary>
        private readonly Dictionary<Type, Delegate> _delegates = new Dictionary<Type, Delegate>();

        /// <summary>
        /// 当前事件系统所属的房间 Id。
        /// </summary>
        private readonly string _roomId;

        /// <summary>
        /// 创建一个房间级事件系统。
        /// </summary>
        public RoomNetEventSystem(string roomId)
        {
            _roomId = roomId;
        }

        /// <summary>
        /// 注册房间事件监听。
        /// </summary>
        public IUnRegister Register<T>(Action<T> onEvent)
        {
            if (onEvent == null)
            {
                return new CustomUnRegister(null);
            }

            Type eventType = typeof(T);
            if (_delegates.TryGetValue(eventType, out Delegate existingDelegate))
            {
                _delegates[eventType] = Delegate.Combine(existingDelegate, onEvent);
            }
            else
            {
                _delegates[eventType] = onEvent;
            }

            return new CustomUnRegister(() => UnRegister(onEvent));
        }

        /// <summary>
        /// 注销房间事件监听。
        /// </summary>
        public void UnRegister<T>(Action<T> onEvent)
        {
            if (onEvent == null)
            {
                return;
            }

            Type eventType = typeof(T);
            if (_delegates.TryGetValue(eventType, out Delegate existingDelegate))
            {
                Delegate currentDelegate = Delegate.Remove(existingDelegate, onEvent);
                if (currentDelegate == null)
                {
                    _delegates.Remove(eventType);
                }
                else
                {
                    _delegates[eventType] = currentDelegate;
                }
            }
        }

        /// <summary>
        /// 广播房间事件。
        /// </summary>
        public void Broadcast<T>(T e)
        {
            Type eventType = typeof(T);
            if (_delegates.TryGetValue(eventType, out Delegate existingDelegate))
            {
                var action = existingDelegate as Action<T>;
                action?.Invoke(e);
            }
        }

        /// <summary>
        /// 清空全部房间事件监听。
        /// </summary>
        public void Clear()
        {
            _delegates.Clear();
        }
    }
}
