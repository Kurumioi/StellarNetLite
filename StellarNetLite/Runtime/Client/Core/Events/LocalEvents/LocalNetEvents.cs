using StellarNet.Lite.Client.Core;

namespace StellarNet.Lite.Client.Core.Events
{
    public struct Local_NetworkQualityChanged
    {
        public int RttMs;
        public bool IsWeakNetWarn;
        public bool IsWeakNetBlock;
    }

    public struct Local_ConnectionSuspended
    {
        public float RemainingSeconds;
    }

    public struct Local_ReconnectTimeout
    {
    }

    public struct Local_SystemPrompt
    {
        public string Message;
    }

    public struct Local_RoomEntered
    {
        public ClientRoom Room;
    }

    public struct Local_RoomLeft
    {
        public bool IsSuspended;
        public bool IsSilent;
    }

    public struct Local_ReplayTimeScaleChanged
    {
        public float TimeScale;
    }

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