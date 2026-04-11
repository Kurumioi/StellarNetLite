using System.IO;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Game.Shared.Protocol
{
    [NetMsg(1301, NetScope.Room, NetDir.C2S)]
    /// <summary>
    /// 社交房间移动请求。
    /// </summary>
    public sealed class C2S_SocialMoveReq : ILiteNetSerializable
    {
        /// <summary>
        /// 当前坐标 X。
        /// </summary>
        public float PosX;
        /// <summary>
        /// 当前坐标 Y。
        /// </summary>
        public float PosY;
        /// <summary>
        /// 当前坐标 Z。
        /// </summary>
        public float PosZ;
        /// <summary>
        /// 当前速度 X。
        /// </summary>
        public float VelX;
        /// <summary>
        /// 当前速度 Y。
        /// </summary>
        public float VelY;
        /// <summary>
        /// 当前速度 Z。
        /// </summary>
        public float VelZ;
        /// <summary>
        /// 当前朝向 Y。
        /// </summary>
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
    /// <summary>
    /// 社交房间动作请求。
    /// </summary>
    public sealed class C2S_SocialActionReq : ILiteNetSerializable
    {
        /// <summary>
        /// 动作 Id。
        /// </summary>
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
    /// <summary>
    /// 社交房间气泡请求。
    /// </summary>
    public sealed class C2S_SocialBubbleReq : ILiteNetSerializable
    {
        /// <summary>
        /// 气泡文本内容。
        /// </summary>
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
    /// <summary>
    /// 社交房间气泡同步。
    /// </summary>
    public sealed class S2C_SocialBubbleSync : ILiteNetSerializable
    {
        /// <summary>
        /// 目标实体 NetId。
        /// </summary>
        public int NetId;
        /// <summary>
        /// 气泡文本内容。
        /// </summary>
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
