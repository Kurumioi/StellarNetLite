using System;
using System.IO;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

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

    [NetMsg(1100, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_ObjectSpawn : ILiteNetSerializable
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

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(NetId);
            writer.Write(PrefabHash);
            writer.Write(Mask);
            writer.Write(PosX);
            writer.Write(PosY);
            writer.Write(PosZ);
            writer.Write(RotX);
            writer.Write(RotY);
            writer.Write(RotZ);
            writer.Write(DirX);
            writer.Write(DirY);
            writer.Write(DirZ);
            writer.Write(ScaleX);
            writer.Write(ScaleY);
            writer.Write(ScaleZ);
            writer.Write(AnimStateHash);
            writer.Write(AnimNormalizedTime);
            writer.Write(FloatParam1);
            writer.Write(FloatParam2);
            writer.Write(FloatParam3);
            writer.Write(OwnerSessionId ?? string.Empty);
        }

        public void Deserialize(BinaryReader reader)
        {
            NetId = reader.ReadInt32();
            PrefabHash = reader.ReadInt32();
            Mask = reader.ReadByte();
            PosX = reader.ReadSingle();
            PosY = reader.ReadSingle();
            PosZ = reader.ReadSingle();
            RotX = reader.ReadSingle();
            RotY = reader.ReadSingle();
            RotZ = reader.ReadSingle();
            DirX = reader.ReadSingle();
            DirY = reader.ReadSingle();
            DirZ = reader.ReadSingle();
            ScaleX = reader.ReadSingle();
            ScaleY = reader.ReadSingle();
            ScaleZ = reader.ReadSingle();
            AnimStateHash = reader.ReadInt32();
            AnimNormalizedTime = reader.ReadSingle();
            FloatParam1 = reader.ReadSingle();
            FloatParam2 = reader.ReadSingle();
            FloatParam3 = reader.ReadSingle();
            OwnerSessionId = reader.ReadString();
        }
    }

    [NetMsg(1101, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_ObjectDestroy : ILiteNetSerializable
    {
        public int NetId;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(NetId);
        }

        public void Deserialize(BinaryReader reader)
        {
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
            writer.Write(ValidCount);
            for (int i = 0; i < ValidCount; i++)
            {
                States[i].Serialize(writer);
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            ValidCount = reader.ReadInt32();
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