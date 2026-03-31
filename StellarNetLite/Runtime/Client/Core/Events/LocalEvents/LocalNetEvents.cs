using StellarNet.Lite.Client.Core;

namespace StellarNet.Lite.Client.Core.Events
{
    /// <summary>
    /// 弱网状态事件。
    /// 由网络监控器广播给全局 UI 和输入层。
    /// </summary>
    public struct Local_NetworkQualityChanged
    {
        public int RttMs;
        public bool IsWeakNetWarn;
        public bool IsWeakNetBlock;
    }

    /// <summary>
    /// 连接挂起事件。
    /// 表示当前正在自动重连窗口内。
    /// </summary>
    public struct Local_ConnectionSuspended
    {
        public float RemainingSeconds;
    }

    /// <summary>
    /// 自动重连超时事件。
    /// 用于弹出玩家继续/退出决策。
    /// </summary>
    public struct Local_ReconnectTimeout
    {
    }

    /// <summary>
    /// 轻提示事件。
    /// 用于全局提示框或 Toast。
    /// </summary>
    public struct Local_SystemPrompt
    {
        public string Message;
    }

    /// <summary>
    /// 进入房间事件。
    /// 携带新创建的房间实例。
    /// </summary>
    public struct Local_RoomEntered
    {
        public ClientRoom Room;
    }

    /// <summary>
    /// 离开房间事件。
    /// 用于全局 UI 决定是否回退大厅。
    /// </summary>
    public struct Local_RoomLeft
    {
        public bool IsSuspended;
        public bool IsSilent;
    }

    /// <summary>
    /// 回放倍速变化事件。
    /// 供对象同步和动画层调整播放速度。
    /// </summary>
    public struct Local_ReplayTimeScaleChanged
    {
        public float TimeScale;
    }

    /// <summary>
    /// 回放下载进度事件。
    /// 供大厅录像项刷新进度条。
    /// </summary>
    public struct Local_ReplayDownloadProgress
    {
        public string ReplayId;
        public int DownloadedBytes;
        public int TotalBytes;
    }

    /// <summary>
    /// 连接硬中止事件。
    /// 职责：在 ClientApp 彻底销毁或执行硬清理时抛出，用于通知全局模块（如文件下载）释放底层非托管资源。
    /// </summary>
    public struct Local_ConnectionAborted
    {
    }
}
