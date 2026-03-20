using System;
using System.IO;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.ObjectSync;

namespace StellarNet.Lite.Shared.Replay
{
    /// <summary>
    /// 回放 Raw 帧类型。
    /// 我把录像内部数据拆成普通消息帧和对象关键帧两种，
    /// 是为了让 Seek 时既能继续复用已有消息补播链路，又能在任意中段先恢复一份完整对象世界。
    /// </summary>
    public enum ReplayFrameKind : byte
    {
        None = 0,
        Message = 1,
        ObjectSnapshot = 2
    }

    /// <summary>
    /// 回放文件格式常量定义。
    /// 我通过版本号显式区分是否支持对象关键帧，
    /// 是为了避免老录像按新格式读取时发生帧头错位，导致回放器把整个 Raw 数据流解析崩掉。
    /// </summary>
    public static class ReplayFormatDefines
    {
        public const uint MagicBytes = 0x50455253;
        public const byte VersionLegacy = 2;
        public const byte VersionWithObjectSnapshot = 3;
        public const int DefaultTotalTicksFallback = 108000;
    }

    /// <summary>
    /// 回放对象关键帧。
    /// 我只在这里保存某个 Tick 的完整对象生成态集合，
    /// 因为关键帧的职责是提供世界恢复基线，不应该和在线消息语义混在同一套协议事件里。
    /// </summary>
    [Serializable]
    public sealed class ReplayObjectSnapshotFrame : ILiteNetSerializable
    {
        public int Tick;
        public ObjectSpawnState[] States = Array.Empty<ObjectSpawnState>();

        /// <summary>
        /// 我显式写入 Tick 和数组长度，是为了让录像层可以在不依赖运行时反射的前提下稳定恢复对象完整态。
        /// </summary>
        public void Serialize(BinaryWriter writer)
        {
            if (writer == null)
            {
                NetLogger.LogError("ReplayObjectSnapshotFrame", "序列化失败: writer 为空");
                return;
            }

            writer.Write(Tick);

            int count = States != null ? States.Length : 0;
            writer.Write(count);

            for (int i = 0; i < count; i++)
            {
                States[i].Serialize(writer);
            }
        }

        /// <summary>
        /// 我按共享结构顺序恢复关键帧内容，
        /// 这样后续对象完整态扩展字段时，回放层不需要再维护另一套重复实体定义。
        /// </summary>
        public void Deserialize(BinaryReader reader)
        {
            if (reader == null)
            {
                NetLogger.LogError("ReplayObjectSnapshotFrame", "反序列化失败: reader 为空");
                Tick = 0;
                States = Array.Empty<ObjectSpawnState>();
                return;
            }

            Tick = reader.ReadInt32();

            int count = reader.ReadInt32();
            if (count < 0)
            {
                NetLogger.LogError("ReplayObjectSnapshotFrame", $"反序列化失败: count 非法, Tick:{Tick}, Count:{count}");
                Tick = 0;
                States = Array.Empty<ObjectSpawnState>();
                return;
            }

            if (count == 0)
            {
                States = Array.Empty<ObjectSpawnState>();
                return;
            }

            States = new ObjectSpawnState[count];
            for (int i = 0; i < count; i++)
            {
                States[i].Deserialize(reader);
            }
        }
    }
}