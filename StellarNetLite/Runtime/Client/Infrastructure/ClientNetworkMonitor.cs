using UnityEngine;
using Mirror;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Infrastructure
{
    /// <summary>
    /// 客户端弱网监测器。
    /// 职责：基于 Mirror 底层 RTT 判定当前网络质量等级，并通过本地事件总线广播。
    /// 架构升级：适配休眠剔除机制，移除业务包超时检测，纯依赖底层物理 RTT 进行主动熔断。
    /// </summary>
    public sealed class ClientNetworkMonitor : MonoBehaviour
    {
        private ClientApp _app;
        private bool _isWarnTriggered;
        private bool _isBlockTriggered;
        private float _blockDuration;

        // 核心修复 2：用于控制 UI 刷新频率，防止每帧派发事件
        private float _uiRefreshTimer;
        private const float UIRefreshInterval = 0.5f;

        // 弱网判定阈值配置
        public float WeakNetWarnRttMs = 200f;
        public float WeakNetBlockRttMs = 400f;
        public float ActiveFuseTimeoutSeconds = 5f;

        public void Init(ClientApp app)
        {
            _app = app;
            _blockDuration = 0f;
            _uiRefreshTimer = 0f;
        }

        public void OnPacketReceived()
        {
            // 引入休眠剔除后，业务包可能长时间不发，因此不再依赖此回调判断断线
        }

        private void Update()
        {
            if (_app == null || _app.State != ClientAppState.OnlineRoom)
            {
                if (_isWarnTriggered || _isBlockTriggered)
                {
                    _isWarnTriggered = false;
                    _isBlockTriggered = false;
                    _blockDuration = 0f;
                    GlobalTypeNetEvent.Broadcast(new Local_NetworkQualityChanged { RttMs = 0, IsWeakNetWarn = false, IsWeakNetBlock = false });
                }

                return;
            }

            // 直接依赖 Mirror 底层维护的真实物理 RTT
            float rttMs = (float)NetworkTime.rtt * 1000f;

            // 核心修复 1：移除 timeSinceLastPacket 的判断，因为休眠剔除会导致长时间无业务包
            bool shouldBlock = rttMs >= WeakNetBlockRttMs;
            bool shouldWarn = rttMs >= WeakNetWarnRttMs && !shouldBlock;

            // 核心防御：主动熔断机制
            if (shouldBlock)
            {
                _blockDuration += Time.deltaTime;
                if (_blockDuration >= ActiveFuseTimeoutSeconds)
                {
                    NetLogger.LogWarning("[ClientNetworkMonitor]", $"重度弱网卡死持续超过 {ActiveFuseTimeoutSeconds} 秒，触发主动熔断，准备通过快照重建纯净状态");
                    _blockDuration = 0f;
                    _isBlockTriggered = false;
                    _isWarnTriggered = false;
                    _app.SuspendConnection();
                    return;
                }
            }
            else
            {
                _blockDuration = 0f;
            }

            // 判定状态是否发生越级变化
            bool stateChanged = (shouldBlock != _isBlockTriggered || shouldWarn != _isWarnTriggered);

            _uiRefreshTimer += Time.deltaTime;

            // 核心修复 2：状态改变，或者达到 UI 刷新周期时，派发事件更新 RTT 显示
            if (stateChanged || _uiRefreshTimer >= UIRefreshInterval)
            {
                _isBlockTriggered = shouldBlock;
                _isWarnTriggered = shouldWarn;
                _uiRefreshTimer = 0f;

                GlobalTypeNetEvent.Broadcast(new Local_NetworkQualityChanged
                {
                    RttMs = Mathf.RoundToInt(rttMs),
                    IsWeakNetWarn = _isWarnTriggered,
                    IsWeakNetBlock = _isBlockTriggered
                });
            }
        }
    }
}