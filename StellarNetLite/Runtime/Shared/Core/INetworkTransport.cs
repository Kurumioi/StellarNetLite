using System;

namespace StellarNet.Lite.Shared.Core
{
    /// <summary>
    /// 统一网络传输层抽象接口。
    /// 架构意图：隔离底层网络库（如 Mirror, KCP, LiteNetLib），实现业务层与传输层的彻底解耦。
    /// </summary>
    public interface INetworkTransport
    {
        void SendToServer(Packet packet);
        void SendToClient(int connectionId, Packet packet);
        void DisconnectClient(int connectionId);
        void StopServer();
        void StopClient();
        float GetRTT();
    }
}