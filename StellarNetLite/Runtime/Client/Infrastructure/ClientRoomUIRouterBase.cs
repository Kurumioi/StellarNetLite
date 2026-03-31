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
        // 当前绑定的业务组件。
        protected T BoundComponent { get; private set; }
        // 当前绑定组件所属房间。
        protected ClientRoom BoundRoom => BoundComponent != null ? BoundComponent.Room : null;

        // 避免重复解绑。
        private bool _isUnbound = true;

        public void Bind(T component)
        {
            if (component == null)
            {
                NetLogger.LogError(GetType().Name, $"绑定失败: component 为空, Object:{name}");
                return;
            }

            // 已绑定同一组件时直接忽略。
            if (!_isUnbound && BoundComponent == component)
            {
                NetLogger.LogWarning(GetType().Name, $"重复绑定已忽略: Component:{typeof(T).Name}, Object:{name}");
                return;
            }

            // 跨组件重复绑定时，先做一次安全解绑。
            if (!_isUnbound && BoundComponent != null && BoundComponent != component)
            {
                NetLogger.LogWarning(GetType().Name, $"检测到跨组件重复绑定，先执行旧解绑。Old:{BoundComponent.GetType().Name}, New:{component.GetType().Name}, Object:{name}");
                Unbind();
            }

            // 先完成基类绑定，再交给子类挂具体 UI。
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

            // 解绑时统一走子类收尾逻辑。
            if (BoundComponent != null)
            {
                NetLogger.LogInfo(GetType().Name, $"开始解绑: Component:{BoundComponent.GetType().Name}, RoomId:{BoundRoom?.RoomId ?? "-"}, Object:{name}");
                OnUnbind();
                BoundComponent = null;
            }
        }

        protected virtual void OnDestroy()
        {
            // Router 销毁时自动解绑，避免残留引用。
            Unbind();
        }

        protected abstract void OnBind(T component);
        protected abstract void OnUnbind();
    }
}
