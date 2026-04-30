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
        private long _serverTcpTotalPackets;
        private long _serverTcpDeserializeFailures;
        private long _clientTcpTotalPackets;
        private long _clientTcpDeserializeFailures;
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
            public TcpSendQueue SendQueue;
        }

        /// <summary>
        /// 单条 TCP 连接的发送上下文。
        /// 通过单发送循环串行写流，避免高负载下为每个包都创建独立异步任务。
        /// </summary>
        private sealed class TcpSendQueue
        {
            public NetworkStream Stream;
            public readonly ConcurrentQueue<PendingSendFrame> PendingFrames = new ConcurrentQueue<PendingSendFrame>();
            public int SendLoopRunning;
        }

        /// <summary>
        /// 已完成序列化、等待写入底层流的独立帧。
        /// </summary>
        private sealed class PendingSendFrame
        {
            public byte[] Buffer;
            public int Length;
        }

        private readonly ConcurrentDictionary<int, TcpConnection> _serverConnections = new ConcurrentDictionary<int, TcpConnection>();

        private TcpClient _client;
        private NetworkStream _clientStream;
        private CancellationTokenSource _clientCts;
        private TcpSendQueue _clientSendQueue;

        private bool _isServerActive;
        private bool _isClientActive;
        private bool _isPhysicalConnected;

        public void ApplyConfig(NetConfig config)
        {
            _appConfig = config;
        }

        private void Awake()
        {
            UnityPlayerLoopDispatcher.EnsureInstalled();
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
                DisposeServerConnection(kvp.Value);
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
                    NetworkStream stream = client.GetStream();
                    var connection = new TcpConnection
                    {
                        Id = connId,
                        Client = client,
                        SendQueue = new TcpSendQueue { Stream = stream }
                    };
                    _serverConnections.TryAdd(connId, connection);

                    OnServerClientConnectedEvent?.Invoke(connId);

                    _ = ReceiveDataAsync(connection, stream, token);
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

        private async Task ReceiveDataAsync(TcpConnection connection, NetworkStream stream, CancellationToken token)
        {
            byte[] headerBuffer = new byte[4];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (!await ReadExactAsync(stream, headerBuffer, 4, token)) break;
                    int length = BitConverter.ToInt32(headerBuffer, 0);

                    if (length <= 0 || length > 1024 * 1024 * 10) break;

                    byte[] payloadBuffer = ArrayPool<byte>.Shared.Rent(length);
                    try
                    {
                        if (!await ReadExactAsync(stream, payloadBuffer, length, token)) break;

                        _serverTcpTotalPackets++;
                        if (LitePacketFormatter.TryDeserialize(payloadBuffer, 0, length, out Packet packet))
                        {
                            byte[] safePayload = new byte[packet.PayloadLength];
                            Buffer.BlockCopy(packet.Payload, packet.PayloadOffset, safePayload, 0, packet.PayloadLength);
                            Packet safePacket = new Packet(packet.Seq, packet.MsgId, packet.Scope, packet.RoomId, safePayload, packet.PayloadLength);

                            OnServerReceivePacketEvent?.Invoke(connection.Id, safePacket);
                        }
                        else
                        {
                            _serverTcpDeserializeFailures++;
                            NetLogger.LogWarning("TcpTransportProvider", $"TCP 服务端解包失败，长度={length}，连接={connection.Id}");
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
            UnityPlayerLoopDispatcher.EnsureInstalled();

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
                    NetworkStream clientStream = _client.GetStream();
                    _clientStream = clientStream;
                    _clientSendQueue = new TcpSendQueue { Stream = clientStream };
                    _clientCts = new CancellationTokenSource();
                    _isPhysicalConnected = true;

                NetLogger.LogInfo("TcpTransportProvider", $"TCP 客户端已连接到 {_appConfig.Ip}:{_appConfig.Port}");

                // 连接成功后的上层事件仍然回到 Unity 主线程触发，保持现有客户端线程边界。
                UnityPlayerLoopDispatcher.ExecuteOrPost(() =>
                {
                    if (!_isClientActive)
                    {
                        _isClientActive = true;
                        OnClientStartedEvent?.Invoke();
                    }

                    OnClientConnectedEvent?.Invoke();
                });

                    _ = ReceiveClientDataAsync(clientStream, _clientCts.Token);
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

            TcpSendQueue sendQueue = _clientSendQueue;
            _clientSendQueue = null;
            if (sendQueue != null)
            {
                sendQueue.Stream = null;
                DrainPendingFrames(sendQueue);
            }

            _clientCts?.Cancel();
            _clientStream?.Close();
            _client?.Close();

            _clientStream = null;
            _client = null;

            NetLogger.LogInfo("TcpTransportProvider", "TCP 客户端已停止");
            UnityPlayerLoopDispatcher.ExecuteOrPost(() =>
            {
                OnClientDisconnectedEvent?.Invoke();
                OnClientStoppedEvent?.Invoke();
            });
        }

        private void HandlePhysicalDisconnect()
        {
            if (!_isPhysicalConnected && _client == null) return;
            _isPhysicalConnected = false;

            TcpSendQueue sendQueue = _clientSendQueue;
            _clientSendQueue = null;
            if (sendQueue != null)
            {
                sendQueue.Stream = null;
                DrainPendingFrames(sendQueue);
            }

            _clientCts?.Cancel();
            _clientStream?.Close();
            _client?.Close();

            _clientStream = null;
            _client = null;

            UnityPlayerLoopDispatcher.ExecuteOrPost(() => { OnClientDisconnectedEvent?.Invoke(); });
        }

        private async Task ReceiveClientDataAsync(NetworkStream stream, CancellationToken token)
        {
            byte[] headerBuffer = new byte[4];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (!await ReadExactAsync(stream, headerBuffer, 4, token)) break;
                    int length = BitConverter.ToInt32(headerBuffer, 0);

                    if (length <= 0 || length > 1024 * 1024 * 10) break;

                    byte[] payloadBuffer = ArrayPool<byte>.Shared.Rent(length);
                    try
                    {
                        if (!await ReadExactAsync(stream, payloadBuffer, length, token)) break;

                        _clientTcpTotalPackets++;
                        if (LitePacketFormatter.TryDeserialize(payloadBuffer, 0, length, out Packet packet))
                        {
                            byte[] safePayload = new byte[packet.PayloadLength];
                            Buffer.BlockCopy(packet.Payload, packet.PayloadOffset, safePayload, 0, packet.PayloadLength);
                            Packet safePacket = new Packet(packet.Seq, packet.MsgId, packet.Scope, packet.RoomId, safePayload, packet.PayloadLength);

                            // 后台线程只负责解包，真正进入客户端逻辑仍通过统一调度器回主线程。
                            UnityPlayerLoopDispatcher.ExecuteOrPost(() => OnClientReceivePacketEvent?.Invoke(safePacket));
                        }
                        else
                        {
                            _clientTcpDeserializeFailures++;
                            NetLogger.LogWarning("TcpTransportProvider", $"TCP 客户端解包失败，长度={length}");
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
                // 只有当前仍然挂在客户端字段上的那条物理连接，才允许触发真实断线清理。
                // 这样旧连接在重连过程中退出时，不会误把新建好的连接一起断掉。
                if (_isClientActive && ReferenceEquals(_clientStream, stream))
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
            if (!_isPhysicalConnected || _clientSendQueue == null) return;
            QueueSerializedSend(_clientSendQueue, packet);
        }

        public void SendToClient(int connectionId, Packet packet)
        {
            if (_serverConnections.TryGetValue(connectionId, out TcpConnection conn) && conn.SendQueue != null)
            {
                QueueSerializedSend(conn.SendQueue, packet);
            }
        }

        private void QueueSerializedSend(TcpSendQueue sendQueue, Packet packet)
        {
            if (sendQueue == null || sendQueue.Stream == null)
            {
                return;
            }

            byte[] frameBuffer = null;
            int frameLength = 0;
            try
            {
                // 先在调用线程把 Packet 序列化成独立帧，确保上层即使立即归还 Payload 池化缓冲区，
                // TCP 后台发送任务也不会再读到被复用的旧载荷。
                int packetLength = LitePacketFormatter.GetSerializedLength(packet);
                frameLength = packetLength + 4;
                frameBuffer = ArrayPool<byte>.Shared.Rent(frameLength);
                int serializedLength = LitePacketFormatter.Serialize(packet, frameBuffer, 4);
                byte[] lengthBytes = BitConverter.GetBytes(serializedLength);
                Buffer.BlockCopy(lengthBytes, 0, frameBuffer, 0, 4);
            }
            catch (Exception ex)
            {
                if (frameBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(frameBuffer);
                }

                NetLogger.LogError("TcpTransportProvider", $"序列化待发送帧异常: {ex.Message}");
                return;
            }

            sendQueue.PendingFrames.Enqueue(new PendingSendFrame
            {
                Buffer = frameBuffer,
                Length = frameLength
            });

            // 每条连接同一时间只允许一个发送循环在跑，避免高负载下为每个包堆积一个等待锁的异步任务。
            if (Interlocked.CompareExchange(ref sendQueue.SendLoopRunning, 1, 0) == 0)
            {
                _ = SendQueuedFramesAsync(sendQueue);
            }
        }

        private async Task SendQueuedFramesAsync(TcpSendQueue sendQueue)
        {
            if (sendQueue == null)
            {
                return;
            }

            bool abortLoop = false;
            try
            {
                while (true)
                {
                    while (sendQueue.PendingFrames.TryDequeue(out PendingSendFrame frame))
                    {
                        try
                        {
                            if (sendQueue.Stream == null)
                            {
                                abortLoop = true;
                                break;
                            }

                            await sendQueue.Stream.WriteAsync(frame.Buffer, 0, frame.Length);
                        }
                        catch (Exception ex)
                        {
                            abortLoop = true;
                            NetLogger.LogError("TcpTransportProvider", $"发送数据异常: {ex.Message}");
                            break;
                        }
                        finally
                        {
                            if (frame.Buffer != null)
                            {
                                ArrayPool<byte>.Shared.Return(frame.Buffer);
                            }
                        }
                    }

                    if (abortLoop)
                    {
                        break;
                    }

                    Interlocked.Exchange(ref sendQueue.SendLoopRunning, 0);
                    if (sendQueue.PendingFrames.IsEmpty ||
                        Interlocked.CompareExchange(ref sendQueue.SendLoopRunning, 1, 0) != 0)
                    {
                        break;
                    }
                }
            }
            finally
            {
                if (abortLoop)
                {
                    Interlocked.Exchange(ref sendQueue.SendLoopRunning, 0);
                    DrainPendingFrames(sendQueue);
                }
            }
        }

        private static void DrainPendingFrames(TcpSendQueue sendQueue)
        {
            if (sendQueue == null)
            {
                return;
            }

            while (sendQueue.PendingFrames.TryDequeue(out PendingSendFrame frame))
            {
                if (frame?.Buffer != null)
                {
                    ArrayPool<byte>.Shared.Return(frame.Buffer);
                }
            }
        }

        public void DisconnectClient(int connectionId)
        {
            if (_serverConnections.TryRemove(connectionId, out TcpConnection conn))
            {
                DisposeServerConnection(conn);
                OnServerClientDisconnectedEvent?.Invoke(connectionId);
            }
        }

        private static void DisposeServerConnection(TcpConnection connection)
        {
            if (connection == null)
            {
                return;
            }

            NetworkStream stream = connection.SendQueue != null ? connection.SendQueue.Stream : null;
            if (connection.SendQueue != null)
            {
                connection.SendQueue.Stream = null;
            }

            DrainPendingFrames(connection.SendQueue);
            stream?.Close();
            connection.Client?.Close();
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
