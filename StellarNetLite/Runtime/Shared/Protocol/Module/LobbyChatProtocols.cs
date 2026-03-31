using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    // 客户端发送大厅聊天消息。
    [NetMsg(400, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_SendLobbyChat
    {
        // 聊天正文。
        public string Content;
    }

    // 服务端广播大厅聊天消息。
    [NetMsg(401, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_LobbyChatMsg
    {
        // 发送者会话 Id。
        public string SenderSessionId;
        // 聊天正文。
        public string Content;
        // 发送时间的 UTC 秒时间戳。
        public long SendUnixTime;
    }
}
