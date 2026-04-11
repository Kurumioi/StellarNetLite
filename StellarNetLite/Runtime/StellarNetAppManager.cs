using System;
using System.Collections;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Client.Infrastructure;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Server.Modules;
using StellarNet.Lite.Shared.Binders;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

namespace StellarNet.Lite.Runtime
{
    [RequireComponent(typeof(INetworkTransport))]
    /// <summary>
    /// StellarNet Lite 的统一运行时入口。
    /// 负责装配传输层、序列化器、客户端逻辑宿主和服务端逻辑宿主。
    /// </summary>
    public class StellarNetAppManager : MonoBehaviour
    {
        /// <summary>
        /// 当前运行时使用的消息序列化器。
        /// </summary>
        public INetSerializer Serializer { get; private set; }

        /// <summary>
        /// 当前挂载的物理传输层组件。
        /// </summary>
        public INetworkTransport Transport { get; private set; }

        private NetConfig _netConfig;
        private bool _isCoreInitialized;

        public void Awake()
        {
            Transport = GetComponent<INetworkTransport>();
            if (Transport == null)
            {
                NetLogger.LogError("StellarNetAppManager", "初始化失败: 未找到实现 INetworkTransport 的物理层组件");
                return;
            }

            Serializer = new LiteNetSerializer();
            _netConfig = NetConfigLoader.LoadServerConfigSync(ConfigRootPath.StreamingAssets);
            Transport.ApplyConfig(_netConfig);

            NetMessageMapper.Initialize();

            Func<byte[], int, int, Type, object> deserializeFunc = Serializer.Deserialize;
            ServerRoomFactory.ComponentBinder = (comp, dispatcher) =>
                AutoRegistry.BindServerComponent(comp, dispatcher, deserializeFunc);
            ClientRoomFactory.ComponentBinder = (comp, dispatcher) =>
                AutoRegistry.BindClientComponent(comp, dispatcher, deserializeFunc);

            _isCoreInitialized = true;

            Transport.OnServerStartedEvent += HandleServerStarted;
            Transport.OnServerStoppedEvent += HandleServerStopped;
            Transport.OnServerClientConnectedEvent += HandleServerClientConnected;
            Transport.OnServerClientDisconnectedEvent += HandleServerClientDisconnected;
            Transport.OnServerReceivePacketEvent += HandleServerReceivePacket;

            Transport.OnClientStartedEvent += HandleClientStarted;
            Transport.OnClientStoppedEvent += HandleClientStopped;
            Transport.OnClientConnectedEvent += HandleClientConnected;
            Transport.OnClientDisconnectedEvent += HandleClientDisconnected;
            Transport.OnClientReceivePacketEvent += HandleClientReceivePacket;
        }

        private void OnDestroy()
        {
            if (Transport != null)
            {
                Transport.OnServerStartedEvent -= HandleServerStarted;
                Transport.OnServerStoppedEvent -= HandleServerStopped;
                Transport.OnServerClientConnectedEvent -= HandleServerClientConnected;
                Transport.OnServerClientDisconnectedEvent -= HandleServerClientDisconnected;
                Transport.OnServerReceivePacketEvent -= HandleServerReceivePacket;

                Transport.OnClientStartedEvent -= HandleClientStarted;
                Transport.OnClientStoppedEvent -= HandleClientStopped;
                Transport.OnClientConnectedEvent -= HandleClientConnected;
                Transport.OnClientDisconnectedEvent -= HandleClientDisconnected;
                Transport.OnClientReceivePacketEvent -= HandleClientReceivePacket;
            }
        }

        public void ApplyConfig(NetConfig config)
        {
            if (config == null)
            {
                NetLogger.LogError("StellarNetAppManager", "应用配置失败: 传入 config 为空");
                return;
            }
            _netConfig = config;
            Transport?.ApplyConfig(_netConfig);
        }

        private void FixedUpdate()
        {
            if (ServerApp != null)
            {
                ServerApp.Tick();
            }
        }

        #region ================= 物理层快捷代理 =================
        public void StartClient() => Transport?.StartClient();
        public void StopClient() => Transport?.StopClient();
        public void StartServer() => Transport?.StartServer();
        public void StopServer() => Transport?.StopServer();
        public void StartHost() => Transport?.StartHost();
        #endregion

        #region ================= 服务端逻辑宿主 =================
        /// <summary>
        /// 当前服务端逻辑宿主。
        /// </summary>
        public ServerApp ServerApp { get; private set; }

        public static event Action OnServerStartedEvent;
        public static event Action OnServerStoppedEvent;

        private void HandleServerStarted()
        {
            if (!_isCoreInitialized || _netConfig == null)
            {
                NetLogger.LogError("StellarNetAppManager", "服务端启动失败: 核心初始化未完成或配置为空");
                return;
            }

            ServerApp = new ServerApp(Transport, Serializer, _netConfig);
            ServerApp.RegisterUnauthenticatedGlobalProtocol(MsgIdConst.C2S_Login);
            Func<byte[], int, int, Type, object> deserializeFunc = Serializer.Deserialize;
            AutoRegistry.RegisterServer(ServerApp, deserializeFunc);

            NetLogger.LogInfo("StellarNetAppManager", $"服务端逻辑内核装配完毕。TickRate:{_netConfig.TickRate}");
            OnServerStartedEvent?.Invoke();
        }

        private void HandleServerStopped()
        {
            OnServerStoppedEvent?.Invoke();
            NetLogger.LogInfo("StellarNetAppManager", "服务端逻辑内核已停止运行");

            if (ServerApp != null)
            {
                ServerApp.Dispose();
                ServerApp = null;
            }
            ServerRoomFactory.Clear();
        }

        private void HandleServerClientConnected(int connectionId)
        {
            NetLogger.LogInfo("StellarNetAppManager", "物理连接建立，等待业务层鉴权", "-", "-", $"ConnId:{connectionId}");
        }

        private void HandleServerClientDisconnected(int connectionId)
        {
            if (ServerApp == null) return;
            Session session = ServerApp.TryGetSessionByConnectionId(connectionId);
            if (session != null)
            {
                NetLogger.LogInfo("StellarNetAppManager", "物理连接断开，触发会话离线", "-", session.SessionId, $"ConnId:{connectionId}");
                ServerApp.UnbindConnection(session);
            }
            ServerLobbyModule.BroadcastOnlinePlayerList(ServerApp);
        }

        private void HandleServerReceivePacket(int connectionId, Packet packet)
        {
            ServerApp?.OnReceivePacket(connectionId, packet);
        }
        #endregion

        #region ================= 客户端逻辑宿主 =================
        /// <summary>
        /// 当前客户端逻辑宿主。
        /// </summary>
        public ClientApp ClientApp { get; private set; }

        /// <summary>
        /// 当前客户端网络质量监控器。
        /// </summary>
        public ClientNetworkMonitor NetworkMonitor { get; private set; }
        private Coroutine _reconnectCoroutine;

        public static event Action OnClientStartedEvent;
        public static event Action OnClientStoppedEvent;
        public static event Action OnClientConnectedEvent;
        public static event Action OnClientDisconnectedEvent;

        private void HandleClientStarted()
        {
            if (!_isCoreInitialized)
            {
                NetLogger.LogError("StellarNetAppManager", "客户端启动失败: 核心初始化未完成");
                return;
            }

            if (ClientApp == null)
            {
                ClientApp = new ClientApp(Transport, Serializer);
                
                // 业务层主动注册弱网豁免白名单，解耦底层引擎
                ClientApp.RegisterWeakNetBypassProtocol(MsgIdConst.C2S_Ping);
                ClientApp.RegisterWeakNetBypassProtocol(MsgIdConst.C2S_Login);
                ClientApp.RegisterWeakNetBypassProtocol(MsgIdConst.C2S_ConfirmReconnect);
                ClientApp.RegisterWeakNetBypassProtocol(MsgIdConst.C2S_ReconnectReady);

                NetClient.Initialize(ClientApp);
                Func<byte[], int, int, Type, object> deserializeFunc = Serializer.Deserialize;
                AutoRegistry.RegisterClient(ClientApp, deserializeFunc);
            }

            if (NetworkMonitor == null)
            {
                NetworkMonitor = gameObject.GetComponent<ClientNetworkMonitor>();
                if (NetworkMonitor == null)
                {
                    NetworkMonitor = gameObject.AddComponent<ClientNetworkMonitor>();
                }
            }
            NetworkMonitor.Init(ClientApp, Transport);

            NetLogger.LogInfo("StellarNetAppManager", "客户端逻辑内核装配完毕，准备就绪。");
            OnClientStartedEvent?.Invoke();
        }

        private void HandleClientStopped()
        {
            OnClientStoppedEvent?.Invoke();
            NetLogger.LogInfo("StellarNetAppManager", "客户端逻辑内核已停止运行");

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
        }

        private void HandleClientConnected()
        {
            NetLogger.LogInfo("StellarNetAppManager", "成功连接到服务端");
            if (_reconnectCoroutine != null)
            {
                StopCoroutine(_reconnectCoroutine);
                _reconnectCoroutine = null;
            }

            if (ClientApp != null && ClientApp.Session.IsReconnecting)
            {
                if (string.IsNullOrEmpty(ClientApp.Session.AccountId))
                {
                    NetLogger.LogError("StellarNetAppManager", "物理连接恢复失败: AccountId 为空，无法自动发起 Login 重连链");
                }
                else
                {
                    NetLogger.LogInfo("StellarNetAppManager", "物理连接恢复，自动发起 Login 鉴权恢复链");
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

        private void HandleClientDisconnected()
        {
            if (ClientApp != null)
            {
                if (ClientApp.State == ClientAppState.OnlineRoom)
                {
                    NetLogger.LogWarning("StellarNetAppManager", "物理连接意外断开，进入软挂起与自动重试链");
                    ClientApp.SuspendConnection();
                    if (_reconnectCoroutine != null)
                    {
                        StopCoroutine(_reconnectCoroutine);
                    }
                    _reconnectCoroutine = StartCoroutine(ReconnectionRoutine());
                }
                else if (ClientApp.State == ClientAppState.ConnectionSuspended)
                {
                    NetLogger.LogInfo("StellarNetAppManager", "重试连接失败，等待下一轮自动重试");
                }
                else
                {
                    NetLogger.LogInfo("StellarNetAppManager", "非在线房间态断开，执行常规硬清理");
                    ClientApp.AbortConnection();
                }
            }
            OnClientDisconnectedEvent?.Invoke();
        }

        private void HandleClientReceivePacket(Packet packet)
        {
            NetworkMonitor?.OnPacketReceived();
            ClientApp?.OnReceivePacket(packet);
        }

        private IEnumerator ReconnectionRoutine()
        {
            if (ClientApp == null || Transport == null)
            {
                NetLogger.LogError("StellarNetAppManager", "重连协程启动失败: 核心组件为空");
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
                    NetLogger.LogError("StellarNetAppManager", "15 秒自动重试超时，抛出交互事件等待玩家决策");
                    ClientApp.Session.IsReconnecting = false;
                    GlobalTypeNetEvent.Broadcast(new Local_ReconnectTimeout());
                    _reconnectCoroutine = null;
                    yield break;
                }

                GlobalTypeNetEvent.Broadcast(new Local_ConnectionSuspended { RemainingSeconds = remaining });

                if (Time.realtimeSinceStartup - lastRetryTime >= retryInterval)
                {
                    NetLogger.LogInfo("StellarNetAppManager", $"发起物理重连尝试，剩余时间:{remaining:F1}s");
                    lastRetryTime = Time.realtimeSinceStartup;
                    Transport.StartClient();
                }

                yield return null;
            }
        }

        public void RestartReconnectionRoutine()
        {
            if (ClientApp == null)
            {
                NetLogger.LogError("StellarNetAppManager", "重启重连协程失败: ClientApp 为空");
                return;
            }

            if (ClientApp.State != ClientAppState.ConnectionSuspended)
            {
                NetLogger.LogWarning("StellarNetAppManager", $"重启重连协程失败: 当前状态非法, State:{ClientApp.State}");
                return;
            }

            ClientApp.Session.LastDisconnectRealtime = DateTime.UtcNow;
            if (_reconnectCoroutine != null)
            {
                StopCoroutine(_reconnectCoroutine);
            }
            _reconnectCoroutine = StartCoroutine(ReconnectionRoutine());
        }
        #endregion
    }
}
