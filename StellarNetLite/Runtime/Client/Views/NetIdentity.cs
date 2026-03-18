using UnityEngine;
using StellarNet.Lite.Client.Components;

namespace StellarNet.Lite.Client.Components.Views
{
    /// <summary>
    /// 网络实体身份标识组件。
    /// 职责：挂载于预制体根节点，作为表现层与底层数据中心的桥梁。
    /// </summary>
    [DisallowMultipleComponent]
    public class NetIdentity : MonoBehaviour
    {
        public int NetId { get; private set; }
        public ClientObjectSyncComponent SyncService { get; private set; }

        public void Init(int netId, ClientObjectSyncComponent syncService)
        {
            NetId = netId;
            SyncService = syncService;
        }
    }
}