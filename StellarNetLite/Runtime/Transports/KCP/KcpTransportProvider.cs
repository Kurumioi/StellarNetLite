using System;
using System.Buffers;
using System.Net;
using kcp2k;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Transports.Common;
using UnityEngine;
using ErrorCode = kcp2k.ErrorCode;

namespace StellarNet.Lite.Transports.KCP
{
    [DisallowMultipleComponent]
    public class KcpTransportProvider : MonoBehaviour, INetworkTransport
    {
        #region 物理事件契约

        public event Action OnServerStartedEvent;
        public event Action OnServerStoppedEvent;
        public event Action<int> OnServerClientConnectedEvent;
        public event Action<int> OnServerClientDisconnectedEvent;
        public event Action<int, Packet> OnServerReceivePacketEvent;

        public event Action OnClientStartedEvent;
        public event Action OnClientStoppedEvent;
        public event Action OnClientConnectedEvent;
        public event Action OnClientDisconnectedEvent;
        public event Action<Packet> OnClientReceivePacketEvent;

        #endregion

        [Header("KCP 核心配置 (生产级预设)")] [Tooltip("NoDelay=true, Interval=10, Resend=2, NC=1 (极速模式)")]
        public KcpConfig KcpConfig = new KcpConfig(
            DualMode: false,
            RecvBufferSize: 1024 * 1024 * 8,
            SendBufferSize: 1024 * 1024 * 8,
            Mtu: 1200,
            NoDelay: true,
            Interval: 10,
            FastResend: 2,
            CongestionWindow: false,
            SendWindowSize: 4096,
            ReceiveWindowSize: 4096,
            Timeout: 10000,
            MaxRetransmits: 40
        );

        private KcpServer _server;
        private KcpClient _client;
        private NetConfig _appConfig;

        private bool _isServerActive;
        private bool _isClientActive;

        // 独立追踪物理层的真实连接状态，避免强依赖底层库内部属性，确保状态机流转清晰。
        private bool _isPhysicalConnected;

        public void ApplyConfig(NetConfig config)
        {
            if (config == null) return;
            _appConfig = config;
            NetLogger.LogInfo("KcpTransportProvider", $"KCP 传输层配置已应用. IP:{config.Ip}, Port:{config.Port}");
        }

        private void Awake()
        {
            Log.Info = (msg) => NetLogger.LogInfo("kcp2k", msg);
            Log.Warning = (msg) => NetLogger.LogWarning("kcp2k", msg);
            Log.Error = (msg) => NetLogger.LogError("kcp2k", msg);
        }

        private void Update()
        {
            if (_isServerActive && _server != null)
            {
                _server.TickIncoming();
                _server.TickOutgoing();
            }

            if (_isClientActive && _client != null)
            {
                _client.TickIncoming();
                _client.TickOutgoing();
            }
        }

        private void OnDestroy()
        {
            StopServer();
            StopClient();
        }

        #region 服务端控制

        public void StartServer()
        {
            if (_isServerActive) return;
            if (_appConfig == null) return;

            _server = new KcpServer(
                OnServerConnected,
                OnServerDataReceived,
                OnServerDisconnected,
                OnServerError,
                KcpConfig
            );

            _server.Start((ushort)_appConfig.Port);
            _isServerActive = true;

            NetLogger.LogInfo("KcpTransportProvider", $"KCP 服务端已启动，监听端口: {_appConfig.Port}");
            OnServerStartedEvent?.Invoke();
        }

        public void StopServer()
        {
            if (!_isServerActive) return;
            _isServerActive = false;

            _server?.Stop();
            _server = null;

            NetLogger.LogInfo("KcpTransportProvider", "KCP 服务端已停止");
            OnServerStoppedEvent?.Invoke();
        }

        private void OnServerConnected(int connectionId)
        {
            OnServerClientConnectedEvent?.Invoke(connectionId);
        }

        private void OnServerDisconnected(int connectionId)
        {
            OnServerClientDisconnectedEvent?.Invoke(connectionId);
        }

        private void OnServerDataReceived(int connectionId, ArraySegment<byte> message, KcpChannel channel)
        {
            if (LitePacketFormatter.TryDeserialize(message.Array, message.Offset, message.Count, out Packet packet))
            {
                OnServerReceivePacketEvent?.Invoke(connectionId, packet);
            }
        }

        private void OnServerError(int connectionId, ErrorCode error, string reason)
        {
            NetLogger.LogWarning("KcpTransportProvider", $"KCP 服务端连接异常: {error} - {reason}", "-", $"ConnId:{connectionId}");
        }

        #endregion

        #region 客户端控制

        public void StartClient()
        {
            if (_appConfig == null) return;

            // 允许在逻辑层活跃但物理层断开的场景下，重新发起物理连接，保障断线重连机制的可靠性。
            if (_isClientActive)
            {
                if (_isPhysicalConnected) return;

                if (_client != null)
                {
                    _client.Disconnect();
                    _client = null;
                }
            }

            _client = new KcpClient(
                OnClientConnected,
                OnClientDataReceived,
                OnClientDisconnected,
                OnClientError,
                KcpConfig
            );

            _client.Connect(_appConfig.Ip, (ushort)_appConfig.Port);
            NetLogger.LogInfo("KcpTransportProvider", $"KCP 客户端发起连接 -> {_appConfig.Ip}:{_appConfig.Port}");

            if (!_isClientActive)
            {
                _isClientActive = true;
                OnClientStartedEvent?.Invoke();
            }
        }

        public void StopClient()
        {
            if (!_isClientActive) return;
            _isClientActive = false;
            _isPhysicalConnected = false;

            _client?.Disconnect();
            _client = null;

            NetLogger.LogInfo("KcpTransportProvider", "KCP 客户端已停止");
            OnClientStoppedEvent?.Invoke();
        }

        private void OnClientConnected()
        {
            _isPhysicalConnected = true;
            OnClientConnectedEvent?.Invoke();
        }

        private void OnClientDisconnected()
        {
            _isPhysicalConnected = false;
            OnClientDisconnectedEvent?.Invoke();
        }

        private void OnClientDataReceived(ArraySegment<byte> message, KcpChannel channel)
        {
            if (LitePacketFormatter.TryDeserialize(message.Array, message.Offset, message.Count, out Packet packet))
            {
                OnClientReceivePacketEvent?.Invoke(packet);
            }
        }

        private void OnClientError(ErrorCode error, string reason)
        {
            NetLogger.LogWarning("KcpTransportProvider", $"KCP 客户端连接异常: {error} - {reason}");
        }

        #endregion

        #region 混合控制与数据发送

        public void StartHost()
        {
            StartServer();
            StartClient();
        }

        public void SendToServer(Packet packet)
        {
            if (!_isClientActive || _client == null) return;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(131072);
            try
            {
                int length = LitePacketFormatter.Serialize(packet, buffer, 0);
                _client.Send(new ArraySegment<byte>(buffer, 0, length), KcpChannel.Reliable);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public void SendToClient(int connectionId, Packet packet)
        {
            if (!_isServerActive || _server == null) return;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(131072);
            try
            {
                int length = LitePacketFormatter.Serialize(packet, buffer, 0);
                _server.Send(connectionId, new ArraySegment<byte>(buffer, 0, length), KcpChannel.Reliable);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public void DisconnectClient(int connectionId)
        {
            if (!_isServerActive || _server == null) return;
            _server.Disconnect(connectionId);
        }

        public float GetRTT()
        {
            if (_isClientActive && _client != null) return 0.05f;
            return 0f;
        }

        #endregion
    }
}