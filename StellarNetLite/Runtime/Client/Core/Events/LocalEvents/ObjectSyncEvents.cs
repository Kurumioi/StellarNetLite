namespace StellarNet.Lite.Client.Core.Events
{
    /// <summary>
    /// 本地网络实体生成事件。
    /// 职责：由 ClientObjectSyncComponent 接收到 S2C_ObjectSpawn 后抛出，驱动 View 层实例化具体的 GameObject。
    /// </summary>
    public struct Local_ObjectSpawned
    {
        public int NetId;
        public int PrefabHash;

        public float PosX;
        public float PosY;
        public float PosZ;

        public float DirX;
        public float DirY;
        public float DirZ;

        public float ScaleX;
        public float ScaleY;
        public float ScaleZ;

        public string OwnerSessionId;
    }

    /// <summary>
    /// 本地网络实体销毁事件。
    /// 职责：由 ClientObjectSyncComponent 接收到 S2C_ObjectDestroy 后抛出，驱动 View 层播放死亡表现并销毁 GameObject。
    /// </summary>
    public struct Local_ObjectDestroyed
    {
        public int NetId;
    }
}