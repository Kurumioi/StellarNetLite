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
    /// 本地 Ping 结果事件。
    /// </summary>
    public struct Local_PingResult
    {
        public float RttMs;
    }
}
