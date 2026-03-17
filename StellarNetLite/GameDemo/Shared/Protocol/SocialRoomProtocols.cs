using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Game.Shared.Protocol
{
    // ==========================================
    // 客户端 -> 服务端 (C2S)
    // ==========================================

    // 玩家摇杆/键盘输入请求 (方向归一化向量)
    [NetMsg(1301, NetScope.Room, NetDir.C2S)]
    public sealed class C2S_SocialMoveReq
    {
        public float DirX;
        public float DirZ;
    }

    // 玩家请求播放社交动作 (如挥手、跳舞)
    [NetMsg(1302, NetScope.Room, NetDir.C2S)]
    public sealed class C2S_SocialActionReq
    {
        // 1: 挥手 (Wave), 2: 跳舞 (Dance)
        public int ActionId; 
    }

    // 玩家发送头顶聊天气泡
    [NetMsg(1303, NetScope.Room, NetDir.C2S)]
    public sealed class C2S_SocialBubbleReq
    {
        public string Content;
    }

    // ==========================================
    // 服务端 -> 客户端 (S2C)
    // ==========================================

    // 服务端广播聊天气泡 (表现层事件，不影响物理同步)
    [NetMsg(1304, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_SocialBubbleSync
    {
        public int NetId;
        public string Content;
    }
}