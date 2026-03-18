#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Editor
{
    /// <summary>
    /// 协议与组件元数据扫描器 (终极安全版)。
    /// 核心修复：引入 AST 级语义校验、标识符净化、重复注册拦截、严格类型比对。
    /// 架构防御：引入 GetCSharpTypeName 统一类型解析，防止泛型/嵌套类炸毁生成代码。
    /// </summary>
    [InitializeOnLoad]
    public static class LiteProtocolScanner
    {
        private const string ProtocolOutputPath = "Assets/StellarNetLite/Runtime/Shared/Protocol/Const/MsgIdConst.cs";
        private const string ComponentOutputPath = "Assets/StellarNetLite/Runtime/Shared/Protocol/Const/ComponentIdConst.cs";
        private const string RegistryOutputPath = "Assets/StellarNetLite/Runtime/Shared/Binders/AutoRegistry.cs";

        private class MethodMeta
        {
            public string MethodName;
            public string MsgFullName;
            public int MsgId;
        }

        private class ClassMeta
        {
            public int Id;
            public string Name;
            public string SafeVarName; // 净化后的安全变量名
            public string FullName;
            public string DisplayName;
            public List<MethodMeta> Methods = new List<MethodMeta>();
        }

        static LiteProtocolScanner()
        {
            RunScanAndGenerate();
        }

        [MenuItem("StellarNet/Lite 强制重新生成协议与组件常量表")]
        public static void ManualRun()
        {
            RunScanAndGenerate();
        }

        private static void RunScanAndGenerate()
        {
            bool protocolChanged = ScanAndGenerateProtocols();
            bool componentChanged = ScanAndGenerateComponentsAndRegistry();
            if (protocolChanged || componentChanged)
            {
                AssetDatabase.Refresh();
                NetLogger.LogInfo("LiteProtocolScanner", "0 反射装配代码已重新生成并应用。");
            }
        }

        #region ================= 核心防御：C# 源码类型名统一解析 =================

        private static readonly Dictionary<Type, string> BuiltInTypeNames = new Dictionary<Type, string>
        {
            { typeof(void), "void" }, { typeof(bool), "bool" }, { typeof(byte), "byte" },
            { typeof(sbyte), "sbyte" }, { typeof(char), "char" }, { typeof(decimal), "decimal" },
            { typeof(double), "double" }, { typeof(float), "float" }, { typeof(int), "int" },
            { typeof(uint), "uint" }, { typeof(long), "long" }, { typeof(ulong), "ulong" },
            { typeof(object), "object" }, { typeof(short), "short" }, { typeof(ushort), "ushort" },
            { typeof(string), "string" }
        };

        /// <summary>
        /// 将反射的 Type 转换为合法的 C# 源码类型字符串。
        /// 核心修复 P0-2：完美覆盖多维数组、指针、引用、开放泛型等极端边界。
        /// </summary>
        private static string GetCSharpTypeName(Type type)
        {
            if (type == null) return "void";

            // 1. 处理 by-ref (如 ref int, out string)
            if (type.IsByRef)
            {
                return "ref " + GetCSharpTypeName(type.GetElementType());
            }

            // 2. 处理指针 (如 int*)
            if (type.IsPointer)
            {
                return GetCSharpTypeName(type.GetElementType()) + "*";
            }

            // 3. 处理开放泛型参数本身 (如 T)
            if (type.IsGenericParameter)
            {
                return type.Name;
            }

            // 4. 处理内置基础类型 (如 int, string)
            if (BuiltInTypeNames.TryGetValue(type, out string builtInName))
            {
                return builtInName;
            }

            // 5. 处理数组 (含多维数组，如 int[,], List<string>[])
            if (type.IsArray)
            {
                int rank = type.GetArrayRank();
                string commas = new string(',', rank - 1);
                return GetCSharpTypeName(type.GetElementType()) + $"[{commas}]";
            }

            // 6. 处理泛型 (如 Dictionary<int, string>)
            if (type.IsGenericType)
            {
                string genericName = type.GetGenericTypeDefinition().FullName;
                if (genericName != null)
                {
                    // 剔除反引号及后面的数字，并将嵌套类的 + 替换为 .
                    genericName = genericName.Substring(0, genericName.IndexOf('`')).Replace('+', '.');
                }
                else
                {
                    genericName = type.Name.Substring(0, type.Name.IndexOf('`'));
                }

                var genericArgs = type.GetGenericArguments();
                string[] argNames = new string[genericArgs.Length];
                for (int i = 0; i < genericArgs.Length; i++)
                {
                    argNames[i] = GetCSharpTypeName(genericArgs[i]);
                }

                return $"{genericName}<{string.Join(", ", argNames)}>";
            }

            // 7. 处理常规类型与嵌套类 (将 Outer+Inner 转换为 Outer.Inner)
            if (type.FullName != null)
            {
                return type.FullName.Replace('+', '.');
            }

            return type.Name;
        }

        #endregion

        private static string SanitizeIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unknown";
            string safe = Regex.Replace(name, @"[^a-zA-Z0-9_]", "_");
            if (char.IsDigit(safe[0])) safe = "_" + safe;
            return safe;
        }

        private static bool ScanAndGenerateProtocols()
        {
            var types = TypeCache.GetTypesWithAttribute<NetMsgAttribute>();
            var protocolList = new List<(int Id, string SafeName)>();
            var idSet = new HashSet<int>();
            bool hasConflict = false;

            foreach (var type in types)
            {
                var attr = type.GetCustomAttribute<NetMsgAttribute>();
                if (attr != null)
                {
                    if (!idSet.Add(attr.Id))
                    {
                        NetLogger.LogError("LiteProtocolScanner", $"协议 ID 冲突: ID {attr.Id} 在类 {type.Name} 中重复使用！");
                        hasConflict = true;
                    }
                    else
                    {
                        protocolList.Add((attr.Id, SanitizeIdentifier(type.Name)));
                    }
                }
            }

            if (hasConflict) return false;
            return GenerateProtocolConstFile(protocolList);
        }

        private static bool ScanAndGenerateComponentsAndRegistry()
        {
            var serverComps = new List<ClassMeta>();
            var clientComps = new List<ClassMeta>();
            var serverMods = new List<ClassMeta>();
            var clientMods = new List<ClassMeta>();

            // 用于拦截同端同作用域下的 MsgId 重复注册
            var serverGlobalMsgIds = new Dictionary<int, string>();
            var serverRoomMsgIds = new Dictionary<int, string>();
            var clientGlobalMsgIds = new Dictionary<int, string>();
            var clientRoomMsgIds = new Dictionary<int, string>();

            bool hasFatalError = false;

            // 1. 扫描 RoomComponent
            var roomCompTypes = TypeCache.GetTypesWithAttribute<RoomComponentAttribute>();
            foreach (var type in roomCompTypes)
            {
                var attr = type.GetCustomAttribute<RoomComponentAttribute>();
                if (attr == null) continue;

                var meta = new ClassMeta
                {
                    Id = attr.Id,
                    Name = attr.Name,
                    SafeVarName = SanitizeIdentifier(GetCSharpTypeName(type)),
                    FullName = GetCSharpTypeName(type),
                    DisplayName = attr.DisplayName
                };

                bool isServer = type.IsSubclassOf(typeof(StellarNet.Lite.Server.Core.RoomComponent));
                bool isClient = type.IsSubclassOf(typeof(StellarNet.Lite.Client.Core.ClientRoomComponent));

                if (isServer)
                {
                    if (!ScanMethods(type, true, false, meta, serverRoomMsgIds)) hasFatalError = true;
                    serverComps.Add(meta);
                }
                else if (isClient)
                {
                    if (!ScanMethods(type, false, false, meta, clientRoomMsgIds)) hasFatalError = true;
                    clientComps.Add(meta);
                }
            }

            // 2. 扫描 ServerModule
            var serverModTypes = TypeCache.GetTypesWithAttribute<ServerModuleAttribute>();
            foreach (var type in serverModTypes)
            {
                var attr = type.GetCustomAttribute<ServerModuleAttribute>();
                if (attr == null) continue;
                var meta = new ClassMeta
                {
                    Name = attr.Name,
                    SafeVarName = SanitizeIdentifier(GetCSharpTypeName(type)),
                    FullName = GetCSharpTypeName(type),
                    DisplayName = attr.DisplayName
                };
                if (!ScanMethods(type, true, true, meta, serverGlobalMsgIds)) hasFatalError = true;
                serverMods.Add(meta);
            }

            // 3. 扫描 ClientModule
            var clientModTypes = TypeCache.GetTypesWithAttribute<ClientModuleAttribute>();
            foreach (var type in clientModTypes)
            {
                var attr = type.GetCustomAttribute<ClientModuleAttribute>();
                if (attr == null) continue;
                var meta = new ClassMeta
                {
                    Name = attr.Name,
                    SafeVarName = SanitizeIdentifier(GetCSharpTypeName(type)),
                    FullName = GetCSharpTypeName(type),
                    DisplayName = attr.DisplayName
                };
                if (!ScanMethods(type, false, true, meta, clientGlobalMsgIds)) hasFatalError = true;
                clientMods.Add(meta);
            }

            if (hasFatalError)
            {
                NetLogger.LogError("LiteProtocolScanner", "扫描阶段发现致命语义错误，已强行阻断装配代码生成，请修复上述 Error！");
                return false;
            }

            bool constChanged = GenerateComponentConstFile(serverComps.Concat(clientComps).GroupBy(c => c.Id).Select(g => g.First()).ToList());
            bool registryChanged = GenerateAutoRegistryFile(serverComps, clientComps, serverMods, clientMods);
            return constChanged || registryChanged;
        }

        private static bool ScanMethods(Type type, bool isServer, bool isGlobalModule, ClassMeta meta, Dictionary<int, string> msgIdTracker)
        {
            var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            bool isSafe = true;

            foreach (var m in methods)
            {
                if (m.GetCustomAttribute<NetHandlerAttribute>() == null) continue;

                var parameters = m.GetParameters();
                Type msgType = null;

                if (isServer)
                {
                    if (parameters.Length != 2 || parameters[0].ParameterType != typeof(StellarNet.Lite.Server.Core.Session))
                    {
                        NetLogger.LogError("LiteProtocolScanner", $"方法 {type.Name}.{m.Name} 签名非法: 服务端必须为 (Session, TMsg)");
                        isSafe = false;
                        continue;
                    }

                    msgType = parameters[1].ParameterType;
                }
                else
                {
                    if (parameters.Length != 1)
                    {
                        NetLogger.LogError("LiteProtocolScanner", $"方法 {type.Name}.{m.Name} 签名非法: 客户端必须为 (TMsg)");
                        isSafe = false;
                        continue;
                    }

                    msgType = parameters[0].ParameterType;
                }

                var netMsgAttr = msgType.GetCustomAttribute<NetMsgAttribute>();
                if (netMsgAttr == null)
                {
                    NetLogger.LogError("LiteProtocolScanner", $"方法 {type.Name}.{m.Name} 的参数 {msgType.Name} 缺失 [NetMsg] 特性");
                    isSafe = false;
                    continue;
                }

                NetScope expectedScope = isGlobalModule ? NetScope.Global : NetScope.Room;
                NetDir expectedDir = isServer ? NetDir.C2S : NetDir.S2C;

                if (netMsgAttr.Scope != expectedScope || netMsgAttr.Dir != expectedDir)
                {
                    NetLogger.LogError("LiteProtocolScanner",
                        $"语义冲突: {type.Name}.{m.Name} 试图监听 {msgType.Name}。预期 Scope:{expectedScope} Dir:{expectedDir}，实际 Scope:{netMsgAttr.Scope} Dir:{netMsgAttr.Dir}");
                    isSafe = false;
                    continue;
                }

                if (msgIdTracker.TryGetValue(netMsgAttr.Id, out string existingMethod))
                {
                    NetLogger.LogError("LiteProtocolScanner",
                        $"重复注册: 协议 ID {netMsgAttr.Id} ({msgType.Name}) 被 {existingMethod} 和 {type.Name}.{m.Name} 同时监听！");
                    isSafe = false;
                    continue;
                }

                msgIdTracker.Add(netMsgAttr.Id, $"{type.Name}.{m.Name}");

                meta.Methods.Add(new MethodMeta
                {
                    MethodName = m.Name,
                    MsgFullName = GetCSharpTypeName(msgType),
                    MsgId = netMsgAttr.Id
                });
            }

            return isSafe;
        }

        private static bool GenerateProtocolConstFile(List<(int Id, string SafeName)> protocolList)
        {
            protocolList = protocolList.OrderBy(p => p.Id).ToList();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("// ========================================================");
            sb.AppendLine("// 自动生成的协议 ID 常量表。");
            sb.AppendLine("// ========================================================");
            sb.AppendLine("namespace StellarNet.Lite.Shared.Protocol");
            sb.AppendLine("{");
            sb.AppendLine("    public static class MsgIdConst");
            sb.AppendLine("    {");
            foreach (var proto in protocolList)
            {
                sb.AppendLine($"        public const int {proto.SafeName} = {proto.Id};");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return WriteToFileIfChanged(ProtocolOutputPath, sb.ToString());
        }

        private static bool GenerateComponentConstFile(List<ClassMeta> compList)
        {
            compList = compList.OrderBy(p => p.Id).ToList();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("// ========================================================");
            sb.AppendLine("// 自动生成的组件 ID 常量表。");
            sb.AppendLine("// ========================================================");
            sb.AppendLine("namespace StellarNet.Lite.Shared.Protocol");
            sb.AppendLine("{");
            sb.AppendLine("    public static class ComponentIdConst");
            sb.AppendLine("    {");
            foreach (var comp in compList)
            {
                sb.AppendLine($"        public const int {SanitizeIdentifier(comp.Name)} = {comp.Id};");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return WriteToFileIfChanged(ComponentOutputPath, sb.ToString());
        }

        private static bool GenerateAutoRegistryFile(List<ClassMeta> serverComps, List<ClassMeta> clientComps, List<ClassMeta> serverMods, List<ClassMeta> clientMods)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("// ========================================================");
            sb.AppendLine("// 自动生成的 0 反射静态装配器。");
            sb.AppendLine("// 请勿手动修改！由 LiteProtocolScanner 自动生成。");
            sb.AppendLine("// ========================================================");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using StellarNet.Lite.Shared.Core;");
            sb.AppendLine("using StellarNet.Lite.Server.Core;");
            sb.AppendLine("using StellarNet.Lite.Client.Core;");
            sb.AppendLine("using StellarNet.Lite.Shared.Protocol;");
            sb.AppendLine("");
            sb.AppendLine("namespace StellarNet.Lite.Shared.Binders");
            sb.AppendLine("{");
            sb.AppendLine("    public static class AutoRegistry");
            sb.AppendLine("    {");

            sb.AppendLine("        public static readonly List<RoomComponentMeta> RoomComponentMetaList = new List<RoomComponentMeta>");
            sb.AppendLine("        {");
            var uniqueComps = serverComps.Concat(clientComps).GroupBy(c => c.Id).Select(g => g.First()).OrderBy(c => c.Id).ToList();
            foreach (var comp in uniqueComps)
            {
                sb.AppendLine($"            new RoomComponentMeta {{ Id = {comp.Id}, Name = \"{comp.Name}\", DisplayName = \"{comp.DisplayName}\" }},");
            }

            sb.AppendLine("        };");
            sb.AppendLine("");

            sb.AppendLine("        public static void RegisterServer(ServerApp serverApp, Func<byte[], Type, object> deserializeFunc)");
            sb.AppendLine("        {");
            sb.AppendLine("            ServerRoomFactory.Clear();");
            foreach (var mod in serverMods)
            {
                sb.AppendLine($"            var mod_{mod.SafeVarName} = new {mod.FullName}(serverApp);");
                foreach (var method in mod.Methods)
                {
                    sb.AppendLine($"            serverApp.GlobalDispatcher.Register({method.MsgId}, (session, packet) => {{");
                    sb.AppendLine($"                if (deserializeFunc(packet.Payload, typeof({method.MsgFullName})) is {method.MsgFullName} msg) {{");
                    sb.AppendLine($"                    mod_{mod.SafeVarName}.{method.MethodName}(session, msg);");
                    sb.AppendLine($"                }} else {{");
                    sb.AppendLine(
                        $"                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError(\"AutoRegistry\", $\"反序列化失败: {method.MsgFullName}, MsgId: {method.MsgId}\");");
                    sb.AppendLine($"                }}");
                    sb.AppendLine($"            }});");
                }
            }

            foreach (var comp in serverComps)
            {
                sb.AppendLine($"            ServerRoomFactory.Register({comp.Id}, () => new {comp.FullName}(serverApp));");
            }

            sb.AppendLine("        }");
            sb.AppendLine("");

            sb.AppendLine(
                "        public static void BindServerComponent(StellarNet.Lite.Server.Core.RoomComponent comp, StellarNet.Lite.Server.Core.RoomDispatcher dispatcher, Func<byte[], Type, object> deserializeFunc)");
            sb.AppendLine("        {");
            sb.AppendLine("            switch (comp)");
            sb.AppendLine("            {");
            foreach (var comp in serverComps)
            {
                if (comp.Methods.Count == 0) continue;
                sb.AppendLine($"                case {comp.FullName} c_{comp.Id}:");
                foreach (var method in comp.Methods)
                {
                    sb.AppendLine($"                    dispatcher.Register({method.MsgId}, (session, packet) => {{");
                    sb.AppendLine($"                        if (deserializeFunc(packet.Payload, typeof({method.MsgFullName})) is {method.MsgFullName} msg) {{");
                    sb.AppendLine($"                            c_{comp.Id}.{method.MethodName}(session, msg);");
                    sb.AppendLine($"                        }} else {{");
                    sb.AppendLine(
                        $"                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError(\"AutoRegistry\", $\"反序列化失败: {method.MsgFullName}, MsgId: {method.MsgId}\");");
                    sb.AppendLine($"                        }}");
                    sb.AppendLine($"                    }});");
                }

                sb.AppendLine("                    break;");
            }

            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("");

            sb.AppendLine("        public static void RegisterClient(ClientApp clientApp, Func<byte[], Type, object> deserializeFunc)");
            sb.AppendLine("        {");
            sb.AppendLine("            ClientRoomFactory.Clear();");
            foreach (var mod in clientMods)
            {
                sb.AppendLine($"            var mod_{mod.SafeVarName} = new {mod.FullName}(clientApp);");
                foreach (var method in mod.Methods)
                {
                    sb.AppendLine($"            clientApp.GlobalDispatcher.Register({method.MsgId}, (packet) => {{");
                    sb.AppendLine($"                if (deserializeFunc(packet.Payload, typeof({method.MsgFullName})) is {method.MsgFullName} msg) {{");
                    sb.AppendLine($"                    mod_{mod.SafeVarName}.{method.MethodName}(msg);");
                    sb.AppendLine($"                }} else {{");
                    sb.AppendLine(
                        $"                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError(\"AutoRegistry\", $\"反序列化失败: {method.MsgFullName}, MsgId: {method.MsgId}\");");
                    sb.AppendLine($"                }}");
                    sb.AppendLine($"            }});");
                }
            }

            foreach (var comp in clientComps)
            {
                sb.AppendLine($"            ClientRoomFactory.Register({comp.Id}, () => new {comp.FullName}(clientApp));");
            }

            sb.AppendLine("        }");
            sb.AppendLine("");

            sb.AppendLine(
                "        public static void BindClientComponent(StellarNet.Lite.Client.Core.ClientRoomComponent comp, StellarNet.Lite.Client.Core.ClientRoomDispatcher dispatcher, Func<byte[], Type, object> deserializeFunc)");
            sb.AppendLine("        {");
            sb.AppendLine("            switch (comp)");
            sb.AppendLine("            {");
            foreach (var comp in clientComps)
            {
                if (comp.Methods.Count == 0) continue;
                sb.AppendLine($"                case {comp.FullName} c_{comp.Id}:");
                foreach (var method in comp.Methods)
                {
                    sb.AppendLine($"                    dispatcher.Register({method.MsgId}, (packet) => {{");
                    sb.AppendLine($"                        if (deserializeFunc(packet.Payload, typeof({method.MsgFullName})) is {method.MsgFullName} msg) {{");
                    sb.AppendLine($"                            c_{comp.Id}.{method.MethodName}(msg);");
                    sb.AppendLine($"                        }} else {{");
                    sb.AppendLine(
                        $"                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError(\"AutoRegistry\", $\"反序列化失败: {method.MsgFullName}, MsgId: {method.MsgId}\");");
                    sb.AppendLine($"                        }}");
                    sb.AppendLine($"                    }});");
                }

                sb.AppendLine("                    break;");
            }

            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return WriteToFileIfChanged(RegistryOutputPath, sb.ToString());
        }

        private static bool WriteToFileIfChanged(string path, string newContent)
        {
            string oldContent = string.Empty;
            if (File.Exists(path)) oldContent = File.ReadAllText(path);

            if (newContent != oldContent)
            {
                string directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(path, newContent, Encoding.UTF8);
                return true;
            }

            return false;
        }
    }
}
#endif