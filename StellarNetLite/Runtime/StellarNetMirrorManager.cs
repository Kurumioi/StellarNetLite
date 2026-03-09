using System;
using System.Reflection;
using UnityEngine;
using Mirror;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Binders;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Server.Modules;
using StellarNet.Lite.Server.Components;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Modules;
using StellarNet.Lite.Client.Components;
// 引入 Demo 命名空间
using StellarNet.Lite.GameDemo.Server;
using StellarNet.Lite.GameDemo.Client;

namespace StellarNet.Lite.Shared.Infrastructure
{
    /// <summary>
    /// StellarNet Lite + Mirror 96.x 核心网络桥接器。
    /// 职责：接管 Mirror 的底层生命周期，将 Socket 字节流与连接事件，无缝桥接到 StellarNet 的纯粹业务状态机中。
    /// </summary>
    [RequireComponent(typeof(Transport))]
    public class StellarNetMirrorManager : NetworkManager
    {
        public ServerApp ServerApp { get; private set; }
        public ClientApp ClientApp { get; private set; }
        public Func<object, byte[]> SerializeFunc { get; private set; }
        public Func<byte[], Type, object> DeserializeFunc { get; private set; }

        private NetConfig _netConfig;

        // 核心修复：利用静态标记，确保静态工厂在整个 Unity 进程生命周期内只注册一次
        private static bool _factoriesRegistered = false;

        public override void Awake()
        {
            base.Awake();

            // 初始化基础序列化器
            var serializer = new JsonNetSerializer();
            SerializeFunc = serializer.Serialize;
            DeserializeFunc = serializer.Deserialize;

            _netConfig = new NetConfig();

            // 核心修复：将工厂注册从网络启动生命周期中剥离，移至 App 启动生命周期
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

        #region ================= 静态工厂装配 =================

        protected virtual void OnRegisterServerComponents()
        {
            ServerRoomFactory.Register(1, () => new ServerRoomSettingsComponent(SerializeFunc));
            // 注册 Demo 服务端组件
            ServerRoomFactory.Register(100, () => new ServerDemoGameComponent(SerializeFunc));

            ServerRoomFactory.ComponentBinder = (comp, dispatcher) =>
                AutoBinder.BindServerComponent(comp, dispatcher, DeserializeFunc);
        }

        protected virtual void OnRegisterClientComponents()
        {
            ClientRoomFactory.Register(1, () => new ClientRoomSettingsComponent());
            // 注册 Demo 客户端组件
            ClientRoomFactory.Register(100, () => new ClientDemoGameComponent());

            // 核心修复：移除 (ClientRoomSettingsComponent) 的错误强转，允许所有实现了 ClientRoomComponent 的多态组件正常绑定
            ClientRoomFactory.ComponentBinder = (comp, dispatcher) =>
                AutoBinder.BindClientComponent(comp, dispatcher, DeserializeFunc);
        }

        #endregion

        #region ================= 服务端 (Server) 桥接逻辑 =================

        public override void OnStartServer()
        {
            base.OnStartServer();

            // 1. 实例化 ServerApp
            ServerApp = new ServerApp(MirrorServerSend);

            // 2. 实例化全局业务模块
            var userModule = new ServerUserModule(ServerApp, MirrorServerSend, SerializeFunc);
            var roomModule = new ServerRoomModule(ServerApp, MirrorServerSend, SerializeFunc);

            // 3. 自动装配全局模块路由 (App实例级别的路由，每次启动都需要重新绑定)
            AutoBinder.BindServerModule(userModule, ServerApp.GlobalDispatcher, DeserializeFunc);
            AutoBinder.BindServerModule(roomModule, ServerApp.GlobalDispatcher, DeserializeFunc);

            // 4. 注册 Mirror 底层消息监听
            NetworkServer.RegisterHandler<MirrorPacketMsg>(OnServerReceivePacket, false);

            Debug.Log("<color=green>[StellarNet Server] 服务端装配完毕，开始监听网络请求。</color>");
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            if (ServerApp != null)
            {
                var method = typeof(ServerApp).GetMethod("GetSessionByConnectionId", BindingFlags.NonPublic | BindingFlags.Instance);
                if (method != null)
                {
                    var session = method.Invoke(ServerApp, new object[] { conn.connectionId }) as Session;
                    if (session != null)
                    {
                        ServerApp.UnbindConnection(session);
                        Debug.Log($"[StellarNet Server] 物理连接 {conn.connectionId} 断开，已触发 Session {session.SessionId} 离线逻辑");
                    }
                }
            }

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

        #region ================= 客户端 (Client) 桥接逻辑 =================

        public override void OnStartClient()
        {
            base.OnStartClient();

            // 1. 实例化 ClientApp
            ClientApp = new ClientApp(MirrorClientSend);

            // 2. 实例化全局业务模块
            var userModule = new ClientUserModule(ClientApp, MirrorClientSend, SerializeFunc);
            var roomModule = new ClientRoomModule(ClientApp, MirrorClientSend, SerializeFunc);

            // 3. 自动装配全局模块路由
            AutoBinder.BindClientModule(userModule, ClientApp.GlobalDispatcher, DeserializeFunc);
            AutoBinder.BindClientModule(roomModule, ClientApp.GlobalDispatcher, DeserializeFunc);

            // 4. 注册 Mirror 底层消息监听
            NetworkClient.RegisterHandler<MirrorPacketMsg>(OnClientReceivePacket, false);

            Debug.Log("<color=green>[StellarNet Client] 客户端装配完毕，准备就绪。</color>");
        }

        public override void OnClientDisconnect()
        {
            if (ClientApp != null)
            {
                ClientApp.LeaveRoom();
                ClientApp.Session.Clear();
            }

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