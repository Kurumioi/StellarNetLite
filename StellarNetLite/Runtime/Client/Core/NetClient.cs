using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Core
{
    /// <summary>
    /// 客户端网络全局门面 (Facade)
    /// 职责：提供极简的 API 供表现层 (View/UI) 调用，彻底隔离对底层 Manager 和 App 实例的链式强依赖。
    /// </summary>
    public static class NetClient
    {
        private static ClientApp _app;

        /// <summary>
        /// 暴露底层核心实例，仅供特殊模块（如回放沙盒、静态扩展方法）使用。
        /// 常规 UI 业务严禁直接操作 App，必须通过门面提供的属性与 Send 方法。
        /// </summary>
        public static ClientApp App => _app;

        public static void Initialize(ClientApp app)
        {
            _app = app;
        }

        public static void Send<T>(T msg) where T : class
        {
            if (_app == null)
            {
                NetLogger.LogError("[NetClient]", "发送失败: ClientApp 尚未初始化");
                return;
            }

            _app.SendMessage(msg);
        }

        public static ClientSession Session => _app?.Session;

        public static ClientAppState State => _app?.State ?? ClientAppState.InLobby;

        public static ClientRoom CurrentRoom => _app?.CurrentRoom;
    }
}