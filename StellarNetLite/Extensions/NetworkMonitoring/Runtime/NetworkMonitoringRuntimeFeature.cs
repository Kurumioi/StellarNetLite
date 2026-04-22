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
        private ClientNetworkMonitor _networkMonitor;

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

        public override void OnClientPacketReceived(StellarNetAppManager appManager, ClientApp clientApp, Packet packet)
        {
            _networkMonitor?.OnPacketReceived();
        }

        public override void OnClientStopped(StellarNetAppManager appManager, ClientApp clientApp)
        {
            _networkMonitor = null;
        }
    }
}
