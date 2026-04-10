using UnityEngine;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Client.Infrastructure
{
    public sealed class ClientNetworkMonitor : MonoBehaviour
    {
        private ClientApp _app;
        private INetworkTransport _transport;
        
        private bool _isWarnTriggered;
        private bool _isBlockTriggered;
        private float _blockDuration;
        private float _uiRefreshTimer;
        private const float UIRefreshInterval = 0.5f;

        private float _pingTimer;
        private float _currentRttMs;
        private float _lastPongReceiveTime; 
        private const float PingInterval = 1f;

        [Header("弱网阈值配置")]
        public float WeakNetWarnRttMs = 200f;
        public float WeakNetBlockRttMs = 400f;
        public float ActiveFuseTimeoutSeconds = 5f;

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
        }

        private void OnPingResult(Local_PingResult evt)
        {
            _currentRttMs = evt.RttMs;
            _lastPongReceiveTime = Time.realtimeSinceStartup;
        }

        public void OnPacketReceived()
        {
            _lastPongReceiveTime = Time.realtimeSinceStartup;
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
                    _lastPongReceiveTime = 0f;
                    _app.IsNetworkBlocked = false; // 恢复网络状态
                    GlobalTypeNetEvent.Broadcast(new Local_NetworkQualityChanged { RttMs = 0, IsWeakNetWarn = false, IsWeakNetBlock = false });
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

            bool shouldBlock = rttMs >= WeakNetBlockRttMs;
            bool shouldWarn = rttMs >= WeakNetWarnRttMs && !shouldBlock;

            if (shouldBlock)
            {
                _blockDuration += Time.deltaTime;
                if (_blockDuration >= ActiveFuseTimeoutSeconds)
                {
                    NetLogger.LogWarning("[ClientNetworkMonitor]", $"重度弱网卡死持续超过 {ActiveFuseTimeoutSeconds} 秒，触发主动熔断，直接掐断物理连接！");
                    
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

            bool stateChanged = (shouldBlock != _isBlockTriggered || shouldWarn != _isWarnTriggered);
            _uiRefreshTimer += Time.deltaTime;

            // 核心修复：先广播事件，让业务层有机会赶在底层阻断前发出“遗言包”（如刹车）
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

            // 然后再真正闭合底层闸门
            _app.IsNetworkBlocked = shouldBlock;
        }
    }
}
