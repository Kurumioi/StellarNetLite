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
        // 监控器依赖的客户端内核和传输层。
        private ClientApp _app;
        private INetworkTransport _transport;

        // 当前弱网状态缓存。
        private bool _isWarnTriggered;
        private bool _isBlockTriggered;
        private float _blockDuration;
        private float _uiRefreshTimer;
        private const float UIRefreshInterval = 0.5f;

        // 弱网判定阈值。
        public float WeakNetWarnRttMs = 200f;
        public float WeakNetBlockRttMs = 400f;
        public float ActiveFuseTimeoutSeconds = 5f;

        public void Init(ClientApp app, INetworkTransport transport)
        {
            // 切换监控对象时重置内部状态。
            _app = app;
            _transport = transport;
            _blockDuration = 0f;
            _uiRefreshTimer = 0f;
            // 当前实现不依赖收包频率，但保留扩展点。
        }

        public void OnPacketReceived()
        {
        }

        private void Update()
        {
            if (_app == null || _transport == null || _app.State != ClientAppState.OnlineRoom)
            {
                // 非在线房间态时，自动清空弱网 UI 状态。
                if (_isWarnTriggered || _isBlockTriggered)
                {
                    _isWarnTriggered = false;
                    _isBlockTriggered = false;
                    _blockDuration = 0f;
                    GlobalTypeNetEvent.Broadcast(new Local_NetworkQualityChanged { RttMs = 0, IsWeakNetWarn = false, IsWeakNetBlock = false });
                }

                return;
            }

            // 通过传输层抽象读取 RTT，不直接依赖 Mirror。
            float rttMs = _transport.GetRTT() * 1000f;

            bool shouldBlock = rttMs >= WeakNetBlockRttMs;
            bool shouldWarn = rttMs >= WeakNetWarnRttMs && !shouldBlock;

            if (shouldBlock)
            {
                _blockDuration += Time.deltaTime;
                // 连续重度弱网超过阈值后，主动进入挂起恢复链。
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

            // 状态变化或到达 UI 刷新间隔时才广播一次。
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
