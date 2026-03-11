using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarNet.Lite.Client.Core.Events
{
    /// <summary>
    /// 事件注销接口
    /// 职责：提供统一的事件注销能力，支持与 GameObject 生命周期强绑定，防止内存泄漏。
    /// </summary>
    public interface IUnRegister
    {
        void UnRegister();
        IUnRegister UnRegisterWhenGameObjectDestroyed(GameObject gameObject);
    }

    /// <summary>
    /// 注销接口的具体实现
    /// </summary>
    public class CustomUnRegister : IUnRegister
    {
        private Action _onUnRegister;

        public CustomUnRegister(Action onUnRegister)
        {
            _onUnRegister = onUnRegister;
        }

        public void UnRegister()
        {
            _onUnRegister?.Invoke();
            _onUnRegister = null;
        }

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
    }

    /// <summary>
    /// 自动挂载的辅助组件，用于监听 OnDestroy 并触发批量注销
    /// </summary>
    [DisallowMultipleComponent]
    public class EventUnregisterTrigger : MonoBehaviour
    {
        private readonly HashSet<IUnRegister> _unRegisters = new HashSet<IUnRegister>();

        public void Add(IUnRegister unRegister)
        {
            if (unRegister == null) return;
            _unRegisters.Add(unRegister);
        }

        private void OnDestroy()
        {
            foreach (var unRegister in _unRegisters)
            {
                unRegister?.UnRegister();
            }

            _unRegisters.Clear();
        }
    }
}