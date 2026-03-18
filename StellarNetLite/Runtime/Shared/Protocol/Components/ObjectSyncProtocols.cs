using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    public struct ObjectSyncState
    {
        public int NetId;
        public float PosX;
        public float PosY;
        public float PosZ;
        public float VelX;
        public float VelY;
        public float VelZ;
        public float ScaleX;
        public float ScaleY;
        public float ScaleZ;
        public int AnimStateHash;
        public float AnimNormalizedTime;
        public float ServerTime;
    }

    [NetMsg(1100, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_ObjectSpawn
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

    [NetMsg(1101, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_ObjectDestroy
    {
        public int NetId;
    }

    [NetMsg(1102, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_ObjectSync
    {
        // 核心修复 P1-4：引入 ValidCount。
        // 底层序列化器在处理此数组时，只需读取/写入前 ValidCount 个元素。
        public int ValidCount;
        public ObjectSyncState[] States;
    }
}