using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

namespace StellarNet.Lite.Game.Client.Infrastructure
{
    /// <summary>
    /// 房间组件 UI 路由基类 (仅存在于 GameDemo 表现层，不污染 Runtime)
    /// </summary>
    public abstract class ClientRoomUIRouterBase<T> : MonoBehaviour where T : ClientRoomComponent
    {
        protected T BoundComponent { get; private set; }
        protected ClientRoom BoundRoom => BoundComponent != null ? BoundComponent.Room : null;
        private bool _isUnbound = true;

        public void Bind(T component)
        {
            if (component == null) return;
            if (!_isUnbound && BoundComponent == component) return;
            if (!_isUnbound && BoundComponent != null && BoundComponent != component) Unbind();

            BoundComponent = component;
            _isUnbound = false;
            OnBind(component);
        }

        public void Unbind()
        {
            if (_isUnbound) return;
            _isUnbound = true;
            if (BoundComponent != null)
            {
                OnUnbind();
                BoundComponent = null;
            }
        }

        protected virtual void OnDestroy()
        {
            Unbind();
        }

        protected abstract void OnBind(T component);
        protected abstract void OnUnbind();
    }
}