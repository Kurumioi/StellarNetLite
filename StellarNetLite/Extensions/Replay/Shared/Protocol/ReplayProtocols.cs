using System.IO;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Shared.Protocol
{
    /// <summary>
    /// 录像摘要信息。
    /// </summary>
    public sealed class ReplayBriefInfo
    {
        public string ReplayId;
        public string DisplayName;
        public long Timestamp;
        public int TotalTicks;
    }

    /// <summary>
    /// 获取录像列表请求。
    /// </summary>
    [NetMsg(600, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_GetReplayList
    {
    }

    /// <summary>
    /// 录像列表响应。
    /// </summary>
    [NetMsg(601, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_ReplayList
    {
        public ReplayBriefInfo[] Replays;
    }

    /// <summary>
    /// 下载录像请求。
    /// </summary>
    [NetMsg(602, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_DownloadReplay
    {
        public string ReplayId;
        public int StartOffset;
    }

    /// <summary>
    /// 下载录像结果。
    /// </summary>
    [NetMsg(603, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_DownloadReplayResult
    {
        public bool Success;
        public string ReplayId;
        public string ReplayFileData;
        public string Reason;
    }

    /// <summary>
    /// 下载录像分块开始响应。
    /// </summary>
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

    /// <summary>
    /// 下载录像分块响应。
    /// </summary>
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
            ChunkData = len > 0 ? reader.ReadBytes(len) : new byte[0];
        }
    }

    /// <summary>
    /// 下载录像分块确认请求。
    /// </summary>
    [NetMsg(606, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_DownloadReplayChunkAck
    {
        public string ReplayId;
    }

    /// <summary>
    /// 重命名录像请求。
    /// </summary>
    [NetMsg(607, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_RenameReplay
    {
        public string ReplayId;
        public string NewName;
    }
}
