using System;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Shared.Core
{
    /// <summary>
    /// 统一网络传输层接口。
    /// </summary>
    public interface INetworkTransport
    {
        /// <summary>
        /// 服务端启动完成事件。
        /// </summary>
        event Action OnServerStartedEvent;

        /// <summary>
        /// 服务端停止完成事件。
        /// </summary>
        event Action OnServerStoppedEvent;

        /// <summary>
        /// 服务端收到客户端连接事件。
        /// </summary>
        event Action<int> OnServerClientConnectedEvent;

        /// <summary>
        /// 服务端收到客户端断开事件。
        /// </summary>
        event Action<int> OnServerClientDisconnectedEvent;

        /// <summary>
        /// 服务端收到数据包事件。
        /// </summary>
        event Action<int, Packet> OnServerReceivePacketEvent;

        /// <summary>
        /// 客户端启动完成事件。
        /// </summary>
        event Action OnClientStartedEvent;

        /// <summary>
        /// 客户端停止完成事件。
        /// </summary>
        event Action OnClientStoppedEvent;

        /// <summary>
        /// 客户端连接成功事件。
        /// </summary>
        event Action OnClientConnectedEvent;

        /// <summary>
        /// 客户端断开连接事件。
        /// </summary>
        event Action OnClientDisconnectedEvent;

        /// <summary>
        /// 客户端收到数据包事件。
        /// </summary>
        event Action<Packet> OnClientReceivePacketEvent;

        /// <summary>
        /// 客户端向服务端发送数据包。
        /// </summary>
        void SendToServer(Packet packet);

        /// <summary>
        /// 服务端向指定连接发送数据包。
        /// </summary>
        void SendToClient(int connectionId, Packet packet);

        /// <summary>
        /// 服务端主动断开指定连接。
        /// </summary>
        void DisconnectClient(int connectionId);

        /// <summary>
        /// 启动服务端。
        /// </summary>
        void StartServer();

        /// <summary>
        /// 启动客户端。
        /// </summary>
        void StartClient();

        /// <summary>
        /// 启动主机模式。
        /// </summary>
        void StartHost();

        /// <summary>
        /// 停止服务端。
        /// </summary>
        void StopServer();

        /// <summary>
        /// 停止客户端。
        /// </summary>
        void StopClient();

        /// <summary>
        /// 获取当前 RTT。
        /// </summary>
        float GetRTT();

        /// <summary>
        /// 应用网络配置。
        /// </summary>
        void ApplyConfig(NetConfig config);
    }
}
