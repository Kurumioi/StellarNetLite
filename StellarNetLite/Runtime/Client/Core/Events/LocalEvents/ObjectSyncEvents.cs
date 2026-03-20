using StellarNet.Lite.Shared.ObjectSync;

namespace StellarNet.Lite.Client.Core.Events
{
    /// <summary>
    /// 本地网络实体生成事件。
    /// 我让本地表现层事件也直接持有共享完整结构，
    /// 是为了让 View 层与在线协议、回放恢复共同依赖同一份对象完整态定义，而不是再维护第三份扁平字段副本。
    /// </summary>
    public struct Local_ObjectSpawned
    {
        public ObjectSpawnState State;
    }

    /// <summary>
    /// 本地网络实体销毁事件。
    /// 我保留最小字段，是因为销毁是明确事件，不存在需要共享完整结构的问题。
    /// </summary>
    public struct Local_ObjectDestroyed
    {
        public int NetId;
    }
}