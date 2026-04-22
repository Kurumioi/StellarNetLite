namespace StellarNet.Lite.Client.Core.Events
{
    /// <summary>
    /// 连接挂起事件。
    /// </summary>
    public struct Local_ConnectionSuspended
    {
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
        public string Message;
    }

    /// <summary>
    /// 连接硬中止事件。
    /// </summary>
    public struct Local_ConnectionAborted
    {
    }
}
