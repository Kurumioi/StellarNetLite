namespace StellarNet.Lite.Client.Core.Events
{
    /// <summary>
    /// 连接挂起事件。
    /// </summary>
    public struct Local_ConnectionSuspended
    {
        /// <summary>
        /// 预计剩余的自动重连秒数。
        /// </summary>
        public float RemainingSeconds;
    }

    /// <summary>
    /// 自动重连超时事件。
    /// </summary>
    public struct Local_ReconnectTimeout
    {
    }

    /// <summary>
    /// 轻提示事件。
    /// </summary>
    public struct Local_SystemPrompt
    {
        /// <summary>
        /// 要显示给用户的提示文本。
        /// </summary>
        public string Message;
    }

    /// <summary>
    /// 连接硬中止事件。
    /// </summary>
    public struct Local_ConnectionAborted
    {
    }
}
