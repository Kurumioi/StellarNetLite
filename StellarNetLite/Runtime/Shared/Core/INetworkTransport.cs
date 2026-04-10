using System;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Shared.Core
{
    /// <summary>
    /// 统一网络传输层抽象接口。
    /// 架构意图：提供完整的物理层生命周期与收发包契约，彻底隔离底层网络库（如 Mirror、KCP 等），实现业务层与传输层的绝对解耦。
    /// </summary>
    public interface INetworkTransport
    {
        #region 服务端物理事件契约

        event Action OnServerStartedEvent;
        event Action OnServerStoppedEvent;
        event Action<int> OnServerClientConnectedEvent;
        event Action<int> OnServerClientDisconnectedEvent;
        event Action<int, Packet> OnServerReceivePacketEvent;

        #endregion

        #region 客户端物理事件契约

        event Action OnClientStartedEvent;
        event Action OnClientStoppedEvent;
        event Action OnClientConnectedEvent;
        event Action OnClientDisconnectedEvent;
        event Action<Packet> OnClientReceivePacketEvent;

        #endregion

        #region 物理层主动行为契约

        // 客户端侧调用，发包到服务端。
        void SendToServer(Packet packet);

        // 服务端侧调用，发包到指定连接。
        void SendToClient(int connectionId, Packet packet);

        // 服务端主动断开某个客户端。
        void DisconnectClient(int connectionId);

        // 启动与停止控制。
        void StartServer();
        void StartClient();
        void StartHost();
        void StopServer();
        void StopClient();

        // 获取当前 RTT，供网络监视器展示。
        float GetRTT();

        // 应用逻辑层下发的网络配置（如端口、最大连接数）。
        void ApplyConfig(NetConfig config);

        #endregion
    }
}