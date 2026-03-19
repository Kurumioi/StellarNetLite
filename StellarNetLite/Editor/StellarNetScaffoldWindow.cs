#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEditor;
using UnityEngine;

namespace StellarNet.Lite.Editor.Tools
{
    /// <summary>
    /// StellarNet Lite 业务脚手架生成器。
    /// 职责：按既定目录结构生成业务协议、组件与模块模板，并在任何非法状态下立即阻断。
    /// </summary>
    public sealed class StellarNetScaffoldWindow : EditorWindow
    {
        #region ================= 数据结构与状态 =================

        private enum ModuleType
        {
            RoomComponent,
            GlobalModule
        }

        private ModuleType _currentType = ModuleType.RoomComponent;
        private string _moduleName = "NewFeature";
        private string _displayName = "新功能模块";
        private int _startProtocolId = 10000;
        private int _componentId = 10;
        private string _authorName = "Developer";
        private string _outputRootPath = "Assets/Scripts/Game";
        private string _baseNamespace = "Game";
        private bool _genProtocol = true;
        private bool _genServer = true;
        private bool _genClient = true;

        private GUIStyle _headerStyle;
        private GUIStyle _sectionStyle;
        private bool _stylesInitialized;

        #endregion

        [MenuItem("StellarNet/Lite 业务脚手架 (Scaffold)")]
        public static void ShowWindow()
        {
            var window = GetWindow<StellarNetScaffoldWindow>("StellarNet 脚手架");
            window.minSize = new Vector2(500, 600);
            window.Show();
        }

        #region ================= UI 绘制逻辑 =================

        private void OnGUI()
        {
            InitializeStyles();

            GUILayout.Space(10);
            GUILayout.Label("StellarNet Lite 业务代码生成器", _headerStyle);
            EditorGUILayout.HelpBox("输入模块名称与起始协议 ID，工具将自动在指定目录下生成符合规范的业务代码模板。", MessageType.Info);

            GUILayout.Space(10);
            DrawMainPanel();
            GUILayout.FlexibleSpace();
            DrawActionBar();
        }

        private void InitializeStyles()
        {
            if (_stylesInitialized)
            {
                return;
            }

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };

            _sectionStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.3f, 0.6f, 0.9f) }
            };

            _stylesInitialized = true;
        }

        private void DrawMainPanel()
        {
            EditorGUILayout.BeginVertical("box");

            GUILayout.Space(5);
            GUILayout.Label("目录与命名空间配置", _sectionStyle);
            GUILayout.Space(5);

            _outputRootPath = EditorGUILayout.TextField("输出根目录 (Root Path):", _outputRootPath);
            _baseNamespace = EditorGUILayout.TextField("业务命名空间 (Namespace):", _baseNamespace);

            GUILayout.Space(15);
            GUILayout.Label("业务模块配置", _sectionStyle);
            GUILayout.Space(5);

            _currentType = (ModuleType)EditorGUILayout.EnumPopup("模块类型 (Type):", _currentType);

            EditorGUI.BeginChangeCheck();
            _moduleName = EditorGUILayout.TextField("模块类名 (Name):", _moduleName);
            if (EditorGUI.EndChangeCheck())
            {
                _moduleName = Regex.Replace(_moduleName, @"[^a-zA-Z0-9_]", "");
            }

            _displayName = EditorGUILayout.TextField("中文展示名 (DisplayName):", _displayName);
            _startProtocolId = EditorGUILayout.IntField("起始协议 ID:", _startProtocolId);

            if (_currentType == ModuleType.RoomComponent)
            {
                _componentId = EditorGUILayout.IntField("组件 ID (ComponentId):", _componentId);
            }

            _authorName = EditorGUILayout.TextField("开发者 (Author):", _authorName);

            GUILayout.Space(15);
            GUILayout.Label("生成选项", _sectionStyle);
            GUILayout.Space(5);

            _genProtocol = EditorGUILayout.Toggle("生成 Shared 协议定义", _genProtocol);
            _genServer = EditorGUILayout.Toggle("生成 Server 端逻辑", _genServer);
            _genClient = EditorGUILayout.Toggle("生成 Client 端逻辑", _genClient);

            EditorGUILayout.EndVertical();
        }

        private void DrawActionBar()
        {
            EditorGUILayout.BeginVertical("box");
            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.2f);

            if (GUILayout.Button("一键生成业务代码 (Generate)", GUILayout.Height(40)))
            {
                ExecuteGeneration();
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region ================= 代码生成核心逻辑 =================

        private void ExecuteGeneration()
        {
            if (!ValidateInputs())
            {
                return;
            }

            bool success = true;

            if (_genProtocol)
            {
                success &= GenerateSharedProtocol();
            }

            if (success)
            {
                if (_currentType == ModuleType.RoomComponent)
                {
                    if (_genServer)
                    {
                        success &= GenerateServerRoomComponent();
                    }

                    if (success && _genClient)
                    {
                        success &= GenerateClientRoomComponent();
                    }
                }
                else
                {
                    if (_genServer)
                    {
                        success &= GenerateServerGlobalModule();
                    }

                    if (success && _genClient)
                    {
                        success &= GenerateClientGlobalModule();
                    }
                }
            }

            if (!success)
            {
                return;
            }

            AssetDatabase.Refresh();
            NetLogger.LogInfo("Scaffold", $"业务模块生成成功: Module:{_moduleName}, OutputRoot:{_outputRootPath}");
            EditorUtility.DisplayDialog(
                "成功",
                $"业务模块 {_moduleName} 生成完毕！\n\n请点击顶部菜单：\n[StellarNet/Lite 强制重新生成协议与组件常量表]\n以完成自动装配。",
                "确定");
        }

        private bool ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(_moduleName))
            {
                EditorUtility.DisplayDialog("错误", "模块名称不能为空。", "确定");
                return false;
            }

            if (!char.IsLetter(_moduleName[0]) && _moduleName[0] != '_')
            {
                EditorUtility.DisplayDialog("错误", "模块名称必须以字母或下划线开头。", "确定");
                return false;
            }

            if (_startProtocolId <= 0)
            {
                EditorUtility.DisplayDialog("错误", "协议 ID 必须大于 0。", "确定");
                return false;
            }

            if (_currentType == ModuleType.RoomComponent && _componentId <= 0)
            {
                EditorUtility.DisplayDialog("错误", "组件 ID 必须大于 0。", "确定");
                return false;
            }

            if (string.IsNullOrWhiteSpace(_outputRootPath))
            {
                EditorUtility.DisplayDialog("错误", "输出根目录不能为空。", "确定");
                return false;
            }

            if (string.IsNullOrWhiteSpace(_baseNamespace))
            {
                EditorUtility.DisplayDialog("错误", "业务命名空间不能为空。", "确定");
                return false;
            }

            if (!_genProtocol && !_genServer && !_genClient)
            {
                EditorUtility.DisplayDialog("错误", "至少需要勾选一个生成选项。", "确定");
                return false;
            }

            return true;
        }

        private static string EscapeStringLiteral(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private bool GenerateSharedProtocol()
        {
            string path = $"{_outputRootPath}/Shared/Protocol/{_moduleName}Protocols.cs";
            string scope = _currentType == ModuleType.RoomComponent ? "NetScope.Room" : "NetScope.Global";
            string ns = $"{_baseNamespace}.Shared.Protocol";

            var sb = new StringBuilder();
            sb.AppendLine("// ========================================================");
            sb.AppendLine($"// 业务模块: {_moduleName}");
            sb.AppendLine($"// 作者: {EscapeStringLiteral(_authorName)}");
            sb.AppendLine($"// 时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("// ========================================================");
            sb.AppendLine("using StellarNet.Lite.Shared.Core;");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.AppendLine($"    [NetMsg({_startProtocolId}, {scope}, NetDir.C2S)]");
            sb.AppendLine($"    public sealed class C2S_{_moduleName}Req");
            sb.AppendLine("    {");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    [NetMsg({_startProtocolId + 1}, {scope}, NetDir.S2C)]");
            sb.AppendLine($"    public sealed class S2C_{_moduleName}Sync");
            sb.AppendLine("    {");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return WriteToFile(path, sb.ToString());
        }

        private bool GenerateServerRoomComponent()
        {
            string path = $"{_outputRootPath}/Server/Components/Server{_moduleName}Component.cs";
            string ns = $"{_baseNamespace}.Server.Components";
            string protocolNs = $"{_baseNamespace}.Shared.Protocol";

            var sb = new StringBuilder();
            sb.AppendLine("using StellarNet.Lite.Shared.Core;");
            sb.AppendLine("using StellarNet.Lite.Server.Core;");
            sb.AppendLine("using StellarNet.Lite.Shared.Infrastructure;");
            sb.AppendLine($"using {protocolNs};");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.AppendLine($"    [RoomComponent({_componentId}, \"{EscapeStringLiteral(_moduleName)}\", \"{EscapeStringLiteral(_displayName)}\")]");
            sb.AppendLine($"    public sealed class Server{_moduleName}Component : RoomComponent");
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly ServerApp _app;");
            sb.AppendLine();
            sb.AppendLine($"        public Server{_moduleName}Component(ServerApp app)");
            sb.AppendLine("        {");
            sb.AppendLine("            _app = app;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public override void OnInit()");
            sb.AppendLine("        {");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        [NetHandler]");
            sb.AppendLine($"        public void OnC2S_{_moduleName}Req(Session session, C2S_{_moduleName}Req msg)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (session == null || msg == null)");
            sb.AppendLine("            {");
            sb.AppendLine(
                $"                NetLogger.LogError(\"Server{_moduleName}Component\", $\"收到非法请求: Session 或 Msg 为空, RoomId:{{Room?.RoomId ?? \\\"-\\\"}}, SessionId:{{session?.SessionId ?? \\\"-\\\"}}\");");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            if (Room == null)");
            sb.AppendLine("            {");
            sb.AppendLine($"                NetLogger.LogError(\"Server{_moduleName}Component\", $\"处理请求失败: Room 为空, SessionId:{{session.SessionId}}\");");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine($"            var syncMsg = new S2C_{_moduleName}Sync();");
            sb.AppendLine("            Room.BroadcastMessage(syncMsg);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return WriteToFile(path, sb.ToString());
        }

        private bool GenerateClientRoomComponent()
        {
            string path = $"{_outputRootPath}/Client/Components/Client{_moduleName}Component.cs";
            string ns = $"{_baseNamespace}.Client.Components";
            string protocolNs = $"{_baseNamespace}.Shared.Protocol";

            var sb = new StringBuilder();
            sb.AppendLine("using StellarNet.Lite.Shared.Core;");
            sb.AppendLine("using StellarNet.Lite.Client.Core;");
            sb.AppendLine("using StellarNet.Lite.Shared.Infrastructure;");
            sb.AppendLine($"using {protocolNs};");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.AppendLine($"    [RoomComponent({_componentId}, \"{EscapeStringLiteral(_moduleName)}\", \"{EscapeStringLiteral(_displayName)}\")]");
            sb.AppendLine($"    public sealed class Client{_moduleName}Component : ClientRoomComponent");
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly ClientApp _app;");
            sb.AppendLine();
            sb.AppendLine($"        public Client{_moduleName}Component(ClientApp app)");
            sb.AppendLine("        {");
            sb.AppendLine("            _app = app;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public override void OnInit()");
            sb.AppendLine("        {");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        [NetHandler]");
            sb.AppendLine($"        public void OnS2C_{_moduleName}Sync(S2C_{_moduleName}Sync msg)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (msg == null)");
            sb.AppendLine("            {");
            sb.AppendLine($"                NetLogger.LogError(\"Client{_moduleName}Component\", $\"收到非法同步包: Msg 为空, RoomId:{{Room?.RoomId ?? \\\"-\\\"}}\");");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            if (Room == null)");
            sb.AppendLine("            {");
            sb.AppendLine($"                NetLogger.LogError(\"Client{_moduleName}Component\", \"处理同步失败: Room 为空\");");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            Room.NetEventSystem.Broadcast(msg);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return WriteToFile(path, sb.ToString());
        }

        private bool GenerateServerGlobalModule()
        {
            string path = $"{_outputRootPath}/Server/Modules/Server{_moduleName}Module.cs";
            string ns = $"{_baseNamespace}.Server.Modules";
            string protocolNs = $"{_baseNamespace}.Shared.Protocol";

            var sb = new StringBuilder();
            sb.AppendLine("using StellarNet.Lite.Shared.Core;");
            sb.AppendLine("using StellarNet.Lite.Server.Core;");
            sb.AppendLine("using StellarNet.Lite.Shared.Infrastructure;");
            sb.AppendLine($"using {protocolNs};");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.AppendLine($"    [ServerModule(\"{EscapeStringLiteral(_moduleName)}\", \"{EscapeStringLiteral(_displayName)}\")]");
            sb.AppendLine($"    public sealed class Server{_moduleName}Module");
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly ServerApp _app;");
            sb.AppendLine();
            sb.AppendLine($"        public Server{_moduleName}Module(ServerApp app)");
            sb.AppendLine("        {");
            sb.AppendLine("            _app = app;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        [NetHandler]");
            sb.AppendLine($"        public void OnC2S_{_moduleName}Req(Session session, C2S_{_moduleName}Req msg)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (session == null || msg == null)");
            sb.AppendLine("            {");
            sb.AppendLine(
                $"                NetLogger.LogError(\"Server{_moduleName}Module\", $\"收到非法请求: Session 或 Msg 为空, RoomId:{{session?.CurrentRoomId ?? \\\"-\\\"}}, SessionId:{{session?.SessionId ?? \\\"-\\\"}}\");");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            if (_app == null)");
            sb.AppendLine("            {");
            sb.AppendLine($"                NetLogger.LogError(\"Server{_moduleName}Module\", $\"处理请求失败: _app 为空, SessionId:{{session.SessionId}}\");");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine($"            var syncMsg = new S2C_{_moduleName}Sync();");
            sb.AppendLine("            _app.SendMessageToSession(session, syncMsg);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return WriteToFile(path, sb.ToString());
        }

        private bool GenerateClientGlobalModule()
        {
            string path = $"{_outputRootPath}/Client/Modules/Client{_moduleName}Module.cs";
            string ns = $"{_baseNamespace}.Client.Modules";
            string protocolNs = $"{_baseNamespace}.Shared.Protocol";

            var sb = new StringBuilder();
            sb.AppendLine("using StellarNet.Lite.Shared.Core;");
            sb.AppendLine("using StellarNet.Lite.Client.Core;");
            sb.AppendLine("using StellarNet.Lite.Client.Core.Events;");
            sb.AppendLine("using StellarNet.Lite.Shared.Infrastructure;");
            sb.AppendLine($"using {protocolNs};");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.AppendLine($"    [ClientModule(\"{EscapeStringLiteral(_moduleName)}\", \"{EscapeStringLiteral(_displayName)}\")]");
            sb.AppendLine($"    public sealed class Client{_moduleName}Module");
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly ClientApp _app;");
            sb.AppendLine();
            sb.AppendLine($"        public Client{_moduleName}Module(ClientApp app)");
            sb.AppendLine("        {");
            sb.AppendLine("            _app = app;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        [NetHandler]");
            sb.AppendLine($"        public void OnS2C_{_moduleName}Sync(S2C_{_moduleName}Sync msg)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (msg == null)");
            sb.AppendLine("            {");
            sb.AppendLine($"                NetLogger.LogError(\"Client{_moduleName}Module\", \"收到非法同步包: Msg 为空\");");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            GlobalTypeNetEvent.Broadcast(msg);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return WriteToFile(path, sb.ToString());
        }

        private bool WriteToFile(string path, string content)
        {
            if (string.IsNullOrEmpty(path))
            {
                NetLogger.LogError("Scaffold", "写入失败: path 为空");
                EditorUtility.DisplayDialog("写入失败", "输出路径为空，已阻断生成。", "确定");
                return false;
            }

            if (content == null)
            {
                NetLogger.LogError("Scaffold", $"写入失败: content 为空, Path:{path}");
                EditorUtility.DisplayDialog("写入失败", $"生成内容为空，已阻断后续生成。\n{path}", "确定");
                return false;
            }

            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                NetLogger.LogError("Scaffold", $"写入失败: 目录为空, Path:{path}");
                EditorUtility.DisplayDialog("写入失败", $"输出目录非法，已阻断后续生成。\n{path}", "确定");
                return false;
            }

            if (File.Exists(path))
            {
                NetLogger.LogError("Scaffold", $"写入阻断: 目标文件已存在，拒绝覆盖, Path:{path}");
                EditorUtility.DisplayDialog("写入阻断", $"目标文件已存在，已阻断后续生成以防覆盖手写逻辑：\n{path}", "确定");
                return false;
            }

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content, Encoding.UTF8);
            return true;
        }

        #endregion
    }
}
#endif