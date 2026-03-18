using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    public sealed class ReplayBriefInfo
    {
        public string ReplayId;
        public string DisplayName;
        public long Timestamp;
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

    [NetMsg(604, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_DownloadReplayStart
    {
        public bool Success;
        public string ReplayId;
        public int TotalBytes;
        public int AcceptedOffset;
        public string Reason;
    }

    [NetMsg(605, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_DownloadReplayChunk
    {
        public string ReplayId;
        public byte[] ChunkData;
    }

    [NetMsg(606, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_DownloadReplayChunkAck
    {
        public string ReplayId;
    }

    // 核心新增：独立的重命名协议
    [NetMsg(607, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_RenameReplay
    {
        public string ReplayId;
        public string NewName;
    }
}