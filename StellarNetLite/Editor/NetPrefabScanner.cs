#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using StellarNet.Lite.Client.Components.Views;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEditor;
using UnityEngine;

namespace StellarNet.Lite.Editor
{
    /// <summary>
    /// 网络预制体扫描器。
    /// 扫描挂载 NetIdentity 的 Resources 预制体并生成 hash 常量表。
    /// </summary>
    public static class NetPrefabScanner
    {
        /// <summary>
        /// 我将网络预制体常量表统一输出到生成目录根下，
        /// 这样客户端通过 prefab hash 反查资源路径时，只依赖这份静态表即可。
        /// </summary>
        private const string OutputConstFilePath = "Assets/StellarNetLiteGenerated/NetPrefabConsts.cs";

        [MenuItem("StellarNetLite/生成网络预制体常量表 (Net Prefabs)")]
        public static void ManualRun()
        {
            RunScanAndGenerate();
        }

        private static void RunScanAndGenerate()
        {
            // 扫描全项目 Prefab，再筛出符合网络预制体约定的资源。
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            if (prefabGuids == null || prefabGuids.Length == 0)
            {
                bool emptyChanged = GenerateConstFile(new List<(int Hash, string VarName, string ResPath)>());
                if (emptyChanged)
                {
                    AssetDatabase.Refresh();
                }

                NetLogger.LogWarning("NetPrefabScanner", "项目内未找到任何 Prefab，已生成空常量表。");
                return;
            }

            var prefabList = new List<(int Hash, string VarName, string ResPath)>(prefabGuids.Length);
            var hashMap = new Dictionary<int, string>(prefabGuids.Length);
            var varNameMap = new Dictionary<string, string>(prefabGuids.Length);
            bool hasFatalError = false;

            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string guid = prefabGuids[i];
                if (string.IsNullOrEmpty(guid))
                {
                    continue;
                }

                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath))
                {
                    NetLogger.LogError("NetPrefabScanner", $"扫描失败: GUID 无法解析路径, Guid:{guid}");
                    hasFatalError = true;
                    continue;
                }

                string resPath = GetResourcesRelativePath(assetPath);
                if (string.IsNullOrEmpty(resPath))
                {
                    continue;
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab == null)
                {
                    NetLogger.LogError("NetPrefabScanner",
                        $"扫描失败: Prefab 加载为空, AssetPath:{assetPath}, ResPath:{resPath}");
                    hasFatalError = true;
                    continue;
                }

                NetIdentity netIdentity = prefab.GetComponent<NetIdentity>();
                if (netIdentity == null)
                {
                    continue;
                }

                int hash = GetStableHash(resPath);
                string varName = GenerateValidVariableName(resPath);
                if (string.IsNullOrEmpty(varName))
                {
                    NetLogger.LogError("NetPrefabScanner",
                        $"扫描失败: 变量名为空, Prefab:{prefab.name}, ResPath:{resPath}, AssetPath:{assetPath}");
                    hasFatalError = true;
                    continue;
                }

                if (hashMap.TryGetValue(hash, out string existingPath))
                {
                    NetLogger.LogError("NetPrefabScanner",
                        $"Hash 冲突: Hash:{hash}, PathA:{existingPath}, PathB:{resPath}, Prefab:{prefab.name}");
                    hasFatalError = true;
                    continue;
                }

                if (varNameMap.TryGetValue(varName, out string existingVarPath))
                {
                    NetLogger.LogError("NetPrefabScanner",
                        $"变量名冲突: VarName:{varName}, PathA:{existingVarPath}, PathB:{resPath}, Prefab:{prefab.name}");
                    hasFatalError = true;
                    continue;
                }

                hashMap.Add(hash, resPath);
                varNameMap.Add(varName, resPath);
                prefabList.Add((hash, varName, resPath));
            }

            if (hasFatalError)
            {
                NetLogger.LogError("NetPrefabScanner", "扫描中存在致命冲突，已终止常量表生成。");
                return;
            }

            if (prefabList.Count == 0)
            {
                bool emptyChanged = GenerateConstFile(prefabList);
                if (emptyChanged)
                {
                    AssetDatabase.Refresh();
                }

                NetLogger.LogWarning("NetPrefabScanner", "项目内未找到任何挂载 NetIdentity 的 Resources 预制体，已生成空常量表。");
                return;
            }

            bool isChanged = GenerateConstFile(prefabList);
            if (!isChanged)
            {
                NetLogger.LogInfo("NetPrefabScanner", "网络预制体常量表未发生变化，无需更新。");
                return;
            }

            AssetDatabase.Refresh();
            NetLogger.LogInfo("NetPrefabScanner",
                $"网络预制体常量表生成完毕，共收录 {prefabList.Count} 个根节点挂载 NetIdentity 的 Resources 预制体。");
        }

        private static string GetResourcesRelativePath(string fullAssetPath)
        {
            if (string.IsNullOrEmpty(fullAssetPath))
            {
                return string.Empty;
            }

            string normalizedPath = fullAssetPath.Replace("\\", "/");
            const string resourcesToken = "/Resources/";

            int resIndex = normalizedPath.LastIndexOf(resourcesToken, StringComparison.Ordinal);
            if (resIndex < 0)
            {
                return string.Empty;
            }

            int startIndex = resIndex + resourcesToken.Length;
            if (startIndex >= normalizedPath.Length)
            {
                return string.Empty;
            }

            string relativeWithExtension = normalizedPath.Substring(startIndex);
            if (string.IsNullOrEmpty(relativeWithExtension))
            {
                return string.Empty;
            }

            string extension = Path.GetExtension(relativeWithExtension);
            if (string.IsNullOrEmpty(extension))
            {
                return relativeWithExtension;
            }

            return relativeWithExtension.Substring(0, relativeWithExtension.Length - extension.Length);
        }

        private static int GetStableHash(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return 0;
            }

            uint hash = 2166136261;
            for (int i = 0; i < input.Length; i++)
            {
                hash ^= input[i];
                hash *= 16777619;
            }

            return unchecked((int)hash);
        }

        private static string GenerateValidVariableName(string resPath)
        {
            if (string.IsNullOrEmpty(resPath))
            {
                return string.Empty;
            }

            string varName = resPath.Replace("/", "_").Replace(" ", "_").Replace("-", "_");
            varName = System.Text.RegularExpressions.Regex.Replace(varName, @"[^a-zA-Z0-9_]", "_");
            if (string.IsNullOrEmpty(varName))
            {
                return string.Empty;
            }

            if (char.IsDigit(varName[0]))
            {
                varName = "Prefab_" + varName;
            }

            return varName;
        }

        private static bool GenerateConstFile(List<(int Hash, string VarName, string ResPath)> prefabList)
        {
            if (prefabList == null)
            {
                NetLogger.LogError("NetPrefabScanner", "生成常量表失败: prefabList 为空");
                return false;
            }

            prefabList.Sort((a, b) => string.Compare(a.VarName, b.VarName, StringComparison.Ordinal));

            var sb = new StringBuilder(4096);
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();
            sb.AppendLine("namespace StellarNet.Lite.Shared.Protocol");
            sb.AppendLine("{");
            sb.AppendLine("    public static class NetPrefabConsts");
            sb.AppendLine("    {");

            for (int i = 0; i < prefabList.Count; i++)
            {
                (int Hash, string VarName, string ResPath) prefab = prefabList[i];
                sb.AppendLine($"        public const int {prefab.VarName} = {prefab.Hash};");
            }

            if (prefabList.Count > 0)
            {
                sb.AppendLine();
            }

            sb.AppendLine(
                "        public static readonly Dictionary<int, string> HashToPathMap = new Dictionary<int, string>");
            sb.AppendLine("        {");
            for (int i = 0; i < prefabList.Count; i++)
            {
                (int Hash, string VarName, string ResPath) prefab = prefabList[i];
                sb.AppendLine($"            {{ {prefab.Hash}, \"{prefab.ResPath}\" }},");
            }

            sb.AppendLine("        };");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return WriteToFileIfChanged(OutputConstFilePath, sb.ToString());
        }

        private static bool WriteToFileIfChanged(string path, string newContent)
        {
            if (string.IsNullOrEmpty(path))
            {
                NetLogger.LogError("NetPrefabScanner", "写入失败: path 为空");
                return false;
            }

            if (newContent == null)
            {
                NetLogger.LogError("NetPrefabScanner", $"写入失败: newContent 为空, Path:{path}");
                return false;
            }

            string normalizedPath = path.Replace("\\", "/");
            string oldContent = File.Exists(normalizedPath) ? File.ReadAllText(normalizedPath) : string.Empty;
            if (newContent == oldContent)
            {
                return false;
            }

            string directory = Path.GetDirectoryName(normalizedPath);
            if (string.IsNullOrEmpty(directory))
            {
                NetLogger.LogError("NetPrefabScanner", $"写入失败: 目录为空, Path:{normalizedPath}");
                return false;
            }

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(normalizedPath, newContent, Encoding.UTF8);
            return true;
        }
    }
}
#endif
