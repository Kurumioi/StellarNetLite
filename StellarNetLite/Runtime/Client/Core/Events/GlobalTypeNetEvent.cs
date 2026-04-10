using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarNet.Lite.Client.Core.Events
{
    /// <summary>
    /// 全局强类型事件系统。
    /// </summary>
    public static class GlobalTypeNetEvent
    {
        /// <summary>
        /// 注册全局事件监听。
        /// </summary>
        public static IUnRegister Register<T>(Action<T> onEvent)
        {
            if (onEvent == null)
            {
                return new CustomUnRegister(null);
            }

            EventBox<T>.Subscribers += onEvent;
            return EventBox<T>.AllocateToken(onEvent);
        }

        /// <summary>
        /// 显式注销指定的全局事件监听。
        /// </summary>
        public static void UnRegister<T>(Action<T> onEvent)
        {
            if (onEvent == null)
            {
                return;
            }

            EventBox<T>.Subscribers -= onEvent;
        }

        /// <summary>
        /// 广播一条全局事件。
        /// </summary>
        public static void Broadcast<T>(T e)
        {
            EventBox<T>.Subscribers?.Invoke(e);
        }

        /// <summary>
        /// 广播一条默认构造的全局事件。
        /// </summary>
        public static void Broadcast<T>() where T : new()
        {
            EventBox<T>.Subscribers?.Invoke(new T());
        }

        /// <summary>
        /// 清空指定类型的全部监听。
        /// </summary>
        public static void UnRegisterAll<T>()
        {
            EventBox<T>.Subscribers = null;
            EventBox<T>.ClearPool();
        }

        /// <summary>
        /// 单类型事件盒。
        /// </summary>
        private static class EventBox<T>
        {
            // 当前类型的全部订阅者。
            public static Action<T> Subscribers;

            // 当前类型复用的令牌池。
            private static readonly Stack<EventToken> _pool = new Stack<EventToken>();

            /// <summary>
            /// 分配一个事件令牌。
            /// </summary>
            public static EventToken AllocateToken(Action<T> callback)
            {
                EventToken token = _pool.Count > 0 ? _pool.Pop() : new EventToken();
                token.Handler = callback;
                token.IsRecycled = false;
                return token;
            }

            /// <summary>
            /// 回收一个事件令牌。
            /// </summary>
            public static void RecycleToken(EventToken token)
            {
                if (token == null || token.IsRecycled)
                {
                    return;
                }

                token.Handler = null;
                token.IsRecycled = true;
                _pool.Push(token);
            }

            /// <summary>
            /// 清空当前类型的令牌池。
            /// </summary>
            public static void ClearPool()
            {
                _pool.Clear();
            }

            /// <summary>
            /// 全局事件注销令牌。
            /// </summary>
            public class EventToken : IUnRegister
            {
                /// <summary>
                /// 当前令牌持有的回调。
                /// </summary>
                public Action<T> Handler;

                /// <summary>
                /// 当前令牌是否已回收。
                /// </summary>
                public bool IsRecycled;

                /// <summary>
                /// 执行注销。
                /// </summary>
                public void UnRegister()
                {
                    if (IsRecycled)
                    {
                        return;
                    }

                    if (Handler != null)
                    {
                        Subscribers -= Handler;
                    }

                    RecycleToken(this);
                }

                /// <summary>
                /// 在 GameObject 销毁时执行注销。
                /// </summary>
                public IUnRegister UnRegisterWhenGameObjectDestroyed(GameObject gameObject)
                {
                    if (gameObject == null)
                    {
                        UnRegister();
                        return this;
                    }

                    if (!gameObject.TryGetComponent<EventUnregisterTrigger>(out var trigger))
                    {
                        trigger = gameObject.AddComponent<EventUnregisterTrigger>();
                        trigger.hideFlags = HideFlags.HideInInspector;
                    }

                    trigger.Add(this);
                    return this;
                }

                /// <summary>
                /// 在 MonoBehaviour 禁用时执行注销。
                /// </summary>
                public IUnRegister UnRegisterWhenMonoDisable(MonoBehaviour mono)
                {
                    if (mono == null)
                    {
                        UnRegister();
                        return this;
                    }

                    if (!mono.TryGetComponent<EventUnregisterDisableTrigger>(out var trigger))
                    {
                        trigger = mono.gameObject.AddComponent<EventUnregisterDisableTrigger>();
                        trigger.hideFlags = HideFlags.HideInInspector;
                    }

                    trigger.Add(this);
                    return this;
                }
            }
        }
    }
}
