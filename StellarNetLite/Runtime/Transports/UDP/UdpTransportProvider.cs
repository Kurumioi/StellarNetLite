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

namespace StellarNet.Lite.Transports.UDP
{
    [DisallowMultipleComponent]
    public class UdpTransportProvider : MonoBehaviour, INetworkTransport
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
        private UdpClient _serverUdp;
        private CancellationTokenSource _serverCts;
        private int _connectionIdCounter = 0;

        private readonly ConcurrentDictionary<IPEndPoint, int> _endpointToConnId = new ConcurrentDictionary<IPEndPoint, int>();
        private readonly ConcurrentDictionary<int, IPEndPoint> _connIdToEndpoint = new ConcurrentDictionary<int, IPEndPoint>();
        
        private UdpClient _clientUdp;
        private CancellationTokenSource _clientCts;
        private IPEndPoint _serverEndpoint;

        private bool _isServerActive;
        private bool _isClientActive;
        private bool _isPhysicalConnected;

        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();

        public void ApplyConfig(NetConfig config)
        {
            _appConfig = config;
        }

        private void Update()
        {
            while (_mainThreadActions.TryDequeue(out Action action))
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
                _serverUdp = new UdpClient(_appConfig.Port);
                _serverCts = new CancellationTokenSource();
                _isServerActive = true;
                
                _ = ReceiveServerDataAsync(_serverCts.Token);
                
                NetLogger.LogWarning("UdpTransportProvider", $"[警告] UDP 服务端已启动。纯 UDP 会丢包，严禁用于生产环境业务逻辑！监听端口: {_appConfig.Port}");
                _mainThreadActions.Enqueue(() => OnServerStartedEvent?.Invoke());
            }
            catch (Exception ex)
            {
                NetLogger.LogError("UdpTransportProvider", $"UDP 服务端启动失败: {ex.Message}");
            }
        }

        public void StopServer()
        {
            if (!_isServerActive) return;
            _isServerActive = false;
            
            _serverCts?.Cancel();
            _serverUdp?.Close();
            _serverUdp = null;

            _endpointToConnId.Clear();
            _connIdToEndpoint.Clear();

            NetLogger.LogInfo("UdpTransportProvider", "UDP 服务端已停止");
            _mainThreadActions.Enqueue(() => OnServerStoppedEvent?.Invoke());
        }

        private async Task ReceiveServerDataAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    UdpReceiveResult result = await _serverUdp.ReceiveAsync();
                    
                    if (!_endpointToConnId.TryGetValue(result.RemoteEndPoint, out int connId))
                    {
                        connId = Interlocked.Increment(ref _connectionIdCounter);
                        _endpointToConnId[result.RemoteEndPoint] = connId;
                        _connIdToEndpoint[connId] = result.RemoteEndPoint;
                        
                        _mainThreadActions.Enqueue(() => OnServerClientConnectedEvent?.Invoke(connId));
                    }

                    if (LitePacketFormatter.TryDeserialize(result.Buffer, 0, result.Buffer.Length, out Packet packet))
                    {
                        byte[] safePayload = new byte[packet.PayloadLength];
                        Buffer.BlockCopy(packet.Payload, packet.PayloadOffset, safePayload, 0, packet.PayloadLength);
                        Packet safePacket = new Packet(packet.Seq, packet.MsgId, packet.Scope, packet.RoomId, safePayload, packet.PayloadLength);
                        
                        _mainThreadActions.Enqueue(() => OnServerReceivePacketEvent?.Invoke(connId, safePacket));
                    }
                }
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                NetLogger.LogError("UdpTransportProvider", $"服务端接收异常: {ex.Message}");
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
                
                if (_clientUdp != null)
                {
                    _clientCts?.Cancel();
                    _clientUdp.Close();
                    _clientUdp = null;
                }
            }

            try
            {
                _clientUdp = new UdpClient();
                _serverEndpoint = new IPEndPoint(IPAddress.Parse(_appConfig.Ip), _appConfig.Port);
                _clientCts = new CancellationTokenSource();
                _isPhysicalConnected = true;

                NetLogger.LogWarning("UdpTransportProvider", $"[警告] UDP 客户端已启动。目标: {_appConfig.Ip}:{_appConfig.Port}");
                
                _mainThreadActions.Enqueue(() => 
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
                NetLogger.LogError("UdpTransportProvider", $"UDP 客户端启动失败: {ex.Message}");
                HandlePhysicalDisconnect();
            }
        }

        public void StopClient()
        {
            if (!_isClientActive) return;
            _isClientActive = false;
            _isPhysicalConnected = false;

            _clientCts?.Cancel();
            _clientUdp?.Close();
            _clientUdp = null;

            NetLogger.LogInfo("UdpTransportProvider", "UDP 客户端已停止");
            _mainThreadActions.Enqueue(() => 
            {
                OnClientDisconnectedEvent?.Invoke();
                OnClientStoppedEvent?.Invoke();
            });
        }

        private void HandlePhysicalDisconnect()
        {
            if (!_isPhysicalConnected && _clientUdp == null) return;
            _isPhysicalConnected = false;

            _clientCts?.Cancel();
            _clientUdp?.Close();
            _clientUdp = null;

            _mainThreadActions.Enqueue(() => 
            {
                OnClientDisconnectedEvent?.Invoke();
            });
        }

        private async Task ReceiveClientDataAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    UdpReceiveResult result = await _clientUdp.ReceiveAsync();

                    if (LitePacketFormatter.TryDeserialize(result.Buffer, 0, result.Buffer.Length, out Packet packet))
                    {
                        byte[] safePayload = new byte[packet.PayloadLength];
                        Buffer.BlockCopy(packet.Payload, packet.PayloadOffset, safePayload, 0, packet.PayloadLength);
                        Packet safePacket = new Packet(packet.Seq, packet.MsgId, packet.Scope, packet.RoomId, safePayload, packet.PayloadLength);
                        
                        _mainThreadActions.Enqueue(() => OnClientReceivePacketEvent?.Invoke(safePacket));
                    }
                }
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                NetLogger.LogError("UdpTransportProvider", $"客户端接收异常: {ex.Message}");
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
            if (!_isPhysicalConnected || _clientUdp == null || _serverEndpoint == null) return;
            
            byte[] buffer = ArrayPool<byte>.Shared.Rent(65507); 
            try
            {
                int length = LitePacketFormatter.Serialize(packet, buffer, 0);
                _clientUdp.Send(buffer, length, _serverEndpoint);
            }
            catch (Exception ex)
            {
                NetLogger.LogError("UdpTransportProvider", $"发送到服务端异常: {ex.Message}");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public void SendToClient(int connectionId, Packet packet)
        {
            if (!_isServerActive || _serverUdp == null) return;
            if (!_connIdToEndpoint.TryGetValue(connectionId, out IPEndPoint endpoint)) return;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(65507);
            try
            {
                int length = LitePacketFormatter.Serialize(packet, buffer, 0);
                _serverUdp.Send(buffer, length, endpoint);
            }
            catch (Exception ex)
            {
                NetLogger.LogError("UdpTransportProvider", $"发送到客户端异常: {ex.Message}");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public void DisconnectClient(int connectionId)
        {
            if (_connIdToEndpoint.TryRemove(connectionId, out IPEndPoint endpoint))
            {
                _endpointToConnId.TryRemove(endpoint, out _);
                _mainThreadActions.Enqueue(() => OnServerClientDisconnectedEvent?.Invoke(connectionId));
            }
        }

        public float GetRTT() => 0.02f; 
        #endregion
    }
}
