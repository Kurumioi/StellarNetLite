using System;
using UnityEngine;
using Mirror;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Binders;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Server.Modules;
using StellarNet.Lite.Server.Components;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Modules;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.GameDemo.Server;
using StellarNet.Lite.GameDemo.Client;

namespace StellarNet.Lite.Shared.Infrastructure
{
    public class StellarNetMirrorManager : NetworkManager
    {
        #region 全局配置与基础设施

        public Func<object, byte[]> SerializeFunc { get; private set; }
        public Func<byte[], Type, object> DeserializeFunc { get; private set; }

        private NetConfig _netConfig;
        private static bool _factoriesRegistered = false;

        public override void Awake()
        {
            base.Awake();

            var serializer = new JsonNetSerializer();
            SerializeFunc = serializer.Serialize;
            DeserializeFunc = serializer.Deserialize;

            _netConfig = NetConfigLoader.LoadServerConfigSync(ConfigRootPath.PersistentDataPath);
            this.maxConnections = _netConfig.MaxConnections;

            NetMessageMapper.Initialize();

            if (!_factoriesRegistered)
            {
                OnRegisterServerComponents();
                OnRegisterClientComponents();
                _factoriesRegistered = true;
            }
        }

        private void FixedUpdate()
        {
            if (NetworkServer.active && ServerApp != null)
            {
                ServerApp.Tick(_netConfig);
            }
        }

        #endregion

        #region 服务端专属 (状态、事件与逻辑)

        // 服务端核心应用实例
        public ServerApp ServerApp { get; private set; }

        // 服务端节点生命周期事件
        public static event Action OnServerStartedEvent;
        public static event Action OnServerStoppedEvent;

        // 服务端感知客户端连接状态事件 (附带连接ID)
        public static event Action<int> OnServerClientConnectedEvent;
        public static event Action<int> OnServerClientDisconnectedEvent;

        protected virtual void OnRegisterServerComponents()
        {
            ServerRoomFactory.Register(ComponentIdConst.RoomSettings, () => new ServerRoomSettingsComponent(SerializeFunc));
            ServerRoomFactory.Register(ComponentIdConst.DemoGame, () => new ServerDemoGameComponent(SerializeFunc));

            ServerRoomFactory.ComponentBinder = (comp, dispatcher) =>
                AutoBinder.BindServerComponent(comp, dispatcher, DeserializeFunc);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            NetworkServer.tickRate = _netConfig.TickRate;
            ServerApp = new ServerApp(MirrorServerSend, SerializeFunc);

            var userModule = new ServerUserModule(ServerApp, MirrorServerSend, SerializeFunc, _netConfig);
            var roomModule = new ServerRoomModule(ServerApp, MirrorServerSend, SerializeFunc);
            var lobbyModule = new ServerLobbyModule(ServerApp, MirrorServerSend, SerializeFunc);
            var replayModule = new ServerReplayModule(ServerApp, MirrorServerSend, SerializeFunc);

            AutoBinder.BindServerModule(userModule, ServerApp.GlobalDispatcher, DeserializeFunc);
            AutoBinder.BindServerModule(roomModule, ServerApp.GlobalDispatcher, DeserializeFunc);
            AutoBinder.BindServerModule(lobbyModule, ServerApp.GlobalDispatcher, DeserializeFunc);
            AutoBinder.BindServerModule(replayModule, ServerApp.GlobalDispatcher, DeserializeFunc);

            NetworkServer.RegisterHandler<MirrorPacketMsg>(OnServerReceivePacket, false);

            LiteLogger.LogInfo("StellarNetManager", $"服务端装配完毕，开始监听网络请求。TickRate: {NetworkServer.tickRate}, MaxConn: {this.maxConnections}");

            OnServerStartedEvent?.Invoke();
        }

        public override void OnStopServer()
        {
            OnServerStoppedEvent?.Invoke();
            LiteLogger.LogInfo("StellarNetManager", "服务端物理节点已停止运行");

            base.OnStopServer();
        }

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            base.OnServerConnect(conn);

            LiteLogger.LogInfo("StellarNetManager", $"物理连接建立", "-", "-", $"ConnId:{conn.connectionId}");
            OnServerClientConnectedEvent?.Invoke(conn.connectionId);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            if (ServerApp != null)
            {
                var session = ServerApp.TryGetSessionByConnectionId(conn.connectionId);
                if (session != null)
                {
                    LiteLogger.LogInfo("StellarNetManager", $"物理连接断开，触发会话离线", "-", session.SessionId, $"ConnId:{conn.connectionId}");
                    ServerApp.UnbindConnection(session);
                }
            }

            OnServerClientDisconnectedEvent?.Invoke(conn.connectionId);
            base.OnServerDisconnect(conn);
        }

        private void MirrorServerSend(int connId, Packet packet)
        {
            if (NetworkServer.connections.TryGetValue(connId, out var conn))
            {
                conn.Send(new MirrorPacketMsg(packet));
            }
        }

        private void OnServerReceivePacket(NetworkConnectionToClient conn, MirrorPacketMsg msg)
        {
            ServerApp.OnReceivePacket(conn.connectionId, msg.ToPacket());
        }

        #endregion

        #region 客户端专属 (状态、事件与逻辑)

        // 客户端核心应用实例
        public ClientApp ClientApp { get; private set; }

        // 客户端节点生命周期事件
        public static event Action OnClientStartedEvent;
        public static event Action OnClientStoppedEvent;

        // 客户端感知服务端连接状态事件
        public static event Action OnClientConnectedEvent;
        public static event Action OnClientDisconnectedEvent;

        protected virtual void OnRegisterClientComponents()
        {
            ClientRoomFactory.Register(ComponentIdConst.RoomSettings, () => new ClientRoomSettingsComponent());
            ClientRoomFactory.Register(ComponentIdConst.DemoGame, () => new ClientDemoGameComponent());

            ClientRoomFactory.ComponentBinder = (comp, dispatcher) =>
                AutoBinder.BindClientComponent(comp, dispatcher, DeserializeFunc);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            ClientApp = new ClientApp(MirrorClientSend, SerializeFunc);

            var userModule = new ClientUserModule(ClientApp, MirrorClientSend, SerializeFunc);
            var roomModule = new ClientRoomModule(ClientApp, MirrorClientSend, SerializeFunc);
            var lobbyModule = new ClientLobbyModule(ClientApp, MirrorClientSend, SerializeFunc);
            var replayModule = new ClientReplayModule(ClientApp, MirrorClientSend, SerializeFunc);

            AutoBinder.BindClientModule(userModule, ClientApp.GlobalDispatcher, DeserializeFunc);
            AutoBinder.BindClientModule(roomModule, ClientApp.GlobalDispatcher, DeserializeFunc);
            AutoBinder.BindClientModule(lobbyModule, ClientApp.GlobalDispatcher, DeserializeFunc);
            AutoBinder.BindClientModule(replayModule, ClientApp.GlobalDispatcher, DeserializeFunc);

            NetworkClient.RegisterHandler<MirrorPacketMsg>(OnClientReceivePacket, false);

            LiteLogger.LogInfo("StellarNetManager", "客户端装配完毕，准备就绪。");

            OnClientStartedEvent?.Invoke();
        }

        public override void OnStopClient()
        {
            OnClientStoppedEvent?.Invoke();
            LiteLogger.LogInfo("StellarNetManager", "客户端物理节点已停止运行");

            base.OnStopClient();
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();

            LiteLogger.LogInfo("StellarNetManager", "成功连接到服务端");
            OnClientConnectedEvent?.Invoke();
        }

        public override void OnClientDisconnect()
        {
            if (ClientApp != null)
            {
                LiteLogger.LogInfo("StellarNetManager", "与服务端的物理连接断开，清理本地房间与会话状态");
                ClientApp.LeaveRoom();
                ClientApp.Session.Clear();
            }

            OnClientDisconnectedEvent?.Invoke();
            base.OnClientDisconnect();
        }

        private void MirrorClientSend(Packet packet)
        {
            if (NetworkClient.ready)
            {
                NetworkClient.Send(new MirrorPacketMsg(packet));
            }
        }

        private void OnClientReceivePacket(MirrorPacketMsg msg)
        {
            ClientApp.OnReceivePacket(msg.ToPacket());
        }

        #endregion
    }
}