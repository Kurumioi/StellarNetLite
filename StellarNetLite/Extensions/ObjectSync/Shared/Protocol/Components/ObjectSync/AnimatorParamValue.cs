using System.IO;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Shared.ObjectSync
{
    /// <summary>
    /// 单个动画参数同步值。
    /// ParamHash 使用服务端稳定字符串哈希。
    /// </summary>
    public struct AnimatorParamValue
    {
        /// <summary>
        /// 逻辑参数哈希。
        /// </summary>
        public int ParamHash;

        /// <summary>
        /// 当前参数值。
        /// </summary>
        public float Value;

        /// <summary>
        /// 序列化单个动画参数值。
        /// </summary>
        public void Serialize(BinaryWriter writer)
        {
            if (writer == null)
            {
                NetLogger.LogError("AnimatorParamValue", "序列化失败: Writer 为空");
                return;
            }

            writer.Write(ParamHash);
            writer.Write(Value);
        }

        /// <summary>
        /// 反序列化单个动画参数值。
        /// </summary>
        public void Deserialize(BinaryReader reader)
        {
            if (reader == null)
            {
                NetLogger.LogError("AnimatorParamValue", "反序列化失败: Reader 为空");
                return;
            }

            ParamHash = reader.ReadInt32();
            Value = reader.ReadSingle();
        }
    }
}
