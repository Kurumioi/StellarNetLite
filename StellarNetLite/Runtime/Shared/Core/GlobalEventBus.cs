using System;

namespace StellarNet.Lite.Shared.Core
{
    /// <summary>
    /// 全局事件标识接口。
    /// 强制约束事件必须为 struct，配合泛型静态类实现零 GC。
    /// </summary>
    public interface IGlobalEvent
    {
    }

    /// <summary>
    /// 零 GC 值类型全局事件总线。
    /// 职责：处理大厅、登录、录像列表等脱离房间上下文的全局系统事件。
    /// </summary>
    public static class GlobalEventBus<T> where T : struct, IGlobalEvent
    {
        public static Action<T> OnEvent;

        public static void Fire(T evt)
        {
            OnEvent?.Invoke(evt);
        }

        public static void Clear()
        {
            OnEvent = null;
        }
    }
}