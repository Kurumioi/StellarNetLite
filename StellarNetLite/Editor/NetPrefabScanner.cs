#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEditor;

namespace StellarNet.Lite.Editor
{
    /// <summary>
    /// 网络预制体自动化扫描器。
    /// 职责：扫描 Resources/NetPrefabs 目录下的所有预制体，计算稳定路径 Hash，并自动生成常量表与路径映射表。
    /// </summary>
    public static class NetPrefabScanner
    {
        private const string TargetResourceFolder = "Assets/Resources/NetPrefabs";
        private const string OutputConstFilePath = "Assets/StellarNetLite/Runtime/Shared/Protocol/Const/NetPrefabConsts.cs";

        [MenuItem("StellarNet/Lite 生成网络预制体常量表 (Net Prefabs)")]
        public static void ManualRun()
        {
            RunScanAndGenerate();
        }

        private static void RunScanAndGenerate()
        {
            if (!Directory.Exists(TargetResourceFolder))
            {
                Directory.CreateDirectory(TargetResourceFolder);
                AssetDatabase.Refresh();
                NetLogger.LogWarning("NetPrefabScanner", $"目标目录不存在，已自动创建: {TargetResourceFolder}");
            }

            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { TargetResourceFolder });
            if (prefabGuids == null || prefabGuids.Length == 0)
            {
                bool emptyChanged = GenerateConstFile(new List<(int Hash, string VarName, string ResPath)>());
                if (emptyChanged)
                {
                    AssetDatabase.Refresh();
                }

                NetLogger.LogWarning("NetPrefabScanner", $"目录下未找到任何预制体，已生成空常量表: {TargetResourceFolder}");
                return;
            }

            var prefabList = new List<(int Hash, string VarName, string ResPath)>();
            var hashMap = new Dictionary<int, string>();
            var varNameMap = new Dictionary<string, string>();
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
                    NetLogger.LogError("NetPrefabScanner", $"扫描失败: 资源路径非法, AssetPath:{assetPath}");
                    hasFatalError = true;
                    continue;
                }

                int hash = GetStableHash(resPath);
                string varName = GenerateValidVariableName(resPath);
                if (string.IsNullOrEmpty(varName))
                {
                    NetLogger.LogError("NetPrefabScanner", $"扫描失败: 变量名为空, ResPath:{resPath}, AssetPath:{assetPath}");
                    hasFatalError = true;
                    continue;
                }

                if (hashMap.TryGetValue(hash, out string existingPath))
                {
                    NetLogger.LogError("NetPrefabScanner", $"Hash 冲突: Hash:{hash}, PathA:{existingPath}, PathB:{resPath}");
                    hasFatalError = true;
                    continue;
                }

                if (varNameMap.TryGetValue(varName, out string existingVarPath))
                {
                    NetLogger.LogError("NetPrefabScanner", $"变量名冲突: VarName:{varName}, PathA:{existingVarPath}, PathB:{resPath}");
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

            bool isChanged = GenerateConstFile(prefabList);
            if (!isChanged)
            {
                NetLogger.LogInfo("NetPrefabScanner", "网络预制体常量表未发生变化，无需更新。");
                return;
            }

            AssetDatabase.Refresh();
            NetLogger.LogInfo("NetPrefabScanner", $"网络预制体常量表生成完毕，共收录 {prefabList.Count} 个预制体。");
        }

        private static string GetResourcesRelativePath(string fullAssetPath)
        {
            if (string.IsNullOrEmpty(fullAssetPath))
            {
                return string.Empty;
            }

            int resIndex = fullAssetPath.IndexOf("Resources/", StringComparison.Ordinal);
            if (resIndex < 0)
            {
                return string.Empty;
            }

            int startIndex = resIndex + "Resources/".Length;
            int dotIndex = fullAssetPath.LastIndexOf('.');
            if (dotIndex < 0)
            {
                dotIndex = fullAssetPath.Length;
            }

            if (dotIndex <= startIndex)
            {
                return string.Empty;
            }

            return fullAssetPath.Substring(startIndex, dotIndex - startIndex);
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

            return (int)hash;
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
            prefabList.Sort((a, b) => string.Compare(a.VarName, b.VarName, StringComparison.Ordinal));

            var sb = new StringBuilder();
            sb.AppendLine("// ========================================================");
            sb.AppendLine("// 自动生成的网络预制体 Hash 常量表与路径映射。");
            sb.AppendLine("// 请勿手动修改！请通过菜单 [StellarNet/Lite 生成网络预制体常量表] 驱动此文件更新。");
            sb.AppendLine("// ========================================================");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();
            sb.AppendLine("namespace StellarNet.Lite.Shared.Protocol");
            sb.AppendLine("{");
            sb.AppendLine("    public static class NetPrefabConsts");
            sb.AppendLine("    {");

            for (int i = 0; i < prefabList.Count; i++)
            {
                (int Hash, string VarName, string ResPath) prefab = prefabList[i];
                sb.AppendLine("        /// <summary>");
                sb.AppendLine($"        /// 映射路径: Resources/{prefab.ResPath}");
                sb.AppendLine("        /// </summary>");
                sb.AppendLine($"        public const int {prefab.VarName} = {prefab.Hash};");
                sb.AppendLine();
            }

            sb.AppendLine("        public static readonly Dictionary<int, string> HashToPathMap = new Dictionary<int, string>");
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

            string oldContent = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            if (newContent == oldContent)
            {
                return false;
            }

            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                NetLogger.LogError("NetPrefabScanner", $"写入失败: 目录为空, Path:{path}");
                return false;
            }

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, newContent, Encoding.UTF8);
            return true;
        }
    }
}
#endif