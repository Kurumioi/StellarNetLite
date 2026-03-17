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

    // 核心新增：全局房间生命周期事件，用于彻底替代 View 层的 Update 轮询
    public struct Local_RoomEntered
    {
        public ClientRoom Room;
    }

    public struct Local_RoomLeft
    {
        public bool IsSuspended; // 标识是否是因为断线挂起而离开，供 View 层决定是否保留定格画面
    }

    // 核心新增：回放时间轴缩放事件，用于驱动表现层动画与插值速度
    public struct Local_ReplayTimeScaleChanged
    {
        public float TimeScale;
    }
}