using System;
using System.IO;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.ObjectSync;

namespace StellarNet.Lite.Shared.Protocol
{
    [Flags]
    public enum EntitySyncMask : byte
    {
        None = 0,
        Transform = 1 << 0,
        Animator = 1 << 1,
        All = Transform | Animator
    }

    /// <summary>
    /// 对象运行时增量同步结构。
    /// 我保留这份结构作为高频同步载体，因为在线运行阶段更关注局部状态刷新，
    /// 不需要重复携带 PrefabHash、OwnerSessionId 这类仅在完整恢复时才需要的字段。
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

        public void Serialize(BinaryWriter writer)
        {
            if (writer == null)
            {
                NetLogger.LogError("ObjectSyncState", "序列化失败: writer 为空");
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

        public void Deserialize(BinaryReader reader)
        {
            if (reader == null)
            {
                NetLogger.LogError("ObjectSyncState", "反序列化失败: reader 为空");
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
    /// 我让在线 Spawn 事件直接持有共享完整结构，是为了让在线创建、断线重连快照、回放关键帧恢复共同依赖同一份字段事实源。
    /// </summary>
    [NetMsg(1100, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_ObjectSpawn : ILiteNetSerializable
    {
        public ObjectSpawnState State = ObjectSpawnState.CreateDefault();

        public int NetId => State.NetId;
        public int PrefabHash => State.PrefabHash;
        public byte Mask => State.Mask;

        public float PosX => State.PosX;
        public float PosY => State.PosY;
        public float PosZ => State.PosZ;

        public float RotX => State.RotX;
        public float RotY => State.RotY;
        public float RotZ => State.RotZ;

        public float DirX => State.DirX;
        public float DirY => State.DirY;
        public float DirZ => State.DirZ;

        public float ScaleX => State.ScaleX;
        public float ScaleY => State.ScaleY;
        public float ScaleZ => State.ScaleZ;

        public int AnimStateHash => State.AnimStateHash;
        public float AnimNormalizedTime => State.AnimNormalizedTime;
        public float FloatParam1 => State.FloatParam1;
        public float FloatParam2 => State.FloatParam2;
        public float FloatParam3 => State.FloatParam3;

        public string OwnerSessionId => State.OwnerSessionId;

        public void Serialize(BinaryWriter writer)
        {
            if (writer == null)
            {
                NetLogger.LogError("S2C_ObjectSpawn", "序列化失败: writer 为空");
                return;
            }

            State.Serialize(writer);
        }

        public void Deserialize(BinaryReader reader)
        {
            if (reader == null)
            {
                NetLogger.LogError("S2C_ObjectSpawn", "反序列化失败: reader 为空");
                return;
            }

            State.Deserialize(reader);
        }
    }

    [NetMsg(1101, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_ObjectDestroy : ILiteNetSerializable
    {
        public int NetId;

        public void Serialize(BinaryWriter writer)
        {
            if (writer == null)
            {
                NetLogger.LogError("S2C_ObjectDestroy", "序列化失败: writer 为空");
                return;
            }

            writer.Write(NetId);
        }

        public void Deserialize(BinaryReader reader)
        {
            if (reader == null)
            {
                NetLogger.LogError("S2C_ObjectDestroy", "反序列化失败: reader 为空");
                return;
            }

            NetId = reader.ReadInt32();
        }
    }

    [NetMsg(1102, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_ObjectSync : ILiteNetSerializable
    {
        public int ValidCount;
        public ObjectSyncState[] States;

        public void Serialize(BinaryWriter writer)
        {
            if (writer == null)
            {
                NetLogger.LogError("S2C_ObjectSync", "序列化失败: writer 为空");
                return;
            }

            writer.Write(ValidCount);

            if (States == null)
            {
                NetLogger.LogError($"S2C_ObjectSync", $"序列化失败: States 为空, ValidCount:{ValidCount}");
                return;
            }

            if (ValidCount < 0 || ValidCount > States.Length)
            {
                NetLogger.LogError($"S2C_ObjectSync", $"序列化失败: ValidCount 非法, ValidCount:{ValidCount}, StatesLength:{States.Length}");
                return;
            }

            for (int i = 0; i < ValidCount; i++)
            {
                States[i].Serialize(writer);
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            if (reader == null)
            {
                NetLogger.LogError("S2C_ObjectSync", "反序列化失败: reader 为空");
                return;
            }

            ValidCount = reader.ReadInt32();
            if (ValidCount < 0)
            {
                NetLogger.LogError($"S2C_ObjectSync", $"反序列化失败: ValidCount 非法, ValidCount:{ValidCount}");
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