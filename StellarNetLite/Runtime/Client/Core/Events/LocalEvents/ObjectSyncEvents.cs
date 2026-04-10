using StellarNet.Lite.Shared.ObjectSync;

namespace StellarNet.Lite.Client.Core.Events
{
    /// <summary>
    /// 本地网络实体生成事件。
    /// </summary>
    public struct Local_ObjectSpawned
    {
        /// <summary>
        /// 当前对象完整生成态。
        /// </summary>
        public ObjectSpawnState State;
    }

    /// <summary>
    /// 本地网络实体销毁事件。
    /// </summary>
    public struct Local_ObjectDestroyed
    {
        /// <summary>
        /// 要销毁的实体 NetId。
        /// </summary>
        public int NetId;
    }
}
