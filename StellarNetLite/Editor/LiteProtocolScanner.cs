#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Editor
{
    /// <summary>
    /// 协议元数据扫描与代码自动生成器。
    /// 职责：
    /// 1. 编译后极速扫描所有 [NetMsg]，校验 ID 是否冲突。
    /// 2. 自动生成 MsgIdConst.cs 常量表，彻底消灭手动维护魔数带来的错位风险。
    /// </summary>
    [InitializeOnLoad]
    public static class LiteProtocolScanner
    {
        private const string OutputPath = "Assets/StellarNetLite/Runtime/Shared/Protocol/MsgIdConst.cs";

        static LiteProtocolScanner()
        {
            RunScanAndGenerate();
        }

        [MenuItem("StellarNet/Lite 强制重新生成协议常量表")]
        public static void ManualRun()
        {
            RunScanAndGenerate();
            Debug.Log("[LiteProtocolScanner] 手动触发协议扫描与常量表生成完毕。");
        }

        private static void RunScanAndGenerate()
        {
            var types = TypeCache.GetTypesWithAttribute<NetMsgAttribute>();
            var protocolList = new List<(int Id, string Name)>();
            var idSet = new HashSet<int>();
            bool hasConflict = false;

            foreach (var type in types)
            {
                var attr = type.GetCustomAttribute<NetMsgAttribute>();
                if (attr != null)
                {
                    if (!idSet.Add(attr.Id))
                    {
                        Debug.LogError($"[StellarNet 致命错误] 协议 ID 冲突: ID {attr.Id} 在类 {type.Name} 中重复使用，请立即修改！");
                        hasConflict = true;
                    }
                    else
                    {
                        protocolList.Add((attr.Id, type.Name));
                    }
                }
            }

            if (hasConflict)
            {
                Debug.LogError("[LiteProtocolScanner] 存在协议 ID 冲突，已终止常量表生成。");
                return;
            }

            GenerateConstFile(protocolList);
        }

        private static void GenerateConstFile(List<(int Id, string Name)> protocolList)
        {
            // 按 ID 升序排列，保证生成的代码整洁可读
            protocolList = protocolList.OrderBy(p => p.Id).ToList();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("// ========================================================");
            sb.AppendLine("// 自动生成的协议 ID 常量表。");
            sb.AppendLine("// 请勿手动修改！请通过给类添加 [NetMsg] 特性来驱动此文件更新。");
            sb.AppendLine("// ========================================================");
            sb.AppendLine("namespace StellarNet.Lite.Shared.Protocol");
            sb.AppendLine("{");
            sb.AppendLine("    public static class MsgIdConst");
            sb.AppendLine("    {");

            foreach (var proto in protocolList)
            {
                sb.AppendLine($"        public const int {proto.Name} = {proto.Id};");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            string newContent = sb.ToString();

            // 防御性编程：读取旧文件内容进行比对，只有发生实质性变化才写入，防止触发 Unity 无限编译循环
            string oldContent = string.Empty;
            if (File.Exists(OutputPath))
            {
                oldContent = File.ReadAllText(OutputPath);
            }

            if (newContent != oldContent)
            {
                string directory = Path.GetDirectoryName(OutputPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(OutputPath, newContent, Encoding.UTF8);
                AssetDatabase.Refresh();
                Debug.Log($"[LiteProtocolScanner] 检测到协议变更，已自动更新常量表: {OutputPath}");
            }
        }
    }
}
#endif