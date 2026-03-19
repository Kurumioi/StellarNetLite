using System;
using System.Collections;
using UnityEngine;
using Mirror;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Binders;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Client.Infrastructure;

namespace StellarNet.Lite.Shared.Infrastructure
{
    public class StellarNetMirrorManager : NetworkManager, INetworkTransport
    {
        public INetSerializer Serializer { get; private set; }
        private NetConfig _netConfig;

        public override void Awake()
        {
            base.Awake();

            Serializer = new LiteNetSerializer();

            _netConfig = NetConfigLoader.LoadServerConfigSync(ConfigRootPath.StreamingAssets);
            ApplyConfigInternal(_netConfig);

            NetMessageMapper.Initialize();

            // 核心修复 P0-3：适配新的委托签名，增加 offset 参数
            Func<byte[], int, int, Type, object> deserializeFunc = Serializer.Deserialize;

            ServerRoomFactory.ComponentBinder = (comp, dispatcher) =>
                AutoRegistry.BindServerComponent(comp, dispatcher, deserializeFunc);
            ClientRoomFactory.ComponentBinder = (comp, dispatcher) =>
                AutoRegistry.BindClientComponent(comp, dispatcher, deserializeFunc);
        }

        public void ApplyConfig(NetConfig config)
        {
            if (config == null) return;
            _netConfig = config;
            ApplyConfigInternal(_netConfig);
        }

        private void ApplyConfigInternal(NetConfig config)
        {
            this.maxConnections = config.MaxConnections;
            this.networkAddress = config.Ip;

            var transport = GetComponent<Transport>();
            if (transport is PortTransport portTransport)
            {
                portTransport.Port = config.Port;
                NetLogger.LogInfo("StellarNetManager", $"已将底层传输端口动态设置为: {config.Port}, 目标 IP: {config.Ip}");
            }
            else if (transport != null)
            {
                var portField = transport.GetType().GetField("port") ?? transport.GetType().GetField("Port");
                if (portField != null)
                {
                    portField.SetValue(transport, config.Port);
                    NetLogger.LogInfo("StellarNetManager", $"已通过反射将底层传输端口设置为: {config.Port}, 目标 IP: {config.Ip}");
                }
                else
                {
                    NetLogger.LogWarning("StellarNetManager", $"当前使用的 Transport ({transport.GetType().Name}) 无法自动设置端口，请在 Inspector 中手动配置。");
                }
            }
        }

        private void FixedUpdate()
        {
            if (NetworkServer.active && ServerApp != null)
            {
                ServerApp.Tick();
            }
        }

        #region ================= INetworkTransport 接口实现 =================

        public void SendToServer(Packet packet)
        {
            if (NetworkClient.ready)
            {
                NetworkClient.Send(new MirrorPacketMsg(packet));
            }
        }

        public void SendToClient(int connectionId, Packet packet)
        {
            if (NetworkServer.connections.TryGetValue(connectionId, out var conn))
            {
                conn.Send(new MirrorPacketMsg(packet));
            }
        }

        public void DisconnectClient(int connectionId)
        {
            if (NetworkServer.connections.TryGetValue(connectionId, out var conn))
            {
                conn.Disconnect();
            }
        }

        public float GetRTT()
        {
            return (float)NetworkTime.rtt;
        }

        #endregion

        #region ================= 服务端专属 =================

        public ServerApp ServerApp { get; private set; }

        public static event Action OnServerStartedEvent;
        public static event Action OnServerStoppedEvent;
        public static event Action<int> OnServerClientConnectedEvent;
        public static event Action<int> OnServerClientDisconnectedEvent;

        public override void OnStartServer()
        {
            base.OnStartServer();
            NetworkServer.tickRate = _netConfig.TickRate;

            ServerApp = new ServerApp(this, Serializer, _netConfig);

            Func<byte[], int, int, Type, object> deserializeFunc = Serializer.Deserialize;
            AutoRegistry.RegisterServer(ServerApp, deserializeFunc);

            NetworkServer.RegisterHandler<MirrorPacketMsg>(OnServerReceivePacket, false);
            NetLogger.LogInfo("StellarNetManager", $"服务端装配完毕，开始监听网络请求。TickRate: {NetworkServer.tickRate}");
            OnServerStartedEvent?.Invoke();
        }

        public override void OnStopServer()
        {
            OnServerStoppedEvent?.Invoke();
            NetLogger.LogInfo("StellarNetManager", "服务端物理节点已停止运行");
            if (ServerApp != null)
            {
                ServerApp.Dispose();
                ServerApp = null;
            }

            ServerRoomFactory.Clear();
            base.OnStopServer();
        }

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            base.OnServerConnect(conn);
            NetLogger.LogInfo("StellarNetManager", $"物理连接建立", "-", "-", $"ConnId:{conn.connectionId}");
            OnServerClientConnectedEvent?.Invoke(conn.connectionId);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            if (ServerApp != null)
            {
                var session = ServerApp.TryGetSessionByConnectionId(conn.connectionId);
                if (session != null)
                {
                    NetLogger.LogInfo("StellarNetManager", $"物理连接断开，触发会话离线", "-", session.SessionId, $"ConnId:{conn.connectionId}");
                    ServerApp.UnbindConnection(session);
                }
            }

            OnServerClientDisconnectedEvent?.Invoke(conn.connectionId);
            base.OnServerDisconnect(conn);
        }

        private void OnServerReceivePacket(NetworkConnectionToClient conn, MirrorPacketMsg msg)
        {
            ServerApp?.OnReceivePacket(conn.connectionId, msg.ToPacket());
        }

        #endregion

        #region ================= 客户端专属 =================

        public ClientApp ClientApp { get; private set; }
        public ClientNetworkMonitor NetworkMonitor { get; private set; }

        private Coroutine _reconnectCoroutine;

        public static event Action OnClientStartedEvent;
        public static event Action OnClientStoppedEvent;
        public static event Action OnClientConnectedEvent;
        public static event Action OnClientDisconnectedEvent;

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (ClientApp == null)
            {
                ClientApp = new ClientApp(this, Serializer);
                NetClient.Initialize(ClientApp);

                Func<byte[], int, int, Type, object> deserializeFunc = Serializer.Deserialize;
                AutoRegistry.RegisterClient(ClientApp, deserializeFunc);

                NetworkClient.RegisterHandler<MirrorPacketMsg>(OnClientReceivePacket, false);
            }

            if (NetworkMonitor == null)
            {
                NetworkMonitor = gameObject.AddComponent<ClientNetworkMonitor>();
            }

            NetworkMonitor.Init(ClientApp, this);

            NetLogger.LogInfo("StellarNetManager", "客户端装配完毕，准备就绪。");
            OnClientStartedEvent?.Invoke();
        }

        public override void OnStopClient()
        {
            OnClientStoppedEvent?.Invoke();
            NetLogger.LogInfo("StellarNetManager", "客户端物理节点已停止运行");
            if (_reconnectCoroutine != null)
            {
                StopCoroutine(_reconnectCoroutine);
                _reconnectCoroutine = null;
            }

            if (ClientApp != null)
            {
                ClientApp.Dispose();
                ClientApp = null;
                NetClient.Initialize(null);
            }

            ClientRoomFactory.Clear();
            base.OnStopClient();
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();
            NetLogger.LogInfo("StellarNetManager", "成功连接到服务端");

            if (_reconnectCoroutine != null)
            {
                StopCoroutine(_reconnectCoroutine);
                _reconnectCoroutine = null;
            }

            if (ClientApp != null && ClientApp.Session.IsReconnecting)
            {
                NetLogger.LogInfo("StellarNetManager", "物理连接恢复，自动发起恢复链 Login 鉴权");
                ClientApp.Session.IsPhysicalOnline = true;
                var loginReq = new C2S_Login
                {
                    AccountId = ClientApp.Session.AccountId,
                    ClientVersion = Application.version
                };
                ClientApp.SendMessage(loginReq);
            }

            OnClientConnectedEvent?.Invoke();
        }

        public override void OnClientDisconnect()
        {
            if (ClientApp != null)
            {
                if (ClientApp.State == ClientAppState.OnlineRoom)
                {
                    NetLogger.LogWarning("StellarNetManager", "物理连接意外断开，触发软挂起与自动重试机制");
                    ClientApp.SuspendConnection();
                    if (_reconnectCoroutine != null) StopCoroutine(_reconnectCoroutine);
                    _reconnectCoroutine = StartCoroutine(ReconnectionRoutine());
                }
                else if (ClientApp.State == ClientAppState.ConnectionSuspended)
                {
                    NetLogger.LogInfo("StellarNetManager", "重试尝试失败，等待下一轮...");
                }
                else
                {
                    NetLogger.LogInfo("StellarNetManager", "非在线对局状态下断开，执行常规硬清理");
                    ClientApp.AbortConnection();
                }
            }

            OnClientDisconnectedEvent?.Invoke();
            base.OnClientDisconnect();
        }

        private IEnumerator ReconnectionRoutine()
        {
            ClientApp.Session.IsReconnecting = true;
            DateTime startTime = ClientApp.Session.LastDisconnectRealtime;
            float timeoutSeconds = 15f;
            float retryInterval = 2f;
            float lastRetryTime = 0f;

            while (true)
            {
                float elapsed = (float)(DateTime.UtcNow - startTime).TotalSeconds;
                float remaining = timeoutSeconds - elapsed;

                if (remaining <= 0)
                {
                    NetLogger.LogError("StellarNetManager", "15秒自动重试超时，抛出交互事件等待玩家决策");
                    ClientApp.Session.IsReconnecting = false;
                    GlobalTypeNetEvent.Broadcast(new Local_ReconnectTimeout());
                    yield break;
                }

                GlobalTypeNetEvent.Broadcast(new Local_ConnectionSuspended { RemainingSeconds = remaining });

                if (Time.realtimeSinceStartup - lastRetryTime >= retryInterval)
                {
                    if (!NetworkClient.active && !NetworkClient.isConnected)
                    {
                        NetLogger.LogInfo("StellarNetManager", $"发起物理重连尝试... 剩余时间: {remaining:F1}s");
                        lastRetryTime = Time.realtimeSinceStartup;
                        StartClient();
                    }
                }

                yield return null;
            }
        }

        public void RestartReconnectionRoutine()
        {
            if (ClientApp == null || ClientApp.State != ClientAppState.ConnectionSuspended) return;
            ClientApp.Session.LastDisconnectRealtime = DateTime.UtcNow;
            if (_reconnectCoroutine != null) StopCoroutine(_reconnectCoroutine);
            _reconnectCoroutine = StartCoroutine(ReconnectionRoutine());
        }

        private void OnClientReceivePacket(MirrorPacketMsg msg)
        {
            NetworkMonitor?.OnPacketReceived();
            ClientApp?.OnReceivePacket(msg.ToPacket());
        }

        #endregion
    }
}