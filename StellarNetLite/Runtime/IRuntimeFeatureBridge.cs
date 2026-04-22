using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Runtime
{
    /// <summary>
    /// Runtime 扩展桥接口。
    /// 用于让 Extensions 在不反向侵入 Runtime 的前提下接入启动链、连接生命周期与消息桥接。
    /// </summary>
    public interface IRuntimeFeatureBridge
    {
        void OnRuntimeAwake(StellarNetAppManager appManager);
        void OnServerAppCreated(StellarNetAppManager appManager, ServerApp serverApp);
        void OnServerSessionStateChanged(StellarNetAppManager appManager, ServerApp serverApp, Session session);
        bool TryNotifyServerSessionKick(StellarNetAppManager appManager, ServerApp serverApp, Session session, string reason);
        void OnClientAppCreated(StellarNetAppManager appManager, ClientApp clientApp);
        void OnClientConnected(StellarNetAppManager appManager, ClientApp clientApp);
        void OnClientDisconnected(StellarNetAppManager appManager, ClientApp clientApp);
        void OnClientPacketReceived(StellarNetAppManager appManager, ClientApp clientApp, Packet packet);
        void OnClientStopped(StellarNetAppManager appManager, ClientApp clientApp);
    }

    /// <summary>
    /// Runtime 扩展桥基类。
    /// 默认提供空实现，降低扩展接入样板代码量。
    /// </summary>
    public abstract class RuntimeFeatureBridgeBase : IRuntimeFeatureBridge
    {
        public virtual void OnRuntimeAwake(StellarNetAppManager appManager)
        {
        }

        public virtual void OnServerAppCreated(StellarNetAppManager appManager, ServerApp serverApp)
        {
        }

        public virtual void OnServerSessionStateChanged(StellarNetAppManager appManager, ServerApp serverApp, Session session)
        {
        }

        public virtual bool TryNotifyServerSessionKick(StellarNetAppManager appManager, ServerApp serverApp, Session session, string reason)
        {
            return false;
        }

        public virtual void OnClientAppCreated(StellarNetAppManager appManager, ClientApp clientApp)
        {
        }

        public virtual void OnClientConnected(StellarNetAppManager appManager, ClientApp clientApp)
        {
        }

        public virtual void OnClientDisconnected(StellarNetAppManager appManager, ClientApp clientApp)
        {
        }

        public virtual void OnClientPacketReceived(StellarNetAppManager appManager, ClientApp clientApp, Packet packet)
        {
        }

        public virtual void OnClientStopped(StellarNetAppManager appManager, ClientApp clientApp)
        {
        }
    }
}
