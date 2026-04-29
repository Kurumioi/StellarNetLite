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
        /// <summary>
        /// Runtime 宿主 Awake 后调用。
        /// </summary>
        void OnRuntimeAwake(StellarNetAppManager appManager);

        /// <summary>
        /// ServerApp 创建完成后调用。
        /// </summary>
        void OnServerAppCreated(StellarNetAppManager appManager, ServerApp serverApp);

        /// <summary>
        /// 服务端会话状态变化后调用。
        /// </summary>
        void OnServerSessionStateChanged(StellarNetAppManager appManager, ServerApp serverApp, Session session);

        /// <summary>
        /// 服务端踢人前尝试通知扩展层。
        /// 返回 true 表示扩展已自行处理通知链。
        /// </summary>
        bool TryNotifyServerSessionKick(StellarNetAppManager appManager, ServerApp serverApp, Session session, string reason);

        /// <summary>
        /// ClientApp 创建完成后调用。
        /// </summary>
        void OnClientAppCreated(StellarNetAppManager appManager, ClientApp clientApp);

        /// <summary>
        /// 客户端物理连接成功后调用。
        /// </summary>
        void OnClientConnected(StellarNetAppManager appManager, ClientApp clientApp);

        /// <summary>
        /// 客户端物理断开后调用。
        /// </summary>
        void OnClientDisconnected(StellarNetAppManager appManager, ClientApp clientApp);

        /// <summary>
        /// 客户端收到网络包后调用。
        /// </summary>
        void OnClientPacketReceived(StellarNetAppManager appManager, ClientApp clientApp, Packet packet);

        /// <summary>
        /// 客户端停止后调用。
        /// </summary>
        void OnClientStopped(StellarNetAppManager appManager, ClientApp clientApp);
    }

    /// <summary>
    /// Runtime 扩展桥基类。
    /// 默认提供空实现，降低扩展接入样板代码量。
    /// </summary>
    public abstract class RuntimeFeatureBridgeBase : IRuntimeFeatureBridge
    {
        /// <summary>
        /// 默认空实现。
        /// </summary>
        public virtual void OnRuntimeAwake(StellarNetAppManager appManager)
        {
        }

        /// <summary>
        /// 默认空实现。
        /// </summary>
        public virtual void OnServerAppCreated(StellarNetAppManager appManager, ServerApp serverApp)
        {
        }

        /// <summary>
        /// 默认空实现。
        /// </summary>
        public virtual void OnServerSessionStateChanged(StellarNetAppManager appManager, ServerApp serverApp, Session session)
        {
        }

        /// <summary>
        /// 默认返回未处理。
        /// </summary>
        public virtual bool TryNotifyServerSessionKick(StellarNetAppManager appManager, ServerApp serverApp, Session session, string reason)
        {
            return false;
        }

        /// <summary>
        /// 默认空实现。
        /// </summary>
        public virtual void OnClientAppCreated(StellarNetAppManager appManager, ClientApp clientApp)
        {
        }

        /// <summary>
        /// 默认空实现。
        /// </summary>
        public virtual void OnClientConnected(StellarNetAppManager appManager, ClientApp clientApp)
        {
        }

        /// <summary>
        /// 默认空实现。
        /// </summary>
        public virtual void OnClientDisconnected(StellarNetAppManager appManager, ClientApp clientApp)
        {
        }

        /// <summary>
        /// 默认空实现。
        /// </summary>
        public virtual void OnClientPacketReceived(StellarNetAppManager appManager, ClientApp clientApp, Packet packet)
        {
        }

        /// <summary>
        /// 默认空实现。
        /// </summary>
        public virtual void OnClientStopped(StellarNetAppManager appManager, ClientApp clientApp)
        {
        }
    }
}
