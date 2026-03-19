namespace StellarNet.Lite.Server.Core
{
    /// <summary>
    /// 可 Tick 组件接口。
    /// 架构意图：仅实现此接口的组件才会被加入 Room 的 Tick 循环，消除无效的虚函数调用开销。
    /// </summary>
    public interface ITickableComponent
    {
        void OnTick();
    }
}