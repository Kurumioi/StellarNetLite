using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;

namespace StellarNet.Lite.Client.Infrastructure
{
    /// <summary>
    /// 客户端弱网监控器。
    /// </summary>
    public sealed class ClientNetworkMonitor : MonoBehaviour
    {
        // 当前客户端应用实例。
        private ClientApp _app;

        // 当前物理传输层。
        private INetworkTransport _transport;

        // 当前是否处于弱网告警态。
        private bool _isWarnTriggered;

        // 当前是否处于弱网阻断态。
        private bool _isBlockTriggered;

        // 当前阻断态已持续的时间。
        private float _blockDuration;

        // 下次刷新 UI 事件前累计的时间。
        private float _uiRefreshTimer;

        // UI 事件刷新间隔。
        private const float UIRefreshInterval = 0.5f;

        // 下次发送探测 Ping 前累计的时间。
        private float _pingTimer;

        // 当前记录的 RTT，单位毫秒。
        private float _currentRttMs;

        // 最后一次收到 Pong 或任意网络包的时间。
        private float _lastPongReceiveTime;

        // 主动发送探测 Ping 的间隔。
        private const float PingInterval = 1f;

        /// <summary>
        /// 进入弱网告警态的 RTT 阈值，单位毫秒。
        /// </summary>
        [Header("弱网阈值配置")]
        public float WeakNetWarnRttMs = 200f;

        /// <summary>
        /// 进入弱网阻断态的 RTT 阈值，单位毫秒。
        /// </summary>
        public float WeakNetBlockRttMs = 400f;

        /// <summary>
        /// 阻断态持续超过该时间后主动断开连接。
        /// </summary>
        public float ActiveFuseTimeoutSeconds = 5f;

        /// <summary>
        /// 初始化弱网监控器。
        /// </summary>
        public void Init(ClientApp app, INetworkTransport transport)
        {
            _app = app;
            _transport = transport;
            _blockDuration = 0f;
            _uiRefreshTimer = 0f;
            _pingTimer = 0f;
            _currentRttMs = 0f;
            _lastPongReceiveTime = 0f;

            GlobalTypeNetEvent.Register<Local_PingResult>(OnPingResult).UnRegisterWhenGameObjectDestroyed(gameObject);
            NetLogger.LogInfo(
                "ClientNetworkMonitor",
                "初始化完成",
                extraContext: $"Warn:{WeakNetWarnRttMs}ms, Block:{WeakNetBlockRttMs}ms, Fuse:{ActiveFuseTimeoutSeconds}s");
        }

        /// <summary>
        /// 处理 Ping 结果事件。
        /// </summary>
        private void OnPingResult(Local_PingResult evt)
        {
            _currentRttMs = evt.RttMs;
            _lastPongReceiveTime = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// 在收到任意网络包时刷新活跃时间。
        /// </summary>
        public void OnPacketReceived()
        {
            _lastPongReceiveTime = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// 按帧刷新弱网状态。
        /// </summary>
        private void Update()
        {
            if (_app == null || _transport == null || _app.State != ClientAppState.OnlineRoom)
            {
                if (_isWarnTriggered || _isBlockTriggered)
                {
                    _isWarnTriggered = false;
                    _isBlockTriggered = false;
                    _blockDuration = 0f;
                    _lastPongReceiveTime = 0f;

                    if (_app != null)
                    {
                        _app.IsNetworkBlocked = false;
                    }

                    GlobalTypeNetEvent.Broadcast(new Local_NetworkQualityChanged
                    {
                        RttMs = 0,
                        IsWeakNetWarn = false,
                        IsWeakNetBlock = false
                    });
                    NetLogger.LogInfo("ClientNetworkMonitor", "监控重置: 已离开在线房间");
                }

                return;
            }

            if (_lastPongReceiveTime == 0f)
            {
                _lastPongReceiveTime = Time.realtimeSinceStartup;
            }

            _pingTimer += Time.deltaTime;
            if (_pingTimer >= PingInterval)
            {
                _pingTimer = 0f;
                NetClient.Send(new C2S_Ping { ClientTime = Time.realtimeSinceStartup });
            }

            float rttMs = _currentRttMs;
            float timeSinceLastPong = Time.realtimeSinceStartup - _lastPongReceiveTime;
            if (timeSinceLastPong > 2.0f)
            {
                rttMs = timeSinceLastPong * 1000f;
            }

            bool wasWarn = _isWarnTriggered;
            bool wasBlock = _isBlockTriggered;
            bool shouldBlock = rttMs >= WeakNetBlockRttMs;
            bool shouldWarn = rttMs >= WeakNetWarnRttMs && !shouldBlock;

            if (shouldBlock)
            {
                _blockDuration += Time.deltaTime;
                if (_blockDuration >= ActiveFuseTimeoutSeconds)
                {
                    NetLogger.LogWarning(
                        "ClientNetworkMonitor",
                        $"熔断断开: 阻断超时, Rtt:{Mathf.RoundToInt(rttMs)}ms, Duration:{_blockDuration:F2}s");

                    _blockDuration = 0f;
                    _isBlockTriggered = false;
                    _isWarnTriggered = false;
                    _lastPongReceiveTime = 0f;
                    _app.IsNetworkBlocked = false;

                    _transport.StopClient();
                    return;
                }
            }
            else
            {
                _blockDuration = 0f;
            }

            bool stateChanged = shouldBlock != _isBlockTriggered || shouldWarn != _isWarnTriggered;
            _uiRefreshTimer += Time.deltaTime;

            if (stateChanged || _uiRefreshTimer >= UIRefreshInterval)
            {
                // 先刷新监控状态，再广播给 UI 和业务层。
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

            if (stateChanged)
            {
                if (shouldBlock)
                {
                    NetLogger.LogWarning(
                        "ClientNetworkMonitor",
                        $"弱网阻断: Rtt:{Mathf.RoundToInt(rttMs)}ms, Threshold:{WeakNetBlockRttMs}ms");
                }
                else if (shouldWarn)
                {
                    NetLogger.LogWarning(
                        "ClientNetworkMonitor",
                        $"弱网告警: Rtt:{Mathf.RoundToInt(rttMs)}ms, Threshold:{WeakNetWarnRttMs}ms");
                }
                else if (wasWarn || wasBlock)
                {
                    NetLogger.LogInfo("ClientNetworkMonitor", $"网络恢复: Rtt:{Mathf.RoundToInt(rttMs)}ms");
                }
            }

            _app.IsNetworkBlocked = shouldBlock;
        }
    }
}
