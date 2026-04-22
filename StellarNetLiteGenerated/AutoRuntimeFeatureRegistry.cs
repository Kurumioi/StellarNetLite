using StellarNet.Lite.Runtime;
using StellarNet.Lite.Extensions.DefaultGameFlow.Runtime;
using StellarNet.Lite.Extensions.NetworkMonitoring.Runtime;
using StellarNet.Lite.Extensions.ObjectSync.Runtime;
using StellarNet.Lite.Extensions.Replay.Runtime;

namespace StellarNet.Lite.Shared.Binders
{
    public static class AutoRuntimeFeatureRegistry
    {
        public static IRuntimeFeatureBridge[] CreateFeatures()
        {
            return new IRuntimeFeatureBridge[]
            {
                new DefaultGameFlowRuntimeFeature(),
                new NetworkMonitoringRuntimeFeature(),
                new ObjectSyncRuntimeFeature(),
                new ReplayRuntimeFeature()
            };
        }
    }
}
