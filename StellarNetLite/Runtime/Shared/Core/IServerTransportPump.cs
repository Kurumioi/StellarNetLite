namespace StellarNet.Lite.Shared.Core
{
    /// <summary>
    /// 服务端主动驱动型传输层接口。
    /// 用于把服务端底层网络泵从 Unity PlayerLoop 中抽离出来。
    /// </summary>
    public interface IServerTransportPump
    {
        /// <summary>
        /// 由服务端运行线程主动调用一次网络泵。
        /// </summary>
        void PumpServer();
    }
}
