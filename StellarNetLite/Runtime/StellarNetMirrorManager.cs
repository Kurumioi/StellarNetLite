using System;
using System.Collections;
using Mirror;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Client.Infrastructure;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Server.Modules;
using StellarNet.Lite.Shared.Binders;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;

namespace StellarNet.Lite.Shared.Infrastructure
{
    public class StellarNetMirrorManager : NetworkManager, INetworkTransport
    {
        public INetSerializer Serializer { get; private set; }

        private NetConfig _netConfig;
        private bool _isCoreInitialized;

        public override void Awake()
        {
            base.Awake();

            Serializer = new LiteNetSerializer();
            _netConfig = NetConfigLoader.LoadServerConfigSync(ConfigRootPath.StreamingAssets);
            ApplyConfigInternal(_netConfig);

            NetMessageMapper.Initialize();

            Func<byte[], int, int, Type, object> deserializeFunc = Serializer.Deserialize;
            ServerRoomFactory.ComponentBinder = (comp, dispatcher) =>
                AutoRegistry.BindServerComponent(comp, dispatcher, deserializeFunc);
            ClientRoomFactory.ComponentBinder = (comp, dispatcher) =>
                AutoRegistry.BindClientComponent(comp, dispatcher, deserializeFunc);

            _isCoreInitialized = true;
        }

        public void ApplyConfig(NetConfig config)
        {
            if (config == null)
            {
                NetLogger.LogError("StellarNetMirrorManager", "应用配置失败: 传入 config 为空");
                return;
            }

            _netConfig = config;
            ApplyConfigInternal(_netConfig);
        }

        private void ApplyConfigInternal(NetConfig config)
        {
            if (config == null)
            {
                NetLogger.LogError("StellarNetMirrorManager", "应用配置失败: 当前 config 为空");
                return;
            }

            maxConnections = config.MaxConnections;
            networkAddress = config.Ip;

            Transport transport = GetComponent<Transport>();
            if (transport == null)
            {
                NetLogger.LogWarning("StellarNetMirrorManager",
                    $"未找到 Transport 组件，已仅应用逻辑层配置。Ip:{config.Ip}, Port:{config.Port}");
                return;
            }

            if (transport is PortTransport portTransport)
            {
                portTransport.Port = config.Port;
                NetLogger.LogInfo("StellarNetMirrorManager", $"底层传输端口已更新。Ip:{config.Ip}, Port:{config.Port}");
                return;
            }

            NetLogger.LogError(
                "StellarNetMirrorManager",
                $"应用配置失败: 当前 Transport 未实现 PortTransport，Runtime 严禁使用反射设置端口。Transport:{transport.GetType().Name}, Ip:{config.Ip}, Port:{config.Port}");
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active)
            {
                return;
            }

            if (ServerApp == null)
            {
                return;
            }

            ServerApp.Tick();
        }

        #region ================= INetworkTransport 接口实现 =================

        public void SendToServer(Packet packet)
        {
            if (!NetworkClient.ready)
            {
                NetLogger.LogWarning("StellarNetMirrorManager", $"发送到服务端失败: NetworkClient 未就绪, MsgId:{packet.MsgId}");
                return;
            }

            NetworkClient.Send(new MirrorPacketMsg(packet));
        }

        public void SendToClient(int connectionId, Packet packet)
        {
            if (!NetworkServer.connections.TryGetValue(connectionId, out NetworkConnectionToClient conn) ||
                conn == null)
            {
                NetLogger.LogError("StellarNetMirrorManager",
                    $"发送到客户端失败: 连接不存在, ConnId:{connectionId}, MsgId:{packet.MsgId}");
                return;
            }

            conn.Send(new MirrorPacketMsg(packet));
        }

        public void DisconnectClient(int connectionId)
        {
            if (!NetworkServer.connections.TryGetValue(connectionId, out NetworkConnectionToClient conn) ||
                conn == null)
            {
                NetLogger.LogWarning("StellarNetMirrorManager", $"断开客户端失败: 连接不存在, ConnId:{connectionId}");
                return;
            }

            conn.Disconnect();
        }

        public new void StopServer()
        {
            base.StopServer();
        }

        public new void StopClient()
        {
            base.StopClient();
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

            if (!_isCoreInitialized)
            {
                NetLogger.LogError("StellarNetMirrorManager", "服务端启动失败: 核心初始化未完成");
                return;
            }

            if (_netConfig == null)
            {
                NetLogger.LogError("StellarNetMirrorManager", "服务端启动失败: _netConfig 为空");
                return;
            }

            NetworkServer.tickRate = _netConfig.TickRate;
            ServerApp = new ServerApp(this, Serializer, _netConfig);

            Func<byte[], int, int, Type, object> deserializeFunc = Serializer.Deserialize;
            AutoRegistry.RegisterServer(ServerApp, deserializeFunc);
            NetworkServer.RegisterHandler<MirrorPacketMsg>(OnServerReceivePacket, false);

            NetLogger.LogInfo("StellarNetMirrorManager", $"服务端装配完毕。TickRate:{NetworkServer.tickRate}");
            OnServerStartedEvent?.Invoke();
        }

        public override void OnStopServer()
        {
            OnServerStoppedEvent?.Invoke();
            NetLogger.LogInfo("StellarNetMirrorManager", "服务端物理节点已停止运行");

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

            if (conn == null)
            {
                NetLogger.LogError("StellarNetMirrorManager", "服务端连接回调异常: conn 为空");
                return;
            }

            NetLogger.LogInfo("StellarNetMirrorManager", "物理连接建立", "-", "-", $"ConnId:{conn.connectionId}");
            OnServerClientConnectedEvent?.Invoke(conn.connectionId);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            if (conn == null)
            {
                NetLogger.LogError("StellarNetMirrorManager", "服务端断线回调异常: conn 为空");
                base.OnServerDisconnect(conn);
                return;
            }

            if (ServerApp != null)
            {
                Session session = ServerApp.TryGetSessionByConnectionId(conn.connectionId);
                if (session != null)
                {
                    NetLogger.LogInfo("StellarNetMirrorManager", "物理连接断开，触发会话离线", "-", session.SessionId,
                        $"ConnId:{conn.connectionId}");
                    ServerApp.UnbindConnection(session);
                }
            }

            ServerLobbyModule.BroadcastOnlinePlayerList(ServerApp);

            OnServerClientDisconnectedEvent?.Invoke(conn.connectionId);
            base.OnServerDisconnect(conn);
        }

        private void OnServerReceivePacket(NetworkConnectionToClient conn, MirrorPacketMsg msg)
        {
            if (conn == null)
            {
                NetLogger.LogError("StellarNetMirrorManager", $"服务端收包失败: conn 为空, MsgId:{msg.MsgId}");
                return;
            }

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

            if (!_isCoreInitialized)
            {
                NetLogger.LogError("StellarNetMirrorManager", "客户端启动失败: 核心初始化未完成");
                return;
            }

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
                NetworkMonitor = gameObject.GetComponent<ClientNetworkMonitor>();
                if (NetworkMonitor == null)
                {
                    NetworkMonitor = gameObject.AddComponent<ClientNetworkMonitor>();
                }
            }

            NetworkMonitor.Init(ClientApp, this);
            NetLogger.LogInfo("StellarNetMirrorManager", "客户端装配完毕，准备就绪。");
            OnClientStartedEvent?.Invoke();
        }

        public override void OnStopClient()
        {
            OnClientStoppedEvent?.Invoke();
            NetLogger.LogInfo("StellarNetMirrorManager", "客户端物理节点已停止运行");

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
            NetLogger.LogInfo("StellarNetMirrorManager", "成功连接到服务端");

            if (_reconnectCoroutine != null)
            {
                StopCoroutine(_reconnectCoroutine);
                _reconnectCoroutine = null;
            }

            if (ClientApp != null && ClientApp.Session.IsReconnecting)
            {
                if (string.IsNullOrEmpty(ClientApp.Session.AccountId))
                {
                    NetLogger.LogError("StellarNetMirrorManager", "物理连接恢复失败: AccountId 为空，无法自动发起 Login 重连链");
                }
                else
                {
                    NetLogger.LogInfo("StellarNetMirrorManager", "物理连接恢复，自动发起 Login 鉴权恢复链");
                    ClientApp.Session.IsPhysicalOnline = true;

                    var loginReq = new C2S_Login
                    {
                        AccountId = ClientApp.Session.AccountId,
                        ClientVersion = Application.version
                    };

                    ClientApp.SendMessage(loginReq);
                }
            }

            OnClientConnectedEvent?.Invoke();
        }

        public override void OnClientDisconnect()
        {
            if (ClientApp != null)
            {
                if (ClientApp.State == ClientAppState.OnlineRoom)
                {
                    NetLogger.LogWarning("StellarNetMirrorManager", "物理连接意外断开，进入软挂起与自动重试链");
                    ClientApp.SuspendConnection();

                    if (_reconnectCoroutine != null)
                    {
                        StopCoroutine(_reconnectCoroutine);
                    }

                    _reconnectCoroutine = StartCoroutine(ReconnectionRoutine());
                }
                else if (ClientApp.State == ClientAppState.ConnectionSuspended)
                {
                    NetLogger.LogInfo("StellarNetMirrorManager", "重试连接失败，等待下一轮自动重试");
                }
                else
                {
                    NetLogger.LogInfo("StellarNetMirrorManager", "非在线房间态断开，执行常规硬清理");
                    ClientApp.AbortConnection();
                }
            }

            OnClientDisconnectedEvent?.Invoke();
            base.OnClientDisconnect();
        }

        private IEnumerator ReconnectionRoutine()
        {
            if (ClientApp == null)
            {
                NetLogger.LogError("StellarNetMirrorManager", "重连协程启动失败: ClientApp 为空");
                yield break;
            }

            ClientApp.Session.IsReconnecting = true;
            DateTime startTime = ClientApp.Session.LastDisconnectRealtime;
            float timeoutSeconds = 15f;
            float retryInterval = 2f;
            float lastRetryTime = -999f;

            while (true)
            {
                float elapsed = (float)(DateTime.UtcNow - startTime).TotalSeconds;
                float remaining = timeoutSeconds - elapsed;
                if (remaining <= 0f)
                {
                    NetLogger.LogError("StellarNetMirrorManager", "15 秒自动重试超时，抛出交互事件等待玩家决策");
                    ClientApp.Session.IsReconnecting = false;
                    GlobalTypeNetEvent.Broadcast(new Local_ReconnectTimeout());
                    _reconnectCoroutine = null;
                    yield break;
                }

                GlobalTypeNetEvent.Broadcast(new Local_ConnectionSuspended { RemainingSeconds = remaining });

                if (Time.realtimeSinceStartup - lastRetryTime >= retryInterval)
                {
                    if (!NetworkClient.active && !NetworkClient.isConnected)
                    {
                        NetLogger.LogInfo("StellarNetMirrorManager", $"发起物理重连尝试，剩余时间:{remaining:F1}s");
                        lastRetryTime = Time.realtimeSinceStartup;
                        StartClient();
                    }
                }

                yield return null;
            }
        }

        public void RestartReconnectionRoutine()
        {
            if (ClientApp == null)
            {
                NetLogger.LogError("StellarNetMirrorManager", "重启重连协程失败: ClientApp 为空");
                return;
            }

            if (ClientApp.State != ClientAppState.ConnectionSuspended)
            {
                NetLogger.LogWarning("StellarNetMirrorManager", $"重启重连协程失败: 当前状态非法, State:{ClientApp.State}");
                return;
            }

            ClientApp.Session.LastDisconnectRealtime = DateTime.UtcNow;

            if (_reconnectCoroutine != null)
            {
                StopCoroutine(_reconnectCoroutine);
            }

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