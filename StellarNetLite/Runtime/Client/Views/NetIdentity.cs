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
        // 运行时网络实体 Id。
        public int NetId { get; private set; }
        // 所属的对象同步服务。
        public ClientObjectSyncComponent SyncService { get; private set; }

        // 由生成器在实例化后注入实体身份。
        public void Init(int netId, ClientObjectSyncComponent syncService)
        {
            NetId = netId;
            SyncService = syncService;
        }
    }
}
