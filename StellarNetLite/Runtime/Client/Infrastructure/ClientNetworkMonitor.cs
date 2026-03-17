using UnityEngine;
using Mirror;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Infrastructure
{
    /// <summary>
    /// 客户端弱网监测器。
    /// 职责：综合 RTT 与最近收包时间，判定当前网络质量等级，并通过本地事件总线广播。
    /// 架构升级：引入主动熔断机制，当重度卡死超过指定阈值时，主动掐断物理连接并切入挂起态，保证状态纯净。
    /// </summary>
    public sealed class ClientNetworkMonitor : MonoBehaviour
    {
        private ClientApp _app;
        private float _lastReceiveTime;
        private bool _isWarnTriggered;
        private bool _isBlockTriggered;
        private float _blockDuration;

        // 弱网判定阈值配置
        public float WeakNetWarnRttMs = 200f;
        public float WeakNetBlockRttMs = 400f;
        public float NoPacketTimeoutSeconds = 3f;

        // 主动熔断阈值：弱网阻断持续超过 5 秒，判定本地状态已脏，主动触发重连快照覆盖
        public float ActiveFuseTimeoutSeconds = 5f;

        public void Init(ClientApp app)
        {
            _app = app;
            _lastReceiveTime = Time.realtimeSinceStartup;
            _blockDuration = 0f;
        }

        public void OnPacketReceived()
        {
            _lastReceiveTime = Time.realtimeSinceStartup;
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

            float rttMs = (float)NetworkTime.rtt * 1000f;
            float timeSinceLastPacket = Time.realtimeSinceStartup - _lastReceiveTime;

            bool shouldBlock = rttMs >= WeakNetBlockRttMs || timeSinceLastPacket >= NoPacketTimeoutSeconds;
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

            if (shouldBlock != _isBlockTriggered || shouldWarn != _isWarnTriggered)
            {
                _isBlockTriggered = shouldBlock;
                _isWarnTriggered = shouldWarn;

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