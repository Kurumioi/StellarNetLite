namespace StellarNet.Lite.Shared.ObjectSync
{
    /// <summary>
    /// ObjectSync 动画相关的稳定哈希工具。
    /// 服务端与客户端通过同一规则对逻辑状态名和逻辑参数名做稳定哈希。
    /// </summary>
    public static class ObjectSyncAnimHashUtility
    {
        /// <summary>
        /// 计算稳定字符串哈希。
        /// </summary>
        public static int GetStableStringHash(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }

            unchecked
            {
                int hash = 23;
                for (int i = 0; i < value.Length; i++)
                {
                    hash = hash * 31 + value[i];
                }

                return hash;
            }
        }
    }
}
