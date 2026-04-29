using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Core
{
    /// <summary>
    /// 客户端网络门面。
    /// </summary>
    public static class NetClient
    {
        /// <summary>
        /// 当前绑定的客户端主状态机。
        /// </summary>
        private static ClientApp _app;

        /// <summary>
        /// 当前客户端主状态机。
        /// </summary>
        public static ClientApp App => _app;

        /// <summary>
        /// 当前客户端会话。
        /// </summary>
        public static ClientSession Session => _app?.Session;

        /// <summary>
        /// 当前客户端状态。
        /// </summary>
        public static ClientAppState State => _app?.State ?? ClientAppState.InLobby;

        /// <summary>
        /// 当前客户端房间。
        /// </summary>
        public static ClientRoom CurrentRoom => _app?.CurrentRoom;

        /// <summary>
        /// 绑定客户端主状态机。
        /// </summary>
        public static void Initialize(ClientApp app)
        {
            _app = app;
        }

        /// <summary>
        /// 发送一条协议消息。
        /// </summary>
        public static void Send<T>(T msg) where T : class
        {
            if (_app == null)
            {
                NetLogger.LogError("NetClient", "发送失败: ClientApp 未初始化");
                return;
            }

            _app.SendMessage(msg);
        }
    }
}
