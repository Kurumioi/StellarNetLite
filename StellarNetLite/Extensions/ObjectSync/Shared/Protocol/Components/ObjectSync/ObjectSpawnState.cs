using System.IO;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Shared.ObjectSync
{
    /// <summary>
    /// 对象完整生成态。
    /// </summary>
    public struct ObjectSpawnState : ILiteNetSerializable
    {
        /// <summary>
        /// 当前实体 NetId。
        /// </summary>
        public int NetId;

        /// <summary>
        /// 当前实体预制体 Hash。
        /// </summary>
        public int PrefabHash;

        /// <summary>
        /// 当前实体同步掩码。
        /// </summary>
        public byte Mask;

        /// <summary>
        /// 当前位置 X。
        /// </summary>
        public float PosX;

        /// <summary>
        /// 当前位置 Y。
        /// </summary>
        public float PosY;

        /// <summary>
        /// 当前位置 Z。
        /// </summary>
        public float PosZ;

        /// <summary>
        /// 当前旋转 X。
        /// </summary>
        public float RotX;

        /// <summary>
        /// 当前旋转 Y。
        /// </summary>
        public float RotY;

        /// <summary>
        /// 当前旋转 Z。
        /// </summary>
        public float RotZ;

        /// <summary>
        /// 当前朝向 X。
        /// </summary>
        public float DirX;

        /// <summary>
        /// 当前朝向 Y。
        /// </summary>
        public float DirY;

        /// <summary>
        /// 当前朝向 Z。
        /// </summary>
        public float DirZ;

        /// <summary>
        /// 当前缩放 X。
        /// </summary>
        public float ScaleX;

        /// <summary>
        /// 当前缩放 Y。
        /// </summary>
        public float ScaleY;

        /// <summary>
        /// 当前缩放 Z。
        /// </summary>
        public float ScaleZ;

        /// <summary>
        /// 当前动画状态 Hash。
        /// </summary>
        public int AnimStateHash;

        /// <summary>
        /// 当前动画归一化时间。
        /// </summary>
        public float AnimNormalizedTime;

        /// <summary>
        /// 当前动画参数数量。
        /// </summary>
        public int AnimParamCount;

        /// <summary>
        /// 当前动画参数列表。
        /// </summary>
        public AnimatorParamValue[] AnimParams;

        /// <summary>
        /// 当前实体拥有者 SessionId。
        /// </summary>
        public string OwnerSessionId;

        /// <summary>
        /// 序列化完整生成态。
        /// </summary>
        public void Serialize(BinaryWriter writer)
        {
            if (writer == null)
            {
                NetLogger.LogError("ObjectSpawnState", "序列化失败: Writer 为空");
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
            writer.Write(AnimParamCount);

            if (AnimParamCount > 0)
            {
                if (AnimParams == null || AnimParams.Length < AnimParamCount)
                {
                    NetLogger.LogError(
                        "ObjectSpawnState",
                        $"序列化失败: AnimParams 非法, Count:{AnimParamCount}, Length:{(AnimParams == null ? 0 : AnimParams.Length)}");
                    return;
                }

                for (int i = 0; i < AnimParamCount; i++)
                {
                    AnimParams[i].Serialize(writer);
                }
            }

            writer.Write(OwnerSessionId ?? string.Empty);
        }

        /// <summary>
        /// 反序列化完整生成态。
        /// </summary>
        public void Deserialize(BinaryReader reader)
        {
            if (reader == null)
            {
                NetLogger.LogError("ObjectSpawnState", "反序列化失败: Reader 为空");
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
            AnimParamCount = reader.ReadInt32();
            if (AnimParamCount < 0)
            {
                NetLogger.LogError("ObjectSpawnState", $"反序列化失败: AnimParamCount 非法, Count:{AnimParamCount}");
                AnimParamCount = 0;
                AnimParams = System.Array.Empty<AnimatorParamValue>();
            }
            else
            {
                if (AnimParams == null || AnimParams.Length < AnimParamCount)
                {
                    AnimParams = new AnimatorParamValue[AnimParamCount];
                }

                for (int i = 0; i < AnimParamCount; i++)
                {
                    AnimParams[i].Deserialize(reader);
                }
            }

            OwnerSessionId = reader.ReadString();
        }

        /// <summary>
        /// 创建默认完整生成态。
        /// </summary>
        public static ObjectSpawnState CreateDefault()
        {
            return new ObjectSpawnState
            {
                ScaleX = 1f,
                ScaleY = 1f,
                ScaleZ = 1f,
                AnimParams = System.Array.Empty<AnimatorParamValue>(),
                OwnerSessionId = string.Empty
            };
        }
    }
}
