using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Infrastructure;
using StellarNet.Lite.Runtime;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Extensions.NetworkMonitoring.Runtime
{
    /// <summary>
    /// 弱网监控运行时桥。
    /// 负责创建并初始化 ClientNetworkMonitor，并把 Ping 豁免注册到客户端宿主。
    /// </summary>
    public sealed class NetworkMonitoringRuntimeFeature : RuntimeFeatureBridgeBase
    {
        /// <summary>
        /// 当前客户端弱网监控器实例。
        /// </summary>
        private ClientNetworkMonitor _networkMonitor;

        /// <summary>
        /// 客户端创建完成后挂载并初始化弱网监控器。
        /// </summary>
        public override void OnClientAppCreated(StellarNetAppManager appManager, ClientApp clientApp)
        {
            if (appManager == null || clientApp == null)
            {
                return;
            }

            clientApp.RegisterWeakNetBypassProtocol<C2S_Ping>();
            _networkMonitor = appManager.GetComponent<ClientNetworkMonitor>();
            if (_networkMonitor == null)
            {
                _networkMonitor = appManager.gameObject.AddComponent<ClientNetworkMonitor>();
            }

            _networkMonitor.Init(clientApp, appManager.Transport);
        }

        /// <summary>
        /// 每次收到客户端网络包时刷新监控器收包时间。
        /// </summary>
        public override void OnClientPacketReceived(StellarNetAppManager appManager, ClientApp clientApp, Packet packet)
        {
            _networkMonitor?.OnPacketReceived();
        }

        /// <summary>
        /// 客户端停止后清理监控器引用。
        /// </summary>
        public override void OnClientStopped(StellarNetAppManager appManager, ClientApp clientApp)
        {
            _networkMonitor = null;
        }
    }
}
