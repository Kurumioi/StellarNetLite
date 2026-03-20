using System.IO;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Shared.ObjectSync
{
    /// <summary>
    /// 对象完整生成态共享结构。
    /// 我把对象生成、重连恢复、回放关键帧恢复共用的字段统一收敛到这里，
    /// 是为了让完整对象状态只维护一份事实源，避免在线事件、回放结构、本地事件分别复制同样字段。
    /// </summary>
    public struct ObjectSpawnState : ILiteNetSerializable
    {
        public int NetId;
        public int PrefabHash;
        public byte Mask;

        public float PosX;
        public float PosY;
        public float PosZ;

        public float RotX;
        public float RotY;
        public float RotZ;

        public float DirX;
        public float DirY;
        public float DirZ;

        public float ScaleX;
        public float ScaleY;
        public float ScaleZ;

        public int AnimStateHash;
        public float AnimNormalizedTime;
        public float FloatParam1;
        public float FloatParam2;
        public float FloatParam3;

        public string OwnerSessionId;

        /// <summary>
        /// 我显式维护二进制顺序，是为了让在线协议和录像关键帧共同依赖同一份稳定布局。
        /// </summary>
        public void Serialize(BinaryWriter writer)
        {
            if (writer == null)
            {
                NetLogger.LogError("ObjectSpawnState", "序列化失败: writer 为空");
                return;
            }

            writer.Write(NetId);
            writer.Write(PrefabHash);
            writer.Write(Mask);

            writer.Write(PosX);
            writer.Write(PosY);
            writer.Write(PosZ);

            writer.Write(RotX);
            writer.Write(RotY);
            writer.Write(RotZ);

            writer.Write(DirX);
            writer.Write(DirY);
            writer.Write(DirZ);

            writer.Write(ScaleX);
            writer.Write(ScaleY);
            writer.Write(ScaleZ);

            writer.Write(AnimStateHash);
            writer.Write(AnimNormalizedTime);
            writer.Write(FloatParam1);
            writer.Write(FloatParam2);
            writer.Write(FloatParam3);

            writer.Write(OwnerSessionId ?? string.Empty);
        }

        /// <summary>
        /// 我按与 Serialize 完全一致的顺序回读字段，
        /// 这样后续扩展完整生成态时，只需要维护这一处共享结构，不需要多处同步补字段。
        /// </summary>
        public void Deserialize(BinaryReader reader)
        {
            if (reader == null)
            {
                NetLogger.LogError("ObjectSpawnState", "反序列化失败: reader 为空");
                return;
            }

            NetId = reader.ReadInt32();
            PrefabHash = reader.ReadInt32();
            Mask = reader.ReadByte();

            PosX = reader.ReadSingle();
            PosY = reader.ReadSingle();
            PosZ = reader.ReadSingle();

            RotX = reader.ReadSingle();
            RotY = reader.ReadSingle();
            RotZ = reader.ReadSingle();

            DirX = reader.ReadSingle();
            DirY = reader.ReadSingle();
            DirZ = reader.ReadSingle();

            ScaleX = reader.ReadSingle();
            ScaleY = reader.ReadSingle();
            ScaleZ = reader.ReadSingle();

            AnimStateHash = reader.ReadInt32();
            AnimNormalizedTime = reader.ReadSingle();
            FloatParam1 = reader.ReadSingle();
            FloatParam2 = reader.ReadSingle();
            FloatParam3 = reader.ReadSingle();

            OwnerSessionId = reader.ReadString();
        }

        /// <summary>
        /// 我提供默认构造入口，是为了避免业务侧漏填缩放时写出零缩放脏数据。
        /// </summary>
        public static ObjectSpawnState CreateDefault()
        {
            return new ObjectSpawnState
            {
                ScaleX = 1f,
                ScaleY = 1f,
                ScaleZ = 1f,
                OwnerSessionId = string.Empty
            };
        }
    }
}
