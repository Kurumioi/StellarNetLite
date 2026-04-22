namespace StellarNet.Lite.Client.Core.Events
{
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
}
