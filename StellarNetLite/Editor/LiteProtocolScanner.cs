#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEditor;

namespace StellarNet.Lite.Editor
{
    [InitializeOnLoad]
    public static class LiteProtocolScanner
    {
        private const string ProtocolOutputPath = "Assets/StellarNetLite/Runtime/Shared/Protocol/Const/MsgIdConst.cs";
        private const string ComponentOutputPath = "Assets/StellarNetLite/Runtime/Shared/Protocol/Const/ComponentIdConst.cs";
        private const string RegistryOutputPath = "Assets/StellarNetLite/Runtime/Shared/Binders/AutoRegistry.cs";
        private const string MessageMetaRegistryOutputPath = "Assets/StellarNetLite/Runtime/Shared/Protocol/Const/AutoMessageMetaRegistry.cs";

        private sealed class MethodMeta
        {
            public string MethodName;
            public string MsgFullName;
            public int MsgId;
        }

        private sealed class ClassMeta
        {
            public int Id;
            public string Name;
            public string SafeVarName;
            public string FullName;
            public string DisplayName;
            public readonly List<MethodMeta> Methods = new List<MethodMeta>();
        }

        private sealed class ProtocolMeta
        {
            public int Id;
            public string SafeName;
            public string FullTypeName;
            public NetScope Scope;
            public NetDir Dir;
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

        private static readonly Dictionary<Type, string> BuiltInTypeNames = new Dictionary<Type, string>
        {
            { typeof(void), "void" },
            { typeof(bool), "bool" },
            { typeof(byte), "byte" },
            { typeof(sbyte), "sbyte" },
            { typeof(char), "char" },
            { typeof(decimal), "decimal" },
            { typeof(double), "double" },
            { typeof(float), "float" },
            { typeof(int), "int" },
            { typeof(uint), "uint" },
            { typeof(long), "long" },
            { typeof(ulong), "ulong" },
            { typeof(object), "object" },
            { typeof(short), "short" },
            { typeof(ushort), "ushort" },
            { typeof(string), "string" }
        };

        private static void RunScanAndGenerate()
        {
            bool protocolChanged = ScanAndGenerateProtocols();
            bool componentChanged = ScanAndGenerateComponentsAndRegistry();

            if (!protocolChanged && !componentChanged)
            {
                return;
            }

            AssetDatabase.Refresh();
            NetLogger.LogInfo("LiteProtocolScanner", "自动装配代码已重新生成并应用。");
        }

        private static string GetCSharpTypeName(Type type)
        {
            if (type == null)
            {
                return "void";
            }

            if (type.IsByRef)
            {
                Type elementType = type.GetElementType();
                return "ref " + GetCSharpTypeName(elementType);
            }

            if (type.IsPointer)
            {
                Type elementType = type.GetElementType();
                return GetCSharpTypeName(elementType) + "*";
            }

            if (type.IsGenericParameter)
            {
                return type.Name;
            }

            if (BuiltInTypeNames.TryGetValue(type, out string builtInName))
            {
                return builtInName;
            }

            if (type.IsArray)
            {
                int rank = type.GetArrayRank();
                string commas = new string(',', rank - 1);
                Type elementType = type.GetElementType();
                return GetCSharpTypeName(elementType) + $"[{commas}]";
            }

            if (type.IsGenericType)
            {
                string genericName = type.GetGenericTypeDefinition().FullName;
                if (!string.IsNullOrEmpty(genericName))
                {
                    int tickIndex = genericName.IndexOf('`');
                    if (tickIndex >= 0)
                    {
                        genericName = genericName.Substring(0, tickIndex);
                    }

                    genericName = genericName.Replace('+', '.');
                }
                else
                {
                    genericName = type.Name;
                    int tickIndex = genericName.IndexOf('`');
                    if (tickIndex >= 0)
                    {
                        genericName = genericName.Substring(0, tickIndex);
                    }
                }

                Type[] genericArgs = type.GetGenericArguments();
                string[] argNames = new string[genericArgs.Length];
                for (int i = 0; i < genericArgs.Length; i++)
                {
                    argNames[i] = GetCSharpTypeName(genericArgs[i]);
                }

                return $"{genericName}<{string.Join(", ", argNames)}>";
            }

            if (!string.IsNullOrEmpty(type.FullName))
            {
                return type.FullName.Replace('+', '.');
            }

            return type.Name;
        }

        private static string SanitizeIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "Unknown";
            }

            string safe = Regex.Replace(name, @"[^a-zA-Z0-9_]", "_");
            if (string.IsNullOrEmpty(safe))
            {
                return "Unknown";
            }

            if (char.IsDigit(safe[0]))
            {
                safe = "_" + safe;
            }

            return safe;
        }

        private static string EscapeStringLiteral(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static bool ScanAndGenerateProtocols()
        {
            var types = TypeCache.GetTypesWithAttribute<NetMsgAttribute>();
            var protocolList = new List<ProtocolMeta>();
            var idMap = new Dictionary<int, string>();
            var safeNameMap = new Dictionary<string, string>();
            var typeNameMap = new Dictionary<string, string>();
            bool hasConflict = false;

            foreach (Type type in types)
            {
                if (type == null)
                {
                    continue;
                }

                NetMsgAttribute attr = type.GetCustomAttribute<NetMsgAttribute>();
                if (attr == null)
                {
                    continue;
                }

                string fullTypeName = GetCSharpTypeName(type);
                string safeName = SanitizeIdentifier(type.Name);

                if (idMap.TryGetValue(attr.Id, out string existingTypeName))
                {
                    NetLogger.LogError(
                        "LiteProtocolScanner",
                        $"协议 ID 冲突: MsgId:{attr.Id}, TypeA:{existingTypeName}, TypeB:{fullTypeName}");
                    hasConflict = true;
                    continue;
                }

                if (safeNameMap.TryGetValue(safeName, out string existingSafeTypeName))
                {
                    NetLogger.LogError(
                        "LiteProtocolScanner",
                        $"协议常量名冲突: ConstName:{safeName}, TypeA:{existingSafeTypeName}, TypeB:{fullTypeName}");
                    hasConflict = true;
                    continue;
                }

                if (typeNameMap.ContainsKey(fullTypeName))
                {
                    NetLogger.LogError(
                        "LiteProtocolScanner",
                        $"协议类型名冲突: Type:{fullTypeName}, MsgId:{attr.Id}");
                    hasConflict = true;
                    continue;
                }

                idMap.Add(attr.Id, fullTypeName);
                safeNameMap.Add(safeName, fullTypeName);
                typeNameMap.Add(fullTypeName, fullTypeName);

                protocolList.Add(new ProtocolMeta
                {
                    Id = attr.Id,
                    SafeName = safeName,
                    FullTypeName = fullTypeName,
                    Scope = attr.Scope,
                    Dir = attr.Dir
                });
            }

            if (hasConflict)
            {
                return false;
            }

            bool constChanged = GenerateProtocolConstFile(protocolList);
            bool metaChanged = GenerateMessageMetaRegistryFile(protocolList);
            return constChanged || metaChanged;
        }

        private static bool ScanAndGenerateComponentsAndRegistry()
        {
            var serverComps = new List<ClassMeta>();
            var clientComps = new List<ClassMeta>();
            var serverMods = new List<ClassMeta>();
            var clientMods = new List<ClassMeta>();

            var serverGlobalMsgIds = new Dictionary<int, string>();
            var serverRoomMsgIds = new Dictionary<int, string>();
            var clientGlobalMsgIds = new Dictionary<int, string>();
            var clientRoomMsgIds = new Dictionary<int, string>();

            bool hasFatalError = false;

            var roomCompTypes = TypeCache.GetTypesWithAttribute<RoomComponentAttribute>();
            foreach (Type type in roomCompTypes)
            {
                if (type == null)
                {
                    continue;
                }

                RoomComponentAttribute attr = type.GetCustomAttribute<RoomComponentAttribute>();
                if (attr == null)
                {
                    continue;
                }

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

                if (!isServer && !isClient)
                {
                    NetLogger.LogError(
                        "LiteProtocolScanner",
                        $"组件扫描失败: 标记了 [RoomComponent] 但未继承合法基类, Type:{meta.FullName}, ComponentId:{meta.Id}, Name:{meta.Name}");
                    hasFatalError = true;
                    continue;
                }

                if (isServer && isClient)
                {
                    NetLogger.LogError(
                        "LiteProtocolScanner",
                        $"组件扫描失败: 类型同时匹配 Server/Client 组件基类, Type:{meta.FullName}, ComponentId:{meta.Id}, Name:{meta.Name}");
                    hasFatalError = true;
                    continue;
                }

                bool methodsSafe = ScanMethods(type, isServer, meta, isServer ? serverRoomMsgIds : clientRoomMsgIds);
                if (!methodsSafe)
                {
                    hasFatalError = true;
                }

                if (isServer)
                {
                    serverComps.Add(meta);
                }
                else
                {
                    clientComps.Add(meta);
                }
            }

            var serverModTypes = TypeCache.GetTypesWithAttribute<ServerModuleAttribute>();
            foreach (Type type in serverModTypes)
            {
                if (type == null)
                {
                    continue;
                }

                ServerModuleAttribute attr = type.GetCustomAttribute<ServerModuleAttribute>();
                if (attr == null)
                {
                    continue;
                }

                var meta = new ClassMeta
                {
                    Name = attr.Name,
                    SafeVarName = SanitizeIdentifier(GetCSharpTypeName(type)),
                    FullName = GetCSharpTypeName(type),
                    DisplayName = attr.DisplayName
                };

                if (!ScanMethods(type, true, meta, serverGlobalMsgIds))
                {
                    hasFatalError = true;
                }

                serverMods.Add(meta);
            }

            var clientModTypes = TypeCache.GetTypesWithAttribute<ClientModuleAttribute>();
            foreach (Type type in clientModTypes)
            {
                if (type == null)
                {
                    continue;
                }

                ClientModuleAttribute attr = type.GetCustomAttribute<ClientModuleAttribute>();
                if (attr == null)
                {
                    continue;
                }

                var meta = new ClassMeta
                {
                    Name = attr.Name,
                    SafeVarName = SanitizeIdentifier(GetCSharpTypeName(type)),
                    FullName = GetCSharpTypeName(type),
                    DisplayName = attr.DisplayName
                };

                if (!ScanMethods(type, false, meta, clientGlobalMsgIds))
                {
                    hasFatalError = true;
                }

                clientMods.Add(meta);
            }

            if (!ValidateComponentMetaConsistency(serverComps, clientComps))
            {
                hasFatalError = true;
            }

            if (!ValidateComponentConstNameConflicts(serverComps, clientComps))
            {
                hasFatalError = true;
            }

            if (hasFatalError)
            {
                return false;
            }

            List<ClassMeta> mergedComps = MergeUniqueComponentsById(serverComps, clientComps);
            bool constChanged = GenerateComponentConstFile(mergedComps);
            bool registryChanged = GenerateAutoRegistryFile(serverComps, clientComps, serverMods, clientMods, mergedComps);
            return constChanged || registryChanged;
        }

        private static bool ScanMethods(Type type, bool isServer, ClassMeta meta, Dictionary<int, string> msgIdTracker)
        {
            if (type == null)
            {
                NetLogger.LogError("LiteProtocolScanner", "方法扫描失败: type 为空");
                return false;
            }

            if (meta == null)
            {
                NetLogger.LogError("LiteProtocolScanner", $"方法扫描失败: meta 为空, Type:{GetCSharpTypeName(type)}");
                return false;
            }

            if (msgIdTracker == null)
            {
                NetLogger.LogError("LiteProtocolScanner", $"方法扫描失败: msgIdTracker 为空, Type:{GetCSharpTypeName(type)}");
                return false;
            }

            MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            bool isSafe = true;

            foreach (MethodInfo method in methods)
            {
                if (method.GetCustomAttribute<NetHandlerAttribute>() == null)
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                Type msgType = null;

                if (isServer)
                {
                    if (parameters.Length != 2 || parameters[0].ParameterType != typeof(StellarNet.Lite.Server.Core.Session))
                    {
                        NetLogger.LogError(
                            "LiteProtocolScanner",
                            $"NetHandler 签名非法: 服务端方法必须为 (Session, Msg), Type:{meta.FullName}, Method:{method.Name}, ParamCount:{parameters.Length}");
                        isSafe = false;
                        continue;
                    }

                    msgType = parameters[1].ParameterType;
                }
                else
                {
                    if (parameters.Length != 1)
                    {
                        NetLogger.LogError(
                            "LiteProtocolScanner",
                            $"NetHandler 签名非法: 客户端方法必须为 (Msg), Type:{meta.FullName}, Method:{method.Name}, ParamCount:{parameters.Length}");
                        isSafe = false;
                        continue;
                    }

                    msgType = parameters[0].ParameterType;
                }

                if (msgType == null)
                {
                    NetLogger.LogError(
                        "LiteProtocolScanner",
                        $"NetHandler 扫描失败: 消息类型为空, Type:{meta.FullName}, Method:{method.Name}");
                    isSafe = false;
                    continue;
                }

                NetMsgAttribute netMsgAttr = msgType.GetCustomAttribute<NetMsgAttribute>();
                if (netMsgAttr == null)
                {
                    NetLogger.LogError(
                        "LiteProtocolScanner",
                        $"NetHandler 扫描失败: 参数类型缺失 [NetMsg], Type:{meta.FullName}, Method:{method.Name}, MsgType:{GetCSharpTypeName(msgType)}");
                    isSafe = false;
                    continue;
                }

                string currentMethodName = $"{type.FullName}.{method.Name}";
                if (msgIdTracker.TryGetValue(netMsgAttr.Id, out string existingMethod))
                {
                    NetLogger.LogError(
                        "LiteProtocolScanner",
                        $"NetHandler MsgId 冲突: MsgId:{netMsgAttr.Id}, MethodA:{existingMethod}, MethodB:{currentMethodName}");
                    isSafe = false;
                    continue;
                }

                msgIdTracker.Add(netMsgAttr.Id, currentMethodName);
                meta.Methods.Add(new MethodMeta
                {
                    MethodName = method.Name,
                    MsgFullName = GetCSharpTypeName(msgType),
                    MsgId = netMsgAttr.Id
                });
            }

            return isSafe;
        }

        private static bool ValidateComponentMetaConsistency(List<ClassMeta> serverComps, List<ClassMeta> clientComps)
        {
            bool isSafe = true;
            var serverMap = new Dictionary<int, ClassMeta>();

            for (int i = 0; i < serverComps.Count; i++)
            {
                ClassMeta meta = serverComps[i];
                if (serverMap.TryGetValue(meta.Id, out ClassMeta existing))
                {
                    NetLogger.LogError(
                        "LiteProtocolScanner",
                        $"服务端组件 ID 冲突: ComponentId:{meta.Id}, TypeA:{existing.FullName}, TypeB:{meta.FullName}");
                    isSafe = false;
                    continue;
                }

                serverMap.Add(meta.Id, meta);
            }

            var clientMap = new Dictionary<int, ClassMeta>();
            for (int i = 0; i < clientComps.Count; i++)
            {
                ClassMeta meta = clientComps[i];
                if (clientMap.TryGetValue(meta.Id, out ClassMeta existing))
                {
                    NetLogger.LogError(
                        "LiteProtocolScanner",
                        $"客户端组件 ID 冲突: ComponentId:{meta.Id}, TypeA:{existing.FullName}, TypeB:{meta.FullName}");
                    isSafe = false;
                    continue;
                }

                clientMap.Add(meta.Id, meta);
            }

            foreach (KeyValuePair<int, ClassMeta> kv in serverMap)
            {
                if (!clientMap.TryGetValue(kv.Key, out ClassMeta clientMeta))
                {
                    continue;
                }

                ClassMeta serverMeta = kv.Value;
                if (!string.Equals(serverMeta.Name, clientMeta.Name, StringComparison.Ordinal) ||
                    !string.Equals(serverMeta.DisplayName, clientMeta.DisplayName, StringComparison.Ordinal))
                {
                    NetLogger.LogError(
                        "LiteProtocolScanner",
                        $"组件双端定义不一致: ComponentId:{kv.Key}, ServerName:{serverMeta.Name}, ClientName:{clientMeta.Name}, ServerDisplay:{serverMeta.DisplayName}, ClientDisplay:{clientMeta.DisplayName}");
                    isSafe = false;
                }
            }

            return isSafe;
        }

        private static bool ValidateComponentConstNameConflicts(List<ClassMeta> serverComps, List<ClassMeta> clientComps)
        {
            bool isSafe = true;
            List<ClassMeta> mergedComps = MergeUniqueComponentsById(serverComps, clientComps);
            var constNameMap = new Dictionary<string, ClassMeta>();

            for (int i = 0; i < mergedComps.Count; i++)
            {
                ClassMeta meta = mergedComps[i];
                string constName = SanitizeIdentifier(meta.Name);

                if (constNameMap.TryGetValue(constName, out ClassMeta existingMeta))
                {
                    bool isSameLogicalComponent =
                        existingMeta.Id == meta.Id &&
                        string.Equals(existingMeta.Name, meta.Name, StringComparison.Ordinal);

                    if (isSameLogicalComponent)
                    {
                        continue;
                    }

                    NetLogger.LogError(
                        "LiteProtocolScanner",
                        $"组件常量名冲突: ConstName:{constName}, A:{existingMeta.FullName}(ComponentId:{existingMeta.Id}, Name:{existingMeta.Name}), B:{meta.FullName}(ComponentId:{meta.Id}, Name:{meta.Name})");
                    isSafe = false;
                    continue;
                }

                constNameMap.Add(constName, meta);
            }

            return isSafe;
        }

        private static List<ClassMeta> MergeUniqueComponentsById(List<ClassMeta> serverComps, List<ClassMeta> clientComps)
        {
            List<ClassMeta> allComps = serverComps.Concat(clientComps).OrderBy(c => c.Id).ToList();
            var merged = new List<ClassMeta>();
            var idMap = new Dictionary<int, ClassMeta>();

            for (int i = 0; i < allComps.Count; i++)
            {
                ClassMeta meta = allComps[i];
                if (meta == null)
                {
                    continue;
                }

                if (idMap.ContainsKey(meta.Id))
                {
                    continue;
                }

                idMap.Add(meta.Id, meta);
                merged.Add(meta);
            }

            return merged;
        }

        private static bool GenerateProtocolConstFile(List<ProtocolMeta> protocolList)
        {
            protocolList = protocolList.OrderBy(p => p.Id).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("// ========================================================");
            sb.AppendLine("// 自动生成的协议 ID 常量表。");
            sb.AppendLine("// ========================================================");
            sb.AppendLine("namespace StellarNet.Lite.Shared.Protocol");
            sb.AppendLine("{");
            sb.AppendLine("    public static class MsgIdConst");
            sb.AppendLine("    {");

            for (int i = 0; i < protocolList.Count; i++)
            {
                ProtocolMeta proto = protocolList[i];
                sb.AppendLine($"        public const int {proto.SafeName} = {proto.Id};");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return WriteToFileIfChanged(ProtocolOutputPath, sb.ToString());
        }

        private static bool GenerateMessageMetaRegistryFile(List<ProtocolMeta> protocolList)
        {
            protocolList = protocolList.OrderBy(p => p.Id).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("// ========================================================");
            sb.AppendLine("// 自动生成的协议元数据静态注册表。");
            sb.AppendLine("// 请勿手动修改！由 LiteProtocolScanner 自动生成。");
            sb.AppendLine("// ========================================================");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using StellarNet.Lite.Shared.Core;");
            sb.AppendLine();
            sb.AppendLine("namespace StellarNet.Lite.Shared.Protocol");
            sb.AppendLine("{");
            sb.AppendLine("    public static class AutoMessageMetaRegistry");
            sb.AppendLine("    {");
            sb.AppendLine("        public static readonly Dictionary<Type, NetMessageMeta> TypeToMeta = new Dictionary<Type, NetMessageMeta>");
            sb.AppendLine("        {");

            for (int i = 0; i < protocolList.Count; i++)
            {
                ProtocolMeta proto = protocolList[i];
                sb.AppendLine(
                    $"            {{ typeof({proto.FullTypeName}), new NetMessageMeta({proto.Id}, NetScope.{proto.Scope}, NetDir.{proto.Dir}) }},");
            }

            sb.AppendLine("        };");
            sb.AppendLine();
            sb.AppendLine("        public static readonly Dictionary<int, Type> MsgIdToType = new Dictionary<int, Type>");
            sb.AppendLine("        {");

            for (int i = 0; i < protocolList.Count; i++)
            {
                ProtocolMeta proto = protocolList[i];
                sb.AppendLine($"            {{ {proto.Id}, typeof({proto.FullTypeName}) }},");
            }

            sb.AppendLine("        };");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return WriteToFileIfChanged(MessageMetaRegistryOutputPath, sb.ToString());
        }

        private static bool GenerateComponentConstFile(List<ClassMeta> compList)
        {
            compList = compList.OrderBy(p => p.Id).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("// ========================================================");
            sb.AppendLine("// 自动生成的组件 ID 常量表。");
            sb.AppendLine("// ========================================================");
            sb.AppendLine("namespace StellarNet.Lite.Shared.Protocol");
            sb.AppendLine("{");
            sb.AppendLine("    public static class ComponentIdConst");
            sb.AppendLine("    {");

            for (int i = 0; i < compList.Count; i++)
            {
                ClassMeta comp = compList[i];
                sb.AppendLine($"        public const int {SanitizeIdentifier(comp.Name)} = {comp.Id};");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return WriteToFileIfChanged(ComponentOutputPath, sb.ToString());
        }

        private static bool GenerateAutoRegistryFile(
            List<ClassMeta> serverComps,
            List<ClassMeta> clientComps,
            List<ClassMeta> serverMods,
            List<ClassMeta> clientMods,
            List<ClassMeta> mergedComps)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// ========================================================");
            sb.AppendLine("// 自动生成的静态装配器。");
            sb.AppendLine("// 请勿手动修改！由 LiteProtocolScanner 自动生成。");
            sb.AppendLine("// ========================================================");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using StellarNet.Lite.Shared.Core;");
            sb.AppendLine("using StellarNet.Lite.Server.Core;");
            sb.AppendLine("using StellarNet.Lite.Client.Core;");
            sb.AppendLine("using StellarNet.Lite.Shared.Protocol;");
            sb.AppendLine();
            sb.AppendLine("namespace StellarNet.Lite.Shared.Binders");
            sb.AppendLine("{");
            sb.AppendLine("    public static class AutoRegistry");
            sb.AppendLine("    {");
            sb.AppendLine("        public static readonly List<RoomComponentMeta> RoomComponentMetaList = new List<RoomComponentMeta>");
            sb.AppendLine("        {");

            for (int i = 0; i < mergedComps.Count; i++)
            {
                ClassMeta comp = mergedComps[i];
                sb.AppendLine(
                    $"            new RoomComponentMeta {{ Id = {comp.Id}, Name = \"{EscapeStringLiteral(comp.Name)}\", DisplayName = \"{EscapeStringLiteral(comp.DisplayName)}\" }},");
            }

            sb.AppendLine("        };");
            sb.AppendLine();
            sb.AppendLine("        public static void RegisterServer(ServerApp serverApp, Func<byte[], int, int, Type, object> deserializeFunc)");
            sb.AppendLine("        {");
            sb.AppendLine("            ServerRoomFactory.Clear();");

            for (int i = 0; i < serverMods.Count; i++)
            {
                ClassMeta mod = serverMods[i];
                sb.AppendLine($"            var mod_{mod.SafeVarName} = new {mod.FullName}(serverApp);");

                for (int j = 0; j < mod.Methods.Count; j++)
                {
                    MethodMeta method = mod.Methods[j];
                    sb.AppendLine($"            serverApp.GlobalDispatcher.Register({method.MsgId}, (session, packet) => {{");
                    sb.AppendLine(
                        $"                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof({method.MsgFullName})) is {method.MsgFullName} msg) {{");
                    sb.AppendLine($"                    mod_{mod.SafeVarName}.{method.MethodName}(session, msg);");
                    sb.AppendLine("                }");
                    sb.AppendLine("                else");
                    sb.AppendLine("                {");
                    sb.AppendLine(
                        $"                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError(\"AutoRegistry\", $\"反序列化失败: {method.MsgFullName}, MsgId:{method.MsgId}\");");
                    sb.AppendLine("                }");
                    sb.AppendLine("            });");
                }
            }

            for (int i = 0; i < serverComps.Count; i++)
            {
                ClassMeta comp = serverComps[i];
                sb.AppendLine($"            ServerRoomFactory.Register({comp.Id}, () => new {comp.FullName}(serverApp));");
            }

            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine(
                "        public static void BindServerComponent(StellarNet.Lite.Server.Core.RoomComponent comp, StellarNet.Lite.Server.Core.RoomDispatcher dispatcher, Func<byte[], int, int, Type, object> deserializeFunc)");
            sb.AppendLine("        {");
            sb.AppendLine("            switch (comp)");
            sb.AppendLine("            {");

            for (int i = 0; i < serverComps.Count; i++)
            {
                ClassMeta comp = serverComps[i];
                if (comp.Methods.Count == 0)
                {
                    continue;
                }

                sb.AppendLine($"                case {comp.FullName} c_{comp.Id}:");
                for (int j = 0; j < comp.Methods.Count; j++)
                {
                    MethodMeta method = comp.Methods[j];
                    sb.AppendLine($"                    dispatcher.Register({method.MsgId}, (session, packet) => {{");
                    sb.AppendLine(
                        $"                        if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof({method.MsgFullName})) is {method.MsgFullName} msg) {{");
                    sb.AppendLine($"                            c_{comp.Id}.{method.MethodName}(session, msg);");
                    sb.AppendLine("                        }");
                    sb.AppendLine("                        else");
                    sb.AppendLine("                        {");
                    sb.AppendLine(
                        $"                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError(\"AutoRegistry\", $\"反序列化失败: {method.MsgFullName}, MsgId:{method.MsgId}\");");
                    sb.AppendLine("                        }");
                    sb.AppendLine("                    });");
                }

                sb.AppendLine("                    break;");
            }

            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public static void RegisterClient(ClientApp clientApp, Func<byte[], int, int, Type, object> deserializeFunc)");
            sb.AppendLine("        {");
            sb.AppendLine("            ClientRoomFactory.Clear();");

            for (int i = 0; i < clientMods.Count; i++)
            {
                ClassMeta mod = clientMods[i];
                sb.AppendLine($"            var mod_{mod.SafeVarName} = new {mod.FullName}(clientApp);");

                for (int j = 0; j < mod.Methods.Count; j++)
                {
                    MethodMeta method = mod.Methods[j];
                    sb.AppendLine($"            clientApp.GlobalDispatcher.Register({method.MsgId}, (packet) => {{");
                    sb.AppendLine(
                        $"                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof({method.MsgFullName})) is {method.MsgFullName} msg) {{");
                    sb.AppendLine($"                    mod_{mod.SafeVarName}.{method.MethodName}(msg);");
                    sb.AppendLine("                }");
                    sb.AppendLine("                else");
                    sb.AppendLine("                {");
                    sb.AppendLine(
                        $"                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError(\"AutoRegistry\", $\"反序列化失败: {method.MsgFullName}, MsgId:{method.MsgId}\");");
                    sb.AppendLine("                }");
                    sb.AppendLine("            });");
                }
            }

            for (int i = 0; i < clientComps.Count; i++)
            {
                ClassMeta comp = clientComps[i];
                sb.AppendLine($"            ClientRoomFactory.Register({comp.Id}, () => new {comp.FullName}(clientApp));");
            }

            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine(
                "        public static void BindClientComponent(StellarNet.Lite.Client.Core.ClientRoomComponent comp, StellarNet.Lite.Client.Core.ClientRoomDispatcher dispatcher, Func<byte[], int, int, Type, object> deserializeFunc)");
            sb.AppendLine("        {");
            sb.AppendLine("            switch (comp)");
            sb.AppendLine("            {");

            for (int i = 0; i < clientComps.Count; i++)
            {
                ClassMeta comp = clientComps[i];
                if (comp.Methods.Count == 0)
                {
                    continue;
                }

                sb.AppendLine($"                case {comp.FullName} c_{comp.Id}:");
                for (int j = 0; j < comp.Methods.Count; j++)
                {
                    MethodMeta method = comp.Methods[j];
                    sb.AppendLine($"                    dispatcher.Register({method.MsgId}, (packet) => {{");
                    sb.AppendLine(
                        $"                        if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof({method.MsgFullName})) is {method.MsgFullName} msg) {{");
                    sb.AppendLine($"                            c_{comp.Id}.{method.MethodName}(msg);");
                    sb.AppendLine("                        }");
                    sb.AppendLine("                        else");
                    sb.AppendLine("                        {");
                    sb.AppendLine(
                        $"                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError(\"AutoRegistry\", $\"反序列化失败: {method.MsgFullName}, MsgId:{method.MsgId}\");");
                    sb.AppendLine("                        }");
                    sb.AppendLine("                    });");
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
            if (string.IsNullOrEmpty(path))
            {
                NetLogger.LogError("LiteProtocolScanner", "写入失败: path 为空");
                return false;
            }

            if (newContent == null)
            {
                NetLogger.LogError("LiteProtocolScanner", $"写入失败: newContent 为空, Path:{path}");
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
                NetLogger.LogError("LiteProtocolScanner", $"写入失败: 目录为空, Path:{path}");
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