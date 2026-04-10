using StellarNet.Lite.Client.Components;
using UnityEngine;

namespace StellarNet.Lite.Client.Components.Views
{
    /// <summary>
    /// 网络实体身份组件。
    /// </summary>
    [DisallowMultipleComponent]
    public class NetIdentity : MonoBehaviour
    {
        /// <summary>
        /// 当前实体的网络 Id。
        /// </summary>
        public int NetId { get; private set; }

        /// <summary>
        /// 当前实体所属的对象同步组件。
        /// </summary>
        public ClientObjectSyncComponent SyncService { get; private set; }

        /// <summary>
        /// 初始化实体身份数据。
        /// </summary>
        public void Init(int netId, ClientObjectSyncComponent syncService)
        {
            NetId = netId;
            SyncService = syncService;
        }
    }
}
