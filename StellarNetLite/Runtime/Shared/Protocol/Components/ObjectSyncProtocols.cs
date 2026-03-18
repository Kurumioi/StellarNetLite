using System;
using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    /// <summary>
    /// 实体同步能力掩码。
    /// 架构意图：通过位掩码解耦大一统的同步结构，实现服务端按需打包与客户端按需渲染。
    /// </summary>
    [Flags]
    public enum EntitySyncMask : byte
    {
        None = 0,
        Transform = 1 << 0,
        Animator = 1 << 1,
        All = Transform | Animator
    }

    /// <summary>
    /// 空间与动画全量同步状态快照。
    /// 架构设计：引入 Mask 掩码，序列化时可根据 Mask 忽略无效字段，节省带宽。
    /// </summary>
    public struct ObjectSyncState
    {
        public int NetId;
        public byte Mask;

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
        public byte Mask;

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