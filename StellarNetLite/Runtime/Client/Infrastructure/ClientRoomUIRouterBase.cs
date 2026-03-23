using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

namespace StellarNet.Lite.Game.Client.Infrastructure
{
    /// <summary>
    /// 房间业务 UI 路由基类。
    /// </summary>
    public abstract class ClientRoomUIRouterBase<T> : MonoBehaviour where T : ClientRoomComponent
    {
        protected T BoundComponent { get; private set; }
        protected ClientRoom BoundRoom => BoundComponent != null ? BoundComponent.Room : null;

        private bool _isUnbound = true;

        public void Bind(T component)
        {
            if (component == null)
            {
                NetLogger.LogError(GetType().Name, $"绑定失败: component 为空, Object:{name}");
                return;
            }

            if (!_isUnbound && BoundComponent == component)
            {
                NetLogger.LogWarning(GetType().Name, $"重复绑定已忽略: Component:{typeof(T).Name}, Object:{name}");
                return;
            }

            if (!_isUnbound && BoundComponent != null && BoundComponent != component)
            {
                NetLogger.LogWarning(GetType().Name, $"检测到跨组件重复绑定，先执行旧解绑。Old:{BoundComponent.GetType().Name}, New:{component.GetType().Name}, Object:{name}");
                Unbind();
            }

            BoundComponent = component;
            _isUnbound = false;

            NetLogger.LogInfo(GetType().Name, $"绑定成功: Component:{typeof(T).Name}, RoomId:{BoundRoom?.RoomId ?? "-"}, Object:{name}");
            OnBind(component);
        }

        public void Unbind()
        {
            if (_isUnbound)
            {
                return;
            }

            _isUnbound = true;

            if (BoundComponent != null)
            {
                NetLogger.LogInfo(GetType().Name, $"开始解绑: Component:{BoundComponent.GetType().Name}, RoomId:{BoundRoom?.RoomId ?? "-"}, Object:{name}");
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