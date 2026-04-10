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
        /// <summary>
        /// 录像唯一 Id。
        /// </summary>
        public string ReplayId;

        /// <summary>
        /// 录像显示名称。
        /// </summary>
        public string DisplayName;

        /// <summary>
        /// 录像时间戳。
        /// </summary>
        public long Timestamp;

        /// <summary>
        /// 录像总 Tick 数。
        /// </summary>
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
        /// <summary>
        /// 当前录像摘要列表。
        /// </summary>
        public ReplayBriefInfo[] Replays;
    }

    /// <summary>
    /// 下载录像请求。
    /// </summary>
    [NetMsg(602, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_DownloadReplay
    {
        /// <summary>
        /// 要下载的录像 Id。
        /// </summary>
        public string ReplayId;

        /// <summary>
        /// 当前续传起始偏移。
        /// </summary>
        public int StartOffset;
    }

    /// <summary>
    /// 下载录像结果。
    /// </summary>
    [NetMsg(603, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_DownloadReplayResult
    {
        /// <summary>
        /// 当前请求是否成功。
        /// </summary>
        public bool Success;

        /// <summary>
        /// 当前录像 Id。
        /// </summary>
        public string ReplayId;

        /// <summary>
        /// 录像文本数据。
        /// </summary>
        public string ReplayFileData;

        /// <summary>
        /// 当前失败原因。
        /// </summary>
        public string Reason;
    }

    /// <summary>
    /// 下载录像分块开始响应。
    /// </summary>
    [NetMsg(604, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_DownloadReplayStart : ILiteNetSerializable
    {
        /// <summary>
        /// 当前请求是否成功。
        /// </summary>
        public bool Success;

        /// <summary>
        /// 当前录像 Id。
        /// </summary>
        public string ReplayId;

        /// <summary>
        /// 当前录像总字节数。
        /// </summary>
        public int TotalBytes;

        /// <summary>
        /// 服务端接受的起始偏移。
        /// </summary>
        public int AcceptedOffset;

        /// <summary>
        /// 当前失败原因。
        /// </summary>
        public string Reason;

        /// <summary>
        /// 序列化分块开始响应。
        /// </summary>
        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Success);
            writer.Write(ReplayId ?? string.Empty);
            writer.Write(TotalBytes);
            writer.Write(AcceptedOffset);
            writer.Write(Reason ?? string.Empty);
        }

        /// <summary>
        /// 反序列化分块开始响应。
        /// </summary>
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
        /// <summary>
        /// 当前录像 Id。
        /// </summary>
        public string ReplayId;

        /// <summary>
        /// 当前分块数据。
        /// </summary>
        public byte[] ChunkData;

        /// <summary>
        /// 序列化下载分块。
        /// </summary>
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

        /// <summary>
        /// 反序列化下载分块。
        /// </summary>
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

    /// <summary>
    /// 下载录像分块确认请求。
    /// </summary>
    [NetMsg(606, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_DownloadReplayChunkAck
    {
        /// <summary>
        /// 当前录像 Id。
        /// </summary>
        public string ReplayId;
    }

    /// <summary>
    /// 重命名录像请求。
    /// </summary>
    [NetMsg(607, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_RenameReplay
    {
        /// <summary>
        /// 当前录像 Id。
        /// </summary>
        public string ReplayId;

        /// <summary>
        /// 新录像名称。
        /// </summary>
        public string NewName;
    }
}
