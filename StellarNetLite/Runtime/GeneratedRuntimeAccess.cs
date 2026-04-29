using System;
using System.Reflection;

namespace StellarNet.Lite.Runtime
{
    /// <summary>
    /// Runtime 对生成代码的安全访问入口。
    /// 允许仓库仅保留占位或暂时缺少某些生成文件时，Runtime 仍能先通过编译。
    /// </summary>
    internal static class GeneratedRuntimeAccess
    {
        /// <summary>
        /// 运行时特性注册表的完整类型名。
        /// </summary>
        private const string RuntimeFeatureRegistryTypeName = "StellarNet.Lite.Shared.Binders.AutoRuntimeFeatureRegistry";

        /// <summary>
        /// 未鉴权协议白名单注册表的完整类型名。
        /// </summary>
        private const string UnauthenticatedRegistryTypeName = "StellarNet.Lite.Shared.Protocol.AutoUnauthenticatedProtocolRegistry";

        /// <summary>
        /// 从生成注册表中创建全部 RuntimeFeatureBridge 实例。
        /// </summary>
        public static IRuntimeFeatureBridge[] CreateRuntimeFeatureBridges()
        {
            Type registryType = ResolveType(RuntimeFeatureRegistryTypeName);
            if (registryType == null)
            {
                return Array.Empty<IRuntimeFeatureBridge>();
            }

            MethodInfo method = registryType.GetMethod(
                "CreateFeatures",
                BindingFlags.Public | BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null);
            if (method == null)
            {
                return Array.Empty<IRuntimeFeatureBridge>();
            }

            object value = method.Invoke(null, null);
            return value as IRuntimeFeatureBridge[] ?? Array.Empty<IRuntimeFeatureBridge>();
        }

        /// <summary>
        /// 读取允许未鉴权访问的 Global C2S 协议 Id 列表。
        /// </summary>
        public static int[] GetUnauthenticatedGlobalC2SMsgIds()
        {
            Type registryType = ResolveType(UnauthenticatedRegistryTypeName);
            if (registryType == null)
            {
                return Array.Empty<int>();
            }

            FieldInfo field = registryType.GetField("GlobalC2SMsgIds", BindingFlags.Public | BindingFlags.Static);
            object value = field != null ? field.GetValue(null) : null;
            return value as int[] ?? Array.Empty<int>();
        }

        /// <summary>
        /// 在当前程序集内解析指定完整类型名。
        /// </summary>
        private static Type ResolveType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return null;
            }

            Assembly assembly = typeof(GeneratedRuntimeAccess).Assembly;
            return assembly.GetType(fullName);
        }
    }
}
