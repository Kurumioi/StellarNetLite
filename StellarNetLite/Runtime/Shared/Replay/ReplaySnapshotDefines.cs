using System;
using System.IO;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Shared.Replay
{
    public enum ReplayFrameKind : byte
    {
        None = 0,
        Message = 1,
        ObjectSnapshot = 2 // 现已升级为通用组件快照帧
    }

    public static class ReplayFormatDefines
    {
        public const uint MagicBytes = 0x50455253;
        public const byte VersionLegacy = 2;
        public const byte VersionWithObjectSnapshot = 3;
        public const int DefaultTotalTicksFallback = 108000;
    }

    // 核心解耦：服务端快照提供者接口
    public interface IReplaySnapshotProvider
    {
        int SnapshotComponentId { get; }
        byte[] ExportSnapshot();
    }

    // 核心解耦：客户端快照消费者接口
    public interface IReplaySnapshotConsumer
    {
        int SnapshotComponentId { get; }
        void ApplySnapshot(byte[] payload);
    }

    // 通用组件快照数据块
    public struct ComponentSnapshotData
    {
        public int ComponentId;
        public byte[] Payload;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(ComponentId);
            if (Payload == null || Payload.Length == 0)
            {
                writer.Write(0);
            }
            else
            {
                writer.Write(Payload.Length);
                writer.Write(Payload);
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            ComponentId = reader.ReadInt32();
            int len = reader.ReadInt32();
            if (len > 0)
            {
                Payload = reader.ReadBytes(len);
            }
            else
            {
                Payload = Array.Empty<byte>();
            }
        }
    }

    // 升级后的通用快照帧，支持容纳多个业务组件的快照数据
    [Serializable]
    public sealed class ReplaySnapshotFrame : ILiteNetSerializable
    {
        public int Tick;
        public ComponentSnapshotData[] ComponentSnapshots = Array.Empty<ComponentSnapshotData>();

        public void Serialize(BinaryWriter writer)
        {
            if (writer == null)
            {
                NetLogger.LogError("ReplaySnapshotFrame", "序列化失败: writer 为空");
                return;
            }

            writer.Write(Tick);
            int count = ComponentSnapshots != null ? ComponentSnapshots.Length : 0;
            writer.Write(count);
            for (int i = 0; i < count; i++)
            {
                ComponentSnapshots[i].Serialize(writer);
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            if (reader == null)
            {
                NetLogger.LogError("ReplaySnapshotFrame", "反序列化失败: reader 为空");
                Tick = 0;
                ComponentSnapshots = Array.Empty<ComponentSnapshotData>();
                return;
            }

            Tick = reader.ReadInt32();
            int count = reader.ReadInt32();
            if (count < 0)
            {
                NetLogger.LogError("ReplaySnapshotFrame", $"反序列化失败: count 非法, Tick:{Tick}, Count:{count}");
                Tick = 0;
                ComponentSnapshots = Array.Empty<ComponentSnapshotData>();
                return;
            }

            if (count == 0)
            {
                ComponentSnapshots = Array.Empty<ComponentSnapshotData>();
                return;
            }

            ComponentSnapshots = new ComponentSnapshotData[count];
            for (int i = 0; i < count; i++)
            {
                ComponentSnapshots[i] = new ComponentSnapshotData();
                ComponentSnapshots[i].Deserialize(reader);
            }
        }
    }
}