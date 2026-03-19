using System.IO;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Shared.Protocol
{
    public sealed class ReplayBriefInfo
    {
        public string ReplayId;
        public string DisplayName;
        public long Timestamp;
        public int TotalTicks;
    }

    [NetMsg(600, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_GetReplayList
    {
    }

    [NetMsg(601, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_ReplayList
    {
        public ReplayBriefInfo[] Replays;
    }

    [NetMsg(602, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_DownloadReplay
    {
        public string ReplayId;
        public int StartOffset;
    }

    [NetMsg(603, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_DownloadReplayResult
    {
        public bool Success;
        public string ReplayId;
        public string ReplayFileData;
        public string Reason;
    }

    // 核心修复：实现 ILiteNetSerializable，彻底绕过 JSON 序列化，实现 0GC 与极速传输
    [NetMsg(604, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_DownloadReplayStart : ILiteNetSerializable
    {
        public bool Success;
        public string ReplayId;
        public int TotalBytes;
        public int AcceptedOffset;
        public string Reason;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Success);
            writer.Write(ReplayId ?? string.Empty);
            writer.Write(TotalBytes);
            writer.Write(AcceptedOffset);
            writer.Write(Reason ?? string.Empty);
        }

        public void Deserialize(BinaryReader reader)
        {
            Success = reader.ReadBoolean();
            ReplayId = reader.ReadString();
            TotalBytes = reader.ReadInt32();
            AcceptedOffset = reader.ReadInt32();
            Reason = reader.ReadString();
        }
    }

    // 核心修复：实现 ILiteNetSerializable，防止 64KB 的二进制数据被 JSON 强转为庞大的 Base64 字符串
    [NetMsg(605, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_DownloadReplayChunk : ILiteNetSerializable
    {
        public string ReplayId;
        public byte[] ChunkData;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(ReplayId ?? string.Empty);
            if (ChunkData == null || ChunkData.Length == 0)
            {
                writer.Write(0);
            }
            else
            {
                writer.Write(ChunkData.Length);
                writer.Write(ChunkData);
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            ReplayId = reader.ReadString();
            int len = reader.ReadInt32();
            if (len > 0)
            {
                ChunkData = reader.ReadBytes(len);
            }
            else
            {
                ChunkData = new byte[0];
            }
        }
    }

    [NetMsg(606, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_DownloadReplayChunkAck
    {
        public string ReplayId;
    }

    [NetMsg(607, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_RenameReplay
    {
        public string ReplayId;
        public string NewName;
    }
}
