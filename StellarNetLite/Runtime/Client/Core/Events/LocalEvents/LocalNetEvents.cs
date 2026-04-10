using StellarNet.Lite.Client.Core;

namespace StellarNet.Lite.Client.Core.Events
{
    /// <summary>
    /// 弱网状态事件。
    /// </summary>
    public struct Local_NetworkQualityChanged
    {
        /// <summary>
        /// 当前 RTT，单位毫秒。
        /// </summary>
        public int RttMs;

        /// <summary>
        /// 当前是否处于弱网告警态。
        /// </summary>
        public bool IsWeakNetWarn;

        /// <summary>
        /// 当前是否处于弱网阻断态。
        /// </summary>
        public bool IsWeakNetBlock;
    }

    /// <summary>
    /// 连接挂起事件。
    /// </summary>
    public struct Local_ConnectionSuspended
    {
        /// <summary>
        /// 剩余自动重连时间。
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
        /// 提示文本。
        /// </summary>
        public string Message;
    }

    /// <summary>
    /// 进入房间事件。
    /// </summary>
    public struct Local_RoomEntered
    {
        /// <summary>
        /// 新创建的房间实例。
        /// </summary>
        public ClientRoom Room;
    }

    /// <summary>
    /// 离开房间事件。
    /// </summary>
    public struct Local_RoomLeft
    {
        /// <summary>
        /// 当前离房是否由断线挂起触发。
        /// </summary>
        public bool IsSuspended;

        /// <summary>
        /// 当前离房是否静默执行。
        /// </summary>
        public bool IsSilent;
    }

    /// <summary>
    /// 回放倍速变化事件。
    /// </summary>
    public struct Local_ReplayTimeScaleChanged
    {
        /// <summary>
        /// 当前回放倍速。
        /// </summary>
        public float TimeScale;
    }

    /// <summary>
    /// 回放下载进度事件。
    /// </summary>
    public struct Local_ReplayDownloadProgress
    {
        /// <summary>
        /// 当前录像 Id。
        /// </summary>
        public string ReplayId;

        /// <summary>
        /// 当前已下载字节数。
        /// </summary>
        public int DownloadedBytes;

        /// <summary>
        /// 当前总字节数。
        /// </summary>
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
        /// <summary>
        /// 当前 RTT，单位毫秒。
        /// </summary>
        public float RttMs;
    }
}
