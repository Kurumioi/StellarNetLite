namespace StellarNet.Lite.Server.Core
{
    /// <summary>
    /// 可参与房间 Tick 的组件接口。
    /// </summary>
    public interface ITickableComponent
    {
        /// <summary>
        /// 执行一帧房间逻辑。
        /// </summary>
        void OnTick();
    }
}
