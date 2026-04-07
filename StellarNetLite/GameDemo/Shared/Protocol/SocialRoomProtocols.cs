using System.IO;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Game.Shared.Protocol
{
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
}