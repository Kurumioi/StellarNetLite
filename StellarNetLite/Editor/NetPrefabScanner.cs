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
    /// 职责：扫描 Resources/NetPrefabs 目录下的所有预制体，计算稳定的路径 Hash，并自动生成常量表。
    /// 架构意图：彻底消灭网络协议中的魔法数字与字符串，实现强类型的资源加载映射。
    /// </summary>
    public static class NetPrefabScanner
    {
        private const string TargetResourceFolder = "Assets/Resources/NetPrefabs";
        private const string OutputConstFilePath = "Assets/StellarNetLite/Runtime/Shared/Protocol/NetPrefabConsts.cs";

        [MenuItem("StellarNet/Lite 生成网络预制体常量表 (Net Prefabs)")]
        public static void ManualRun()
        {
            RunScanAndGenerate();
        }

        private static void RunScanAndGenerate()
        {
            if (!Directory.Exists(TargetResourceFolder))
            {
                NetLogger.LogWarning("[NetPrefabScanner]", $"目标目录不存在，已自动创建: {TargetResourceFolder}。请将需要同步的预制体放入此目录。");
                Directory.CreateDirectory(TargetResourceFolder);
                AssetDatabase.Refresh();
                return;
            }

            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { TargetResourceFolder });
            if (prefabGuids == null || prefabGuids.Length == 0)
            {
                NetLogger.LogWarning("[NetPrefabScanner]", $"目录 {TargetResourceFolder} 下未找到任何预制体，跳过生成。");
                return;
            }

            var prefabList = new List<(int Hash, string VarName, string ResPath)>();
            var hashSet = new HashSet<int>();
            bool hasConflict = false;

            foreach (string guid in prefabGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                // 提取 Resources 相对路径 (例如: NetPrefabs/Player_Warrior)
                string resPath = GetResourcesRelativePath(assetPath);
                if (string.IsNullOrEmpty(resPath)) continue;

                // 计算稳定的 Hash 值
                int hash = GetStableHash(resPath);

                // 生成合法的 C# 变量名 (替换斜杠和非法字符)
                string varName = GenerateValidVariableName(resPath);

                if (!hashSet.Add(hash))
                {
                    NetLogger.LogError("[NetPrefabScanner]", $"致命错误: 预制体 Hash 冲突！路径: {resPath}, Hash: {hash}");
                    hasConflict = true;
                }
                else
                {
                    prefabList.Add((hash, varName, resPath));
                }
            }

            if (hasConflict)
            {
                NetLogger.LogError("[NetPrefabScanner]", "存在 Hash 冲突，已终止常量表生成。请尝试重命名冲突的预制体。");
                return;
            }

            bool isChanged = GenerateConstFile(prefabList);
            if (isChanged)
            {
                AssetDatabase.Refresh();
                NetLogger.LogInfo("[NetPrefabScanner]", $"网络预制体常量表生成完毕，共收录 {prefabList.Count} 个预制体。");
            }
            else
            {
                NetLogger.LogInfo("[NetPrefabScanner]", "网络预制体常量表未发生变化，无需更新。");
            }
        }

        /// <summary>
        /// 提取适用于 Resources.Load 的相对路径 (去除扩展名与 Resources/ 前缀)
        /// </summary>
        private static string GetResourcesRelativePath(string fullAssetPath)
        {
            int resIndex = fullAssetPath.IndexOf("Resources/", StringComparison.Ordinal);
            if (resIndex < 0) return string.Empty;

            int startIndex = resIndex + "Resources/".Length;
            int dotIndex = fullAssetPath.LastIndexOf('.');
            if (dotIndex < 0) dotIndex = fullAssetPath.Length;

            return fullAssetPath.Substring(startIndex, dotIndex - startIndex);
        }

        /// <summary>
        /// 采用 FNV-1a 32位哈希算法。
        /// 为什么不用 string.GetHashCode()？因为 GetHashCode 在不同设备或 .NET 版本间可能不一致，会导致双端 Hash 错位。
        /// </summary>
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

        /// <summary>
        /// 将路径转换为合法的 C# 变量名。
        /// 例如: NetPrefabs/Monsters/Goblin -> NetPrefabs_Monsters_Goblin
        /// </summary>
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
            sb.AppendLine("// 自动生成的网络预制体 Hash 常量表。");
            sb.AppendLine("// 请勿手动修改！请通过菜单 [StellarNet/Lite 生成网络预制体常量表] 驱动此文件更新。");
            sb.AppendLine("// ========================================================");
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
                    // 允许的 Try-Catch：处理不可控的底层文件 I/O 异常
                    NetLogger.LogError("[NetPrefabScanner]", $"写入常量表文件失败: {e.Message}");
                    return false;
                }
            }

            return false;
        }
    }
}
#endif