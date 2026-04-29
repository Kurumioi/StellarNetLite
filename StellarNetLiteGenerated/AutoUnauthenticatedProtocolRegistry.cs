namespace StellarNet.Lite.Shared.Protocol
{
    /// <summary>
    /// 编辑器扫描后生成的未鉴权协议白名单。
    /// </summary>
    public static class AutoUnauthenticatedProtocolRegistry
    {
        /// <summary>
        /// 允许在未登录阶段发送的 Global C2S 协议 Id 列表。
        /// </summary>
        public static readonly int[] GlobalC2SMsgIds = new[] { MsgIdConst.C2S_Login };
    }
}
