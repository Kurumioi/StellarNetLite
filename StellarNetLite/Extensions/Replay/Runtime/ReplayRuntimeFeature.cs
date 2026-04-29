using StellarNet.Lite.Runtime;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Server.Infrastructure;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

namespace StellarNet.Lite.Extensions.Replay.Runtime
{
    /// <summary>
    /// 回放运行时桥。
    /// 负责初始化录像存储路径，并把房间录制服务接入服务端宿主。
    /// </summary>
    public sealed class ReplayRuntimeFeature : RuntimeFeatureBridgeBase
    {
        /// <summary>
        /// Runtime 启动时加载回放配置并初始化录像落盘目录。
        /// </summary>
        public override void OnRuntimeAwake(StellarNetAppManager appManager)
        {
            ReplayConfigLoader.LoadRuntimeConfigSync();
            ServerReplayStorage.InitializePaths(Application.persistentDataPath);
        }

        /// <summary>
        /// 服务端创建完成后挂入录像录制服务。
        /// </summary>
        public override void OnServerAppCreated(StellarNetAppManager appManager, ServerApp serverApp)
        {
            if (serverApp == null)
            {
                return;
            }

            serverApp.RoomRecordingService = ReplayRoomRecordingService.Instance;
        }
    }
}
