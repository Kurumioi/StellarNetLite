using System.IO;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Game.Shared.Protocol
{
    /// <summary>
    /// 社交房业务协议。
    /// 负责移动、动作和头顶气泡消息。
    /// </summary>
    // ==========================================
    // 客户端 -> 服务端 (C2S)
    // ==========================================

    // 玩家摇杆/键盘输入请求 (方向归一化向量)
    // 核心修复：实现 ILiteNetSerializable，彻底绕过 JSON 序列化，消除高频发包带来的 GC 与卡顿
    [NetMsg(1301, NetScope.Room, NetDir.C2S)]
    public sealed class C2S_SocialMoveReq : ILiteNetSerializable
    {
        public float DirX;
        public float DirZ;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(DirX);
            writer.Write(DirZ);
        }

        public void Deserialize(BinaryReader reader)
        {
            DirX = reader.ReadSingle();
            DirZ = reader.ReadSingle();
        }
    }

    // 玩家请求播放社交动作 (如挥手、跳舞)
    [NetMsg(1302, NetScope.Room, NetDir.C2S)]
    public sealed class C2S_SocialActionReq : ILiteNetSerializable
    {
        // 1: 挥手 (Wave), 2: 跳舞 (Dance)
        public int ActionId;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(ActionId);
        }

        public void Deserialize(BinaryReader reader)
        {
            ActionId = reader.ReadInt32();
        }
    }

    // 玩家发送头顶聊天气泡
    [NetMsg(1303, NetScope.Room, NetDir.C2S)]
    public sealed class C2S_SocialBubbleReq : ILiteNetSerializable
    {
        public string Content;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Content ?? string.Empty);
        }

        public void Deserialize(BinaryReader reader)
        {
            Content = reader.ReadString();
        }
    }

    // ==========================================
    // 服务端 -> 客户端 (S2C)
    // ==========================================

    // 服务端广播聊天气泡 (表现层事件，不影响物理同步)
    [NetMsg(1304, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_SocialBubbleSync : ILiteNetSerializable
    {
        public int NetId;
        public string Content;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(NetId);
            writer.Write(Content ?? string.Empty);
        }

        public void Deserialize(BinaryReader reader)
        {
            NetId = reader.ReadInt32();
            Content = reader.ReadString();
        }
    }
}
