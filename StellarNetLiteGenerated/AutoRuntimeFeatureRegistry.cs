using StellarNet.Lite.Runtime;
using StellarNet.Lite.Extensions.DefaultGameFlow.Runtime;
using StellarNet.Lite.Extensions.NetworkMonitoring.Runtime;
using StellarNet.Lite.Extensions.ObjectSync.Runtime;
using StellarNet.Lite.Extensions.Replay.Runtime;

namespace StellarNet.Lite.Shared.Binders
{
    /// <summary>
    /// 编辑器扫描后生成的 RuntimeFeature 自动注册表。
    /// </summary>
    public static class AutoRuntimeFeatureRegistry
    {
        /// <summary>
        /// 创建当前工程启用的全部运行时扩展特性。
        /// </summary>
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
