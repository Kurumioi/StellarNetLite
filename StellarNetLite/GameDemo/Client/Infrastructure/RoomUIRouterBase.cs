using UnityEngine;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Game.Client.Infrastructure
{
    /// <summary>
    /// 房间业务 UI 路由基类 (组件驱动版)
    /// </summary>
    public abstract class RoomUIRouterBase<T> : MonoBehaviour where T : ClientRoomComponent
    {
        protected T BoundComponent { get; private set; }
        protected ClientRoom BoundRoom => BoundComponent?.Room;

        private bool _isUnbound = false;

        public void Bind(T component)
        {
            if (component == null) return;
            BoundComponent = component;
            _isUnbound = false;
            NetLogger.LogInfo(GetType().Name, $"已绑定到组件 {typeof(T).Name}，接管 UI 路由");
            OnBind(component);
        }

        /// <summary>
        /// 核心修复：提供显式的解绑方法，由宿主组件在 OnDestroy 时主动调用，
        /// 避免依赖 Unity 的 OnDestroy 延迟执行导致时序冲突。
        /// </summary>
        public void Unbind()
        {
            if (_isUnbound) return;
            _isUnbound = true;

            if (BoundComponent != null)
            {
                NetLogger.LogInfo(GetType().Name, $"宿主对象主动解绑，清理业务 UI 路由");
                OnUnbind();
                BoundComponent = null;
            }
        }

        protected virtual void OnDestroy()
        {
            // 兜底机制：如果宿主忘记调用 Unbind，在 Unity 销毁时补刀
            Unbind();
        }

        protected abstract void OnBind(T component);
        protected abstract void OnUnbind();
    }
}