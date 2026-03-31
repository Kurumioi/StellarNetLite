using System;

namespace StellarNet.Lite.Shared.Core
{
    /// <summary>
    /// 统一网络传输层抽象接口。
    /// 架构意图：隔离底层网络库，实现业务层与传输层的彻底解耦。
    /// </summary>
    public interface INetworkTransport
    {
        // 客户端侧调用，发包到服务端。
        void SendToServer(Packet packet);
        // 服务端侧调用，发包到指定连接。
        void SendToClient(int connectionId, Packet packet);
        // 服务端主动断开某个客户端。
        void DisconnectClient(int connectionId);
        // 停止服务端监听。
        void StopServer();
        // 停止客户端连接。
        void StopClient();
        // 获取当前 RTT，供网络监视器展示。
        float GetRTT();
    }
}
