using StellarNet.Lite.Runtime;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Extensions.ObjectSync.Runtime
{
    /// <summary>
    /// ObjectSync 运行时桥。
    /// 负责预加载 ObjectSync 扩展配置。
    /// </summary>
    public sealed class ObjectSyncRuntimeFeature : RuntimeFeatureBridgeBase
    {
        /// <summary>
        /// Runtime 启动时同步加载 ObjectSync 配置。
        /// </summary>
        public override void OnRuntimeAwake(StellarNetAppManager appManager)
        {
            ObjectSyncConfigLoader.LoadRuntimeConfigSync();
        }
    }
}
