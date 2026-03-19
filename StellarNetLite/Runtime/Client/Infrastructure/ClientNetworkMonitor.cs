using UnityEngine;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Infrastructure
{
    /// <summary>
    /// 客户端弱网监测器。
    /// 架构升级：通过 INetworkTransport 接口获取 RTT，彻底解耦 Mirror 底层。
    /// </summary>
    public sealed class ClientNetworkMonitor : MonoBehaviour
    {
        private ClientApp _app;
        private INetworkTransport _transport;

        private bool _isWarnTriggered;
        private bool _isBlockTriggered;
        private float _blockDuration;
        private float _uiRefreshTimer;
        private const float UIRefreshInterval = 0.5f;

        public float WeakNetWarnRttMs = 200f;
        public float WeakNetBlockRttMs = 400f;
        public float ActiveFuseTimeoutSeconds = 5f;

        public void Init(ClientApp app, INetworkTransport transport)
        {
            _app = app;
            _transport = transport;
            _blockDuration = 0f;
            _uiRefreshTimer = 0f;
        }

        public void OnPacketReceived()
        {
        }

        private void Update()
        {
            if (_app == null || _transport == null || _app.State != ClientAppState.OnlineRoom)
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

            // 核心优化 P2-1：通过抽象接口获取 RTT，不直接依赖 NetworkTime.rtt
            float rttMs = _transport.GetRTT() * 1000f;

            bool shouldBlock = rttMs >= WeakNetBlockRttMs;
            bool shouldWarn = rttMs >= WeakNetWarnRttMs && !shouldBlock;

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

            bool stateChanged = (shouldBlock != _isBlockTriggered || shouldWarn != _isWarnTriggered);
            _uiRefreshTimer += Time.deltaTime;

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