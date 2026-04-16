using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Transports.Common;
using UnityEngine;

namespace StellarNet.Lite.Transports.TCP
{
    /// <summary>
    /// 基于 TcpClient/TcpListener 的 TCP 传输层实现。
    /// </summary>
    [DisallowMultipleComponent]
    public class TcpTransportProvider : MonoBehaviour, INetworkTransport
    {
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

        private NetConfig _appConfig;
        private TcpListener _serverListener;
        private CancellationTokenSource _serverCts;
        private int _connectionIdCounter = 0;

        /// <summary>
        /// 服务端侧的 TCP 连接包装。
        /// </summary>
        private class TcpConnection
        {
            public int Id;
            public TcpClient Client;
            public NetworkStream Stream;
        }

        private readonly ConcurrentDictionary<int, TcpConnection> _serverConnections = new ConcurrentDictionary<int, TcpConnection>();

        private TcpClient _client;
        private NetworkStream _clientStream;
        private CancellationTokenSource _clientCts;

        private bool _isServerActive;
        private bool _isClientActive;
        private bool _isPhysicalConnected;

        private readonly ConcurrentQueue<Action> _clientMainThreadActions = new ConcurrentQueue<Action>();

        public void ApplyConfig(NetConfig config)
        {
            _appConfig = config;
        }

        private void Update()
        {
            while (_clientMainThreadActions.TryDequeue(out Action action))
            {
                action?.Invoke();
            }
        }

        private void OnDestroy()
        {
            StopServer();
            StopClient();
        }

        #region 服务端

        public void StartServer()
        {
            if (_isServerActive) return;
            if (_appConfig == null) return;

            try
            {
                _serverListener = new TcpListener(IPAddress.Any, _appConfig.Port);
                _serverListener.Start();
                _serverCts = new CancellationTokenSource();
                _isServerActive = true;

                _ = AcceptClientsAsync(_serverCts.Token);

                NetLogger.LogInfo("TcpTransportProvider", $"TCP 服务端已启动，监听端口: {_appConfig.Port}");
                OnServerStartedEvent?.Invoke();
            }
            catch (Exception ex)
            {
                NetLogger.LogError("TcpTransportProvider", $"TCP 服务端启动失败: {ex.Message}");
            }
        }

        public void StopServer()
        {
            if (!_isServerActive) return;
            _isServerActive = false;

            _serverCts?.Cancel();
            _serverListener?.Stop();
            _serverListener = null;

            foreach (var kvp in _serverConnections)
            {
                kvp.Value.Client?.Close();
            }

            _serverConnections.Clear();

            NetLogger.LogInfo("TcpTransportProvider", "TCP 服务端已停止");
            OnServerStoppedEvent?.Invoke();
        }

        private async Task AcceptClientsAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    TcpClient client = await _serverListener.AcceptTcpClientAsync();
                    client.NoDelay = true;

                    int connId = Interlocked.Increment(ref _connectionIdCounter);
                    var connection = new TcpConnection { Id = connId, Client = client, Stream = client.GetStream() };
                    _serverConnections.TryAdd(connId, connection);

                    OnServerClientConnectedEvent?.Invoke(connId);

                    _ = ReceiveDataAsync(connection, token);
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                NetLogger.LogError("TcpTransportProvider", $"接受客户端异常: {ex.Message}");
            }
        }

        private async Task ReceiveDataAsync(TcpConnection connection, CancellationToken token)
        {
            byte[] headerBuffer = new byte[4];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (!await ReadExactAsync(connection.Stream, headerBuffer, 4, token)) break;
                    int length = BitConverter.ToInt32(headerBuffer, 0);

                    if (length <= 0 || length > 1024 * 1024 * 10) break;

                    byte[] payloadBuffer = ArrayPool<byte>.Shared.Rent(length);
                    try
                    {
                        if (!await ReadExactAsync(connection.Stream, payloadBuffer, length, token)) break;

                        if (LitePacketFormatter.TryDeserialize(payloadBuffer, 0, length, out Packet packet))
                        {
                            byte[] safePayload = new byte[packet.PayloadLength];
                            Buffer.BlockCopy(packet.Payload, packet.PayloadOffset, safePayload, 0, packet.PayloadLength);
                            Packet safePacket = new Packet(packet.Seq, packet.MsgId, packet.Scope, packet.RoomId, safePayload, packet.PayloadLength);

                            OnServerReceivePacketEvent?.Invoke(connection.Id, safePacket);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(payloadBuffer);
                    }
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                DisconnectClient(connection.Id);
            }
        }

        #endregion

        #region 客户端

        public void StartClient()
        {
            if (_appConfig == null) return;

            if (_isClientActive)
            {
                if (_isPhysicalConnected) return;

                if (_client != null)
                {
                    _clientCts?.Cancel();
                    _clientStream?.Close();
                    _client.Close();
                    _client = null;
                }
            }

            try
            {
                _client = new TcpClient();
                _client.NoDelay = true;
                _client.Connect(_appConfig.Ip, _appConfig.Port);
                _clientStream = _client.GetStream();
                _clientCts = new CancellationTokenSource();
                _isPhysicalConnected = true;

                NetLogger.LogInfo("TcpTransportProvider", $"TCP 客户端已连接到 {_appConfig.Ip}:{_appConfig.Port}");

                _clientMainThreadActions.Enqueue(() =>
                {
                    if (!_isClientActive)
                    {
                        _isClientActive = true;
                        OnClientStartedEvent?.Invoke();
                    }

                    OnClientConnectedEvent?.Invoke();
                });

                _ = ReceiveClientDataAsync(_clientCts.Token);
            }
            catch (Exception ex)
            {
                NetLogger.LogError("TcpTransportProvider", $"TCP 客户端连接失败: {ex.Message}");
                HandlePhysicalDisconnect();
            }
        }

        public void StopClient()
        {
            if (!_isClientActive) return;
            _isClientActive = false;
            _isPhysicalConnected = false;

            _clientCts?.Cancel();
            _clientStream?.Close();
            _client?.Close();

            _clientStream = null;
            _client = null;

            NetLogger.LogInfo("TcpTransportProvider", "TCP 客户端已停止");
            _clientMainThreadActions.Enqueue(() =>
            {
                OnClientDisconnectedEvent?.Invoke();
                OnClientStoppedEvent?.Invoke();
            });
        }

        private void HandlePhysicalDisconnect()
        {
            if (!_isPhysicalConnected && _client == null) return;
            _isPhysicalConnected = false;

            _clientCts?.Cancel();
            _clientStream?.Close();
            _client?.Close();

            _clientStream = null;
            _client = null;

            _clientMainThreadActions.Enqueue(() => { OnClientDisconnectedEvent?.Invoke(); });
        }

        private async Task ReceiveClientDataAsync(CancellationToken token)
        {
            byte[] headerBuffer = new byte[4];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (!await ReadExactAsync(_clientStream, headerBuffer, 4, token)) break;
                    int length = BitConverter.ToInt32(headerBuffer, 0);

                    if (length <= 0 || length > 1024 * 1024 * 10) break;

                    byte[] payloadBuffer = ArrayPool<byte>.Shared.Rent(length);
                    try
                    {
                        if (!await ReadExactAsync(_clientStream, payloadBuffer, length, token)) break;

                        if (LitePacketFormatter.TryDeserialize(payloadBuffer, 0, length, out Packet packet))
                        {
                            byte[] safePayload = new byte[packet.PayloadLength];
                            Buffer.BlockCopy(packet.Payload, packet.PayloadOffset, safePayload, 0, packet.PayloadLength);
                            Packet safePacket = new Packet(packet.Seq, packet.MsgId, packet.Scope, packet.RoomId, safePayload, packet.PayloadLength);

                            _clientMainThreadActions.Enqueue(() => OnClientReceivePacketEvent?.Invoke(safePacket));
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(payloadBuffer);
                    }
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                if (_isClientActive)
                {
                    HandlePhysicalDisconnect();
                }
            }
        }

        #endregion

        #region 混合与发送

        public void StartHost()
        {
            StartServer();
            StartClient();
        }

        public void SendToServer(Packet packet)
        {
            if (!_isPhysicalConnected || _clientStream == null) return;
            SendToStreamAsync(_clientStream, packet);
        }

        public void SendToClient(int connectionId, Packet packet)
        {
            if (_serverConnections.TryGetValue(connectionId, out TcpConnection conn))
            {
                SendToStreamAsync(conn.Stream, packet);
            }
        }

        private async void SendToStreamAsync(NetworkStream stream, Packet packet)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(131072);
            try
            {
                int length = LitePacketFormatter.Serialize(packet, buffer, 4);
                byte[] lengthBytes = BitConverter.GetBytes(length);
                Buffer.BlockCopy(lengthBytes, 0, buffer, 0, 4);

                await stream.WriteAsync(buffer, 0, length + 4);
            }
            catch (Exception ex)
            {
                NetLogger.LogError("TcpTransportProvider", $"发送数据异常: {ex.Message}");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public void DisconnectClient(int connectionId)
        {
            if (_serverConnections.TryRemove(connectionId, out TcpConnection conn))
            {
                conn.Client?.Close();
                OnServerClientDisconnectedEvent?.Invoke(connectionId);
            }
        }

        public float GetRTT() => 0.05f; // 占位符，真实 RTT 需在业务层通过 PingPong 计算

        private async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int length, CancellationToken token)
        {
            int totalRead = 0;
            while (totalRead < length)
            {
                int read = await stream.ReadAsync(buffer, totalRead, length - totalRead, token);
                if (read == 0) return false;
                totalRead += read;
            }

            return true;
        }

        #endregion
    }
}
