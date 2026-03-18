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

        // 核心修复：静默离开标记。用于回放沙盒重置等内部操作，告知 Router 不要进行 UI 跳转
        public bool IsSilent;
    }

    public struct Local_ReplayTimeScaleChanged
    {
        public float TimeScale;
    }
}