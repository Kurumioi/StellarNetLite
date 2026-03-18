using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    /// <summary>
    /// 空间与动画全量同步状态快照。
    /// 架构设计：引入绝对旋转与固定槽位的浮点参数矩阵，彻底解决 BlendTree 无法同步的问题。
    /// </summary>
    public struct ObjectSyncState
    {
        public int NetId;
        public float PosX;
        public float PosY;
        public float PosZ;
        public float RotX;
        public float RotY;
        public float RotZ;
        public float VelX;
        public float VelY;
        public float VelZ;
        public float ScaleX;
        public float ScaleY;
        public float ScaleZ;
        public int AnimStateHash;
        public float AnimNormalizedTime;
        public float FloatParam1;
        public float FloatParam2;
        public float FloatParam3;
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
        public float RotX;
        public float RotY;
        public float RotZ;
        public float DirX;
        public float DirY;
        public float DirZ;
        public float ScaleX;
        public float ScaleY;
        public float ScaleZ;
        public int AnimStateHash;
        public float AnimNormalizedTime;
        public float FloatParam1;
        public float FloatParam2;
        public float FloatParam3;
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
        public int ValidCount;
        public ObjectSyncState[] States;
    }
}