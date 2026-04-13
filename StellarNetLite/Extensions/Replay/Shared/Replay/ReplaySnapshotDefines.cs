using System;
using System.IO;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Shared.Replay
{
    /// <summary>
    /// 录像帧类型。
    /// </summary>
    public enum ReplayFrameKind : byte
    {
        None = 0,
        Message = 1,
        ObjectSnapshot = 2
    }

    /// <summary>
    /// 录像格式常量定义。
    /// </summary>
    public static class ReplayFormatDefines
    {
        /// <summary>
        /// 录像文件魔数。
        /// </summary>
        public const uint MagicBytes = 0x50455253;

        /// <summary>
        /// 旧版录像格式版本号。
        /// </summary>
        public const byte VersionLegacy = 2;

        /// <summary>
        /// 支持组件快照的录像格式版本号。
        /// </summary>
        public const byte VersionWithObjectSnapshot = 3;

        /// <summary>
        /// 旧格式缺少总 Tick 时使用的默认回退值。
        /// </summary>
        public const int DefaultTotalTicksFallback = 108000;
    }

    /// <summary>
    /// 服务端快照提供者接口。
    /// </summary>
    public interface IReplaySnapshotProvider
    {
        /// <summary>
        /// 当前组件导出的快照组件 Id。
        /// </summary>
        int SnapshotComponentId { get; }

        /// <summary>
        /// 导出当前组件快照。
        /// </summary>
        byte[] ExportSnapshot();
    }

    /// <summary>
    /// 客户端快照消费者接口。
    /// </summary>
    public interface IReplaySnapshotConsumer
    {
        /// <summary>
        /// 当前组件负责消费的快照组件 Id。
        /// </summary>
        int SnapshotComponentId { get; }

        /// <summary>
        /// 应用来自录像流的组件快照。
        /// </summary>
        void ApplySnapshot(byte[] payload);
    }

    /// <summary>
    /// 单个组件的快照数据块。
    /// </summary>
    public struct ComponentSnapshotData
    {
        /// <summary>
        /// 快照所属组件 Id。
        /// </summary>
        public int ComponentId;

        /// <summary>
        /// 组件快照原始负载。
        /// </summary>
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

    /// <summary>
    /// 通用录像快照帧。
    /// 支持同一 Tick 内记录多个组件的快照负载。
    /// </summary>
    [Serializable]
    public sealed class ReplaySnapshotFrame : ILiteNetSerializable
    {
        /// <summary>
        /// 当前快照相对开局的 Tick。
        /// </summary>
        public int Tick;

        /// <summary>
        /// 当前 Tick 下所有组件快照。
        /// </summary>
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
