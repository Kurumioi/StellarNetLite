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
        private const string RuntimeFeatureRegistryTypeName = "StellarNet.Lite.Shared.Binders.AutoRuntimeFeatureRegistry";
        private const string UnauthenticatedRegistryTypeName = "StellarNet.Lite.Shared.Protocol.AutoUnauthenticatedProtocolRegistry";

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
