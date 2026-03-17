using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    [NetMsg(600, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_GetReplayList
    {
    }

    [NetMsg(601, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_ReplayList
    {
        public string[] ReplayIds;
    }

    [NetMsg(602, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_DownloadReplay
    {
        public string ReplayId;

        // 断点续传的起始偏移量
        public int StartOffset;
    }

    // 保留原有的 Result，但改为仅在客户端内部组装完毕后抛出的本地/表现层事件
    [NetMsg(603, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_DownloadReplayResult
    {
        public bool Success;
        public string ReplayId;
        public string ReplayFileData;
        public string Reason;
    }

    // ================= 流式下载分块协议 =================

    [NetMsg(604, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_DownloadReplayStart
    {
        public bool Success;
        public string ReplayId;

        public int TotalBytes;

        // 核心新增：服务端实际接受的偏移量，用于客户端校验脏数据
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
}