namespace StellarNet.Lite.Client.Core.Events
{
    /// <summary>
    /// 弱网状态事件。
    /// </summary>
    public struct Local_NetworkQualityChanged
    {
        public int RttMs;
        public bool IsWeakNetWarn;
        public bool IsWeakNetBlock;
    }

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
    /// 回放倍速变化事件。
    /// </summary>
    public struct Local_ReplayTimeScaleChanged
    {
        public float TimeScale;
    }

    /// <summary>
    /// 回放下载进度事件。
    /// </summary>
    public struct Local_ReplayDownloadProgress
    {
        public string ReplayId;
        public int DownloadedBytes;
        public int TotalBytes;
    }

    /// <summary>
    /// 连接硬中止事件。
    /// </summary>
    public struct Local_ConnectionAborted
    {
    }

    /// <summary>
    /// 本地 Ping 结果事件。
    /// </summary>
    public struct Local_PingResult
    {
        public float RttMs;
    }
}
