#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Editor
{
    /// <summary>
    /// 网络预制体自动化扫描器。
    /// 职责：扫描 Resources/NetPrefabs 目录下的所有预制体，计算稳定的路径 Hash，并自动生成常量表与路径映射表。
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
                NetLogger.LogWarning("NetPrefabScanner", $"目标目录不存在，已自动创建: {TargetResourceFolder}。请将需要同步的预制体放入此目录。");
                Directory.CreateDirectory(TargetResourceFolder);
                AssetDatabase.Refresh();
                return;
            }

            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { TargetResourceFolder });
            if (prefabGuids == null || prefabGuids.Length == 0)
            {
                NetLogger.LogWarning("NetPrefabScanner", $"目录 {TargetResourceFolder} 下未找到任何预制体，跳过生成。");
                return;
            }

            var prefabList = new List<(int Hash, string VarName, string ResPath)>();
            var hashSet = new HashSet<int>();
            bool hasConflict = false;

            foreach (string guid in prefabGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string resPath = GetResourcesRelativePath(assetPath);
                if (string.IsNullOrEmpty(resPath)) continue;

                int hash = GetStableHash(resPath);
                string varName = GenerateValidVariableName(resPath);

                if (!hashSet.Add(hash))
                {
                    NetLogger.LogError("NetPrefabScanner", $"致命错误: 预制体 Hash 冲突！路径: {resPath}, Hash: {hash}");
                    hasConflict = true;
                }
                else
                {
                    prefabList.Add((hash, varName, resPath));
                }
            }

            if (hasConflict)
            {
                NetLogger.LogError("NetPrefabScanner", "存在 Hash 冲突，已终止常量表生成。请尝试重命名冲突的预制体。");
                return;
            }

            bool isChanged = GenerateConstFile(prefabList);
            if (isChanged)
            {
                AssetDatabase.Refresh();
                NetLogger.LogInfo("NetPrefabScanner", $"网络预制体常量表生成完毕，共收录 {prefabList.Count} 个预制体。");
            }
            else
            {
                NetLogger.LogInfo("NetPrefabScanner", "网络预制体常量表未发生变化，无需更新。");
            }
        }

        private static string GetResourcesRelativePath(string fullAssetPath)
        {
            int resIndex = fullAssetPath.IndexOf("Resources/", StringComparison.Ordinal);
            if (resIndex < 0) return string.Empty;
            int startIndex = resIndex + "Resources/".Length;
            int dotIndex = fullAssetPath.LastIndexOf('.');
            if (dotIndex < 0) dotIndex = fullAssetPath.Length;
            return fullAssetPath.Substring(startIndex, dotIndex - startIndex);
        }

        private static int GetStableHash(string input)
        {
            if (string.IsNullOrEmpty(input)) return 0;
            uint hash = 2166136261;
            foreach (char c in input)
            {
                hash ^= c;
                hash *= 16777619;
            }

            return (int)hash;
        }

        private static string GenerateValidVariableName(string resPath)
        {
            string varName = resPath.Replace("/", "_").Replace(" ", "_").Replace("-", "_");
            if (char.IsDigit(varName[0]))
            {
                varName = "Prefab_" + varName;
            }

            return varName;
        }

        private static bool GenerateConstFile(List<(int Hash, string VarName, string ResPath)> prefabList)
        {
            prefabList.Sort((a, b) => string.Compare(a.VarName, b.VarName, StringComparison.Ordinal));

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("// ========================================================");
            sb.AppendLine("// 自动生成的网络预制体 Hash 常量表与路径映射。");
            sb.AppendLine("// 请勿手动修改！请通过菜单 [StellarNet/Lite 生成网络预制体常量表] 驱动此文件更新。");
            sb.AppendLine("// ========================================================");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("");
            sb.AppendLine("namespace StellarNet.Lite.Shared.Protocol");
            sb.AppendLine("{");
            sb.AppendLine("    public static class NetPrefabConsts");
            sb.AppendLine("    {");

            foreach (var prefab in prefabList)
            {
                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// 映射路径: Resources/{prefab.ResPath}");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        public const int {prefab.VarName} = {prefab.Hash};");
                sb.AppendLine("");
            }

            sb.AppendLine("        public static readonly Dictionary<int, string> HashToPathMap = new Dictionary<int, string>");
            sb.AppendLine("        {");
            foreach (var prefab in prefabList)
            {
                sb.AppendLine($"            {{ {prefab.Hash}, \"{prefab.ResPath}\" }},");
            }

            sb.AppendLine("        };");

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return WriteToFileIfChanged(OutputConstFilePath, sb.ToString());
        }

        private static bool WriteToFileIfChanged(string path, string newContent)
        {
            string oldContent = string.Empty;
            if (File.Exists(path))
            {
                oldContent = File.ReadAllText(path);
            }

            if (newContent != oldContent)
            {
                try
                {
                    string directory = Path.GetDirectoryName(path);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllText(path, newContent, Encoding.UTF8);
                    return true;
                }
                catch (Exception e)
                {
                    NetLogger.LogError("NetPrefabScanner", $"写入常量表文件失败: {e.Message}");
                    return false;
                }
            }

            return false;
        }
    }
}
#endif