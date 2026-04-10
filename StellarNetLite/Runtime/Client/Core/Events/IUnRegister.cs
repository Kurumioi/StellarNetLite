using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarNet.Lite.Client.Core.Events
{
    /// <summary>
    /// 事件注销接口。
    /// </summary>
    public interface IUnRegister
    {
        /// <summary>
        /// 执行注销。
        /// </summary>
        void UnRegister();

        /// <summary>
        /// 在 GameObject 销毁时执行注销。
        /// </summary>
        IUnRegister UnRegisterWhenGameObjectDestroyed(GameObject gameObject);

        /// <summary>
        /// 在 MonoBehaviour 禁用时执行注销。
        /// </summary>
        IUnRegister UnRegisterWhenMonoDisable(MonoBehaviour mono);
    }

    /// <summary>
    /// 默认事件注销实现。
    /// </summary>
    public class CustomUnRegister : IUnRegister
    {
        // 当前注销回调。
        private Action _onUnRegister;

        /// <summary>
        /// 创建一个事件注销令牌。
        /// </summary>
        public CustomUnRegister(Action onUnRegister)
        {
            _onUnRegister = onUnRegister;
        }

        /// <summary>
        /// 执行注销。
        /// </summary>
        public void UnRegister()
        {
            _onUnRegister?.Invoke();
            _onUnRegister = null;
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

    /// <summary>
    /// 在 GameObject 销毁时批量注销事件。
    /// </summary>
    [DisallowMultipleComponent]
    public class EventUnregisterTrigger : MonoBehaviour
    {
        // 当前触发器管理的注销令牌集合。
        private readonly HashSet<IUnRegister> _unRegisters = new HashSet<IUnRegister>();

        /// <summary>
        /// 添加一个注销令牌。
        /// </summary>
        public void Add(IUnRegister unRegister)
        {
            if (unRegister == null)
            {
                return;
            }

            _unRegisters.Add(unRegister);
        }

        /// <summary>
        /// 在销毁时执行全部注销。
        /// </summary>
        private void OnDestroy()
        {
            foreach (var unRegister in _unRegisters)
            {
                unRegister?.UnRegister();
            }

            _unRegisters.Clear();
        }
    }

    /// <summary>
    /// 在 MonoBehaviour 禁用时批量注销事件。
    /// </summary>
    [DisallowMultipleComponent]
    public class EventUnregisterDisableTrigger : MonoBehaviour
    {
        // 当前触发器管理的注销令牌集合。
        private readonly HashSet<IUnRegister> _unRegisters = new HashSet<IUnRegister>();

        /// <summary>
        /// 添加一个注销令牌。
        /// </summary>
        public void Add(IUnRegister unRegister)
        {
            if (unRegister == null)
            {
                return;
            }

            _unRegisters.Add(unRegister);
        }

        /// <summary>
        /// 在禁用时执行全部注销。
        /// </summary>
        private void OnDisable()
        {
            foreach (var unRegister in _unRegisters)
            {
                unRegister?.UnRegister();
            }

            _unRegisters.Clear();
        }
    }
}
