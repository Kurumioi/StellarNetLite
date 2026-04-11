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
        /// 第一个动画浮点参数。
        /// </summary>
        public float FloatParam1;

        /// <summary>
        /// 第二个动画浮点参数。
        /// </summary>
        public float FloatParam2;

        /// <summary>
        /// 第三个动画浮点参数。
        /// </summary>
        public float FloatParam3;

        /// <summary>
        /// 当前服务端时间戳。
        /// </summary>
        public float ServerTime;

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

            if ((Mask & (byte)EntitySyncMask.Transform) != 0)
            {
                writer.Write(PosX);
                writer.Write(PosY);
                writer.Write(PosZ);

                writer.Write(RotX);
                writer.Write(RotY);
                writer.Write(RotZ);

                writer.Write(VelX);
                writer.Write(VelY);
                writer.Write(VelZ);

                writer.Write(ScaleX);
                writer.Write(ScaleY);
                writer.Write(ScaleZ);
            }

            if ((Mask & (byte)EntitySyncMask.Animator) != 0)
            {
                writer.Write(AnimStateHash);
                writer.Write(AnimNormalizedTime);
                writer.Write(FloatParam1);
                writer.Write(FloatParam2);
                writer.Write(FloatParam3);
            }

            writer.Write(ServerTime);
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

            if ((Mask & (byte)EntitySyncMask.Transform) != 0)
            {
                PosX = reader.ReadSingle();
                PosY = reader.ReadSingle();
                PosZ = reader.ReadSingle();

                RotX = reader.ReadSingle();
                RotY = reader.ReadSingle();
                RotZ = reader.ReadSingle();

                VelX = reader.ReadSingle();
                VelY = reader.ReadSingle();
                VelZ = reader.ReadSingle();

                ScaleX = reader.ReadSingle();
                ScaleY = reader.ReadSingle();
                ScaleZ = reader.ReadSingle();
            }

            if ((Mask & (byte)EntitySyncMask.Animator) != 0)
            {
                AnimStateHash = reader.ReadInt32();
                AnimNormalizedTime = reader.ReadSingle();
                FloatParam1 = reader.ReadSingle();
                FloatParam2 = reader.ReadSingle();
                FloatParam3 = reader.ReadSingle();
            }

            ServerTime = reader.ReadSingle();
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
        /// 第一个动画浮点参数。
        /// </summary>
        public float FloatParam1 => State.FloatParam1;

        /// <summary>
        /// 第二个动画浮点参数。
        /// </summary>
        public float FloatParam2 => State.FloatParam2;

        /// <summary>
        /// 第三个动画浮点参数。
        /// </summary>
        public float FloatParam3 => State.FloatParam3;

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
