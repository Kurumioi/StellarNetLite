using System;
using System.IO;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.ObjectSync;

namespace StellarNet.Lite.Shared.Protocol
{
    /// <summary>
    /// 实体同步掩码。
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
    /// 对象同步脏字段掩码。
    /// 只标记本帧真正变化的字段。
    /// </summary>
    [Flags]
    public enum ObjectSyncDirtyMask : ushort
    {
        None = 0,
        Position = 1 << 0,
        Rotation = 1 << 1,
        Velocity = 1 << 2,
        Scale = 1 << 3,
        AnimState = 1 << 4,
        AnimNormalizedTime = 1 << 5,
        AnimParams = 1 << 6,
        AllTransform = Position | Rotation | Velocity | Scale,
        AllAnimator = AnimState | AnimNormalizedTime | AnimParams
    }

    /// <summary>
    /// 对象增量同步态。
    /// </summary>
    public struct ObjectSyncState
    {
        /// <summary>
        /// 当前实体 NetId。
        /// </summary>
        public int NetId;

        /// <summary>
        /// 当前增量掩码。
        /// </summary>
        public byte Mask;

        /// <summary>
        /// 当前脏字段掩码。
        /// </summary>
        public ushort DirtyMask;

        /// <summary>
        /// 当前位置 X。
        /// </summary>
        public float PosX;

        /// <summary>
        /// 当前位置 Y。
        /// </summary>
        public float PosY;

        /// <summary>
        /// 当前位置 Z。
        /// </summary>
        public float PosZ;

        /// <summary>
        /// 当前旋转 X。
        /// </summary>
        public float RotX;

        /// <summary>
        /// 当前旋转 Y。
        /// </summary>
        public float RotY;

        /// <summary>
        /// 当前旋转 Z。
        /// </summary>
        public float RotZ;

        /// <summary>
        /// 当前速度 X。
        /// </summary>
        public float VelX;

        /// <summary>
        /// 当前速度 Y。
        /// </summary>
        public float VelY;

        /// <summary>
        /// 当前速度 Z。
        /// </summary>
        public float VelZ;

        /// <summary>
        /// 当前缩放 X。
        /// </summary>
        public float ScaleX;

        /// <summary>
        /// 当前缩放 Y。
        /// </summary>
        public float ScaleY;

        /// <summary>
        /// 当前缩放 Z。
        /// </summary>
        public float ScaleZ;

        /// <summary>
        /// 当前动画状态 Hash。
        /// </summary>
        public int AnimStateHash;

        /// <summary>
        /// 当前动画归一化时间。
        /// </summary>
        public float AnimNormalizedTime;

        /// <summary>
        /// 当前动画参数数量。
        /// </summary>
        public int AnimParamCount;

        /// <summary>
        /// 当前动画参数列表。
        /// </summary>
        public AnimatorParamValue[] AnimParams;

        /// <summary>
        /// 序列化增量同步态。
        /// </summary>
        public void Serialize(BinaryWriter writer)
        {
            if (writer == null)
            {
                NetLogger.LogError("ObjectSyncState", "序列化失败: Writer 为空");
                return;
            }

            writer.Write(NetId);
            writer.Write(Mask);
            writer.Write(DirtyMask);

            if ((DirtyMask & (ushort)ObjectSyncDirtyMask.Position) != 0)
            {
                writer.Write(PosX);
                writer.Write(PosY);
                writer.Write(PosZ);
            }

            if ((DirtyMask & (ushort)ObjectSyncDirtyMask.Rotation) != 0)
            {
                writer.Write(RotX);
                writer.Write(RotY);
                writer.Write(RotZ);
            }

            if ((DirtyMask & (ushort)ObjectSyncDirtyMask.Velocity) != 0)
            {
                writer.Write(VelX);
                writer.Write(VelY);
                writer.Write(VelZ);
            }

            if ((DirtyMask & (ushort)ObjectSyncDirtyMask.Scale) != 0)
            {
                writer.Write(ScaleX);
                writer.Write(ScaleY);
                writer.Write(ScaleZ);
            }

            if ((DirtyMask & (ushort)ObjectSyncDirtyMask.AnimState) != 0)
            {
                writer.Write(AnimStateHash);
            }

            if ((DirtyMask & (ushort)ObjectSyncDirtyMask.AnimNormalizedTime) != 0)
            {
                writer.Write(AnimNormalizedTime);
            }

            if ((DirtyMask & (ushort)ObjectSyncDirtyMask.AnimParams) != 0)
            {
                writer.Write(AnimParamCount);
                if (AnimParamCount > 0)
                {
                    if (AnimParams == null || AnimParams.Length < AnimParamCount)
                    {
                        NetLogger.LogError(
                            "ObjectSyncState",
                            $"序列化失败: AnimParams 非法, Count:{AnimParamCount}, Length:{(AnimParams == null ? 0 : AnimParams.Length)}");
                        return;
                    }

                    for (int i = 0; i < AnimParamCount; i++)
                    {
                        AnimParams[i].Serialize(writer);
                    }
                }
            }
        }

        /// <summary>
        /// 反序列化增量同步态。
        /// </summary>
        public void Deserialize(BinaryReader reader)
        {
            if (reader == null)
            {
                NetLogger.LogError("ObjectSyncState", "反序列化失败: Reader 为空");
                return;
            }

            NetId = reader.ReadInt32();
            Mask = reader.ReadByte();
            DirtyMask = reader.ReadUInt16();

            if ((DirtyMask & (ushort)ObjectSyncDirtyMask.Position) != 0)
            {
                PosX = reader.ReadSingle();
                PosY = reader.ReadSingle();
                PosZ = reader.ReadSingle();
            }

            if ((DirtyMask & (ushort)ObjectSyncDirtyMask.Rotation) != 0)
            {
                RotX = reader.ReadSingle();
                RotY = reader.ReadSingle();
                RotZ = reader.ReadSingle();
            }

            if ((DirtyMask & (ushort)ObjectSyncDirtyMask.Velocity) != 0)
            {
                VelX = reader.ReadSingle();
                VelY = reader.ReadSingle();
                VelZ = reader.ReadSingle();
            }

            if ((DirtyMask & (ushort)ObjectSyncDirtyMask.Scale) != 0)
            {
                ScaleX = reader.ReadSingle();
                ScaleY = reader.ReadSingle();
                ScaleZ = reader.ReadSingle();
            }

            if ((DirtyMask & (ushort)ObjectSyncDirtyMask.AnimState) != 0)
            {
                AnimStateHash = reader.ReadInt32();
            }

            if ((DirtyMask & (ushort)ObjectSyncDirtyMask.AnimNormalizedTime) != 0)
            {
                AnimNormalizedTime = reader.ReadSingle();
            }

            if ((DirtyMask & (ushort)ObjectSyncDirtyMask.AnimParams) != 0)
            {
                AnimParamCount = reader.ReadInt32();
                if (AnimParamCount < 0)
                {
                    NetLogger.LogError("ObjectSyncState", $"反序列化失败: AnimParamCount 非法, Count:{AnimParamCount}");
                    AnimParamCount = 0;
                    AnimParams = Array.Empty<AnimatorParamValue>();
                    return;
                }

                if (AnimParams == null || AnimParams.Length < AnimParamCount)
                {
                    AnimParams = new AnimatorParamValue[AnimParamCount];
                }

                for (int i = 0; i < AnimParamCount; i++)
                {
                    AnimParams[i].Deserialize(reader);
                }
            }
        }
    }

    /// <summary>
    /// 服务端通知客户端生成对象。
    /// </summary>
    [NetMsg(1100, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_ObjectSpawn : ILiteNetSerializable
    {
        /// <summary>
        /// 当前对象完整生成态。
        /// </summary>
        public ObjectSpawnState State = ObjectSpawnState.CreateDefault();

        /// <summary>
        /// 当前实体 NetId。
        /// </summary>
        public int NetId => State.NetId;

        /// <summary>
        /// 当前实体预制体 Hash。
        /// </summary>
        public int PrefabHash => State.PrefabHash;

        /// <summary>
        /// 当前实体同步掩码。
        /// </summary>
        public byte Mask => State.Mask;

        /// <summary>
        /// 当前位置 X。
        /// </summary>
        public float PosX => State.PosX;

        /// <summary>
        /// 当前位置 Y。
        /// </summary>
        public float PosY => State.PosY;

        /// <summary>
        /// 当前位置 Z。
        /// </summary>
        public float PosZ => State.PosZ;

        /// <summary>
        /// 当前旋转 X。
        /// </summary>
        public float RotX => State.RotX;

        /// <summary>
        /// 当前旋转 Y。
        /// </summary>
        public float RotY => State.RotY;

        /// <summary>
        /// 当前旋转 Z。
        /// </summary>
        public float RotZ => State.RotZ;

        /// <summary>
        /// 当前朝向 X。
        /// </summary>
        public float DirX => State.DirX;

        /// <summary>
        /// 当前朝向 Y。
        /// </summary>
        public float DirY => State.DirY;

        /// <summary>
        /// 当前朝向 Z。
        /// </summary>
        public float DirZ => State.DirZ;

        /// <summary>
        /// 当前缩放 X。
        /// </summary>
        public float ScaleX => State.ScaleX;

        /// <summary>
        /// 当前缩放 Y。
        /// </summary>
        public float ScaleY => State.ScaleY;

        /// <summary>
        /// 当前缩放 Z。
        /// </summary>
        public float ScaleZ => State.ScaleZ;

        /// <summary>
        /// 当前动画状态 Hash。
        /// </summary>
        public int AnimStateHash => State.AnimStateHash;

        /// <summary>
        /// 当前动画归一化时间。
        /// </summary>
        public float AnimNormalizedTime => State.AnimNormalizedTime;

        /// <summary>
        /// 当前拥有者 SessionId。
        /// </summary>
        public string OwnerSessionId => State.OwnerSessionId;

        /// <summary>
        /// 序列化生成对象消息。
        /// </summary>
        public void Serialize(BinaryWriter writer)
        {
            if (writer == null)
            {
                NetLogger.LogError("S2C_ObjectSpawn", "序列化失败: Writer 为空");
                return;
            }

            State.Serialize(writer);
        }

        /// <summary>
        /// 反序列化生成对象消息。
        /// </summary>
        public void Deserialize(BinaryReader reader)
        {
            if (reader == null)
            {
                NetLogger.LogError("S2C_ObjectSpawn", "反序列化失败: Reader 为空");
                return;
            }

            State.Deserialize(reader);
        }
    }

    /// <summary>
    /// 服务端通知客户端销毁对象。
    /// </summary>
    [NetMsg(1101, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_ObjectDestroy : ILiteNetSerializable
    {
        /// <summary>
        /// 要销毁的实体 NetId。
        /// </summary>
        public int NetId;

        /// <summary>
        /// 序列化销毁对象消息。
        /// </summary>
        public void Serialize(BinaryWriter writer)
        {
            if (writer == null)
            {
                NetLogger.LogError("S2C_ObjectDestroy", "序列化失败: Writer 为空");
                return;
            }

            writer.Write(NetId);
        }

        /// <summary>
        /// 反序列化销毁对象消息。
        /// </summary>
        public void Deserialize(BinaryReader reader)
        {
            if (reader == null)
            {
                NetLogger.LogError("S2C_ObjectDestroy", "反序列化失败: Reader 为空");
                return;
            }

            NetId = reader.ReadInt32();
        }
    }

    /// <summary>
    /// 服务端批量同步对象状态。
    /// </summary>
    [NetMsg(1102, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_ObjectSync : ILiteNetSerializable
    {
        /// <summary>
        /// 当前有效同步状态数量。
        /// </summary>
        public int ValidCount;

        /// <summary>
        /// 当前批次统一服务端时间。
        /// </summary>
        public float ServerTime;

        /// <summary>
        /// 当前同步状态数组。
        /// </summary>
        public ObjectSyncState[] States;

        /// <summary>
        /// 序列化批量同步消息。
        /// </summary>
        public void Serialize(BinaryWriter writer)
        {
            if (writer == null)
            {
                NetLogger.LogError("S2C_ObjectSync", "序列化失败: Writer 为空");
                return;
            }

            writer.Write(ValidCount);
            writer.Write(ServerTime);
            if (States == null)
            {
                NetLogger.LogError("S2C_ObjectSync", $"序列化失败: States 为空, ValidCount:{ValidCount}");
                return;
            }

            if (ValidCount < 0 || ValidCount > States.Length)
            {
                NetLogger.LogError("S2C_ObjectSync", $"序列化失败: ValidCount 非法, ValidCount:{ValidCount}, StatesLength:{States.Length}");
                return;
            }

            for (int i = 0; i < ValidCount; i++)
            {
                States[i].Serialize(writer);
            }
        }

        /// <summary>
        /// 反序列化批量同步消息。
        /// </summary>
        public void Deserialize(BinaryReader reader)
        {
            if (reader == null)
            {
                NetLogger.LogError("S2C_ObjectSync", "反序列化失败: Reader 为空");
                return;
            }

            ValidCount = reader.ReadInt32();
            ServerTime = reader.ReadSingle();
            if (ValidCount < 0)
            {
                NetLogger.LogError("S2C_ObjectSync", $"反序列化失败: ValidCount 非法, ValidCount:{ValidCount}");
                ValidCount = 0;
                States = Array.Empty<ObjectSyncState>();
                return;
            }

            if (States == null || States.Length < ValidCount)
            {
                States = new ObjectSyncState[ValidCount];
            }

            for (int i = 0; i < ValidCount; i++)
            {
                States[i].Deserialize(reader);
            }
        }
    }
}
