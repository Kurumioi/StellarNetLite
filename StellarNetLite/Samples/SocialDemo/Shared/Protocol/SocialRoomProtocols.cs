using System.IO;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Game.Shared.Protocol
{
    /// <summary>
    /// 社交房间移动请求。
    /// </summary>
    [NetMsg(1301, NetScope.Room, NetDir.C2S)]
    public sealed class C2S_SocialMoveReq : ILiteNetSerializable
    {
        public float PosX;
        public float PosY;
        public float PosZ;
        public float VelX;
        public float VelY;
        public float VelZ;
        public float RotY;

        public void Serialize(BinaryWriter writer)
        {
            if (writer == null)
            {
                NetLogger.LogError("C2S_SocialMoveReq", "序列化失败: writer 为空");
                return;
            }

            writer.Write(PosX);
            writer.Write(PosY);
            writer.Write(PosZ);
            writer.Write(VelX);
            writer.Write(VelY);
            writer.Write(VelZ);
            writer.Write(RotY);
        }

        public void Deserialize(BinaryReader reader)
        {
            if (reader == null)
            {
                NetLogger.LogError("C2S_SocialMoveReq", "反序列化失败: reader 为空");
                return;
            }

            PosX = reader.ReadSingle();
            PosY = reader.ReadSingle();
            PosZ = reader.ReadSingle();
            VelX = reader.ReadSingle();
            VelY = reader.ReadSingle();
            VelZ = reader.ReadSingle();
            RotY = reader.ReadSingle();
        }
    }

    /// <summary>
    /// 社交房间动作请求。
    /// </summary>
    [NetMsg(1302, NetScope.Room, NetDir.C2S)]
    public sealed class C2S_SocialActionReq : ILiteNetSerializable
    {
        public int ActionId;

        public void Serialize(BinaryWriter writer)
        {
            if (writer == null)
            {
                NetLogger.LogError("C2S_SocialActionReq", "序列化失败: writer 为空");
                return;
            }

            writer.Write(ActionId);
        }

        public void Deserialize(BinaryReader reader)
        {
            if (reader == null)
            {
                NetLogger.LogError("C2S_SocialActionReq", "反序列化失败: reader 为空");
                return;
            }

            ActionId = reader.ReadInt32();
        }
    }

    /// <summary>
    /// 社交房间气泡请求。
    /// </summary>
    [NetMsg(1303, NetScope.Room, NetDir.C2S)]
    public sealed class C2S_SocialBubbleReq : ILiteNetSerializable
    {
        public string Content;

        public void Serialize(BinaryWriter writer)
        {
            if (writer == null)
            {
                NetLogger.LogError("C2S_SocialBubbleReq", "序列化失败: writer 为空");
                return;
            }

            writer.Write(Content ?? string.Empty);
        }

        public void Deserialize(BinaryReader reader)
        {
            if (reader == null)
            {
                NetLogger.LogError("C2S_SocialBubbleReq", "反序列化失败: reader 为空");
                return;
            }

            Content = reader.ReadString();
        }
    }

    /// <summary>
    /// 社交房间气泡同步。
    /// </summary>
    [NetMsg(1304, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_SocialBubbleSync : ILiteNetSerializable
    {
        public int NetId;
        public string Content;

        public void Serialize(BinaryWriter writer)
        {
            if (writer == null)
            {
                NetLogger.LogError("S2C_SocialBubbleSync", "序列化失败: writer 为空");
                return;
            }

            writer.Write(NetId);
            writer.Write(Content ?? string.Empty);
        }

        public void Deserialize(BinaryReader reader)
        {
            if (reader == null)
            {
                NetLogger.LogError("S2C_SocialBubbleSync", "反序列化失败: reader 为空");
                return;
            }

            NetId = reader.ReadInt32();
            Content = reader.ReadString();
        }
    }

    /// <summary>
    /// 社交房间即时状态同步。
    /// 客户端真实移动/动作结果被服务端接受后，立刻转发给房间内客户端，
    /// 让远端无需等待周期 ObjectSync 广播即可开始前向预测。
    /// </summary>
    [NetMsg(1305, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_SocialStateSync : ILiteNetSerializable
    {
        public float ServerTime;
        public ObjectSyncState State;

        public void Serialize(BinaryWriter writer)
        {
            if (writer == null)
            {
                NetLogger.LogError("S2C_SocialStateSync", "序列化失败: writer 为空");
                return;
            }

            writer.Write(ServerTime);
            State.Serialize(writer);
        }

        public void Deserialize(BinaryReader reader)
        {
            if (reader == null)
            {
                NetLogger.LogError("S2C_SocialStateSync", "反序列化失败: reader 为空");
                return;
            }

            ServerTime = reader.ReadSingle();
            State.Deserialize(reader);
        }
    }
}
