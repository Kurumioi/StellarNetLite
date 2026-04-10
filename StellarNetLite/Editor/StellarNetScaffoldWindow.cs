using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StellarNet.Lite.Editor
{
    /// <summary>
    /// 业务脚手架生成器窗口。
    /// </summary>
    public sealed class StellarNetScaffoldWindow : EditorWindow
    {
        /// <summary>
        /// 脚手架业务类型。
        /// </summary>
        private enum ScaffoldBusinessType
        {
            RoomComponent = 0,
            GlobalModule = 1
        }

        // 默认输出根目录。
        private const string DefaultOutputRoot = "Assets/Scripts/Game";

        // 默认基础命名空间。
        private const string DefaultBaseNamespace = "StellarNetLite";

        // 脚手架模板目录。
        private const string TemplatesPath = "Assets/StellarNetLite/Editor/Templates/";

        // 当前脚手架业务类型。
        private ScaffoldBusinessType _businessType = ScaffoldBusinessType.RoomComponent;

        // 当前前置命名空间。
        private string _namespacePrefix = string.Empty;

        // 当前输出根目录。
        private string _outputRoot = DefaultOutputRoot;

        // 当前功能名。
        private string _featureName = "NewFeature";

        // 当前显示名。
        private string _displayName = "新功能模块";

        // 当前房间组件 Id。
        private int _componentId = 10;

        // 当前 C2S 协议 Id。
        private int _c2sMsgId = 10000;

        // 当前 S2C 协议 Id。
        private int _s2cMsgId = 10001;

        [MenuItem("StellarNetLite/业务脚手架生成器")]
        public static void Open()
        {
            var window = GetWindow<StellarNetScaffoldWindow>("业务脚手架生成器");
            window.minSize = new Vector2(500f, 550f);
            window.Show();
        }

        /// <summary>
        /// 绘制脚手架生成窗口。
        /// </summary>
        private void OnGUI()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("业务脚手架生成器 (模板版)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("基于模板快速生成房间业务组件或全局模块的样板代码。", MessageType.Info);
            EditorGUILayout.Space(8f);

            _businessType = (ScaffoldBusinessType)EditorGUILayout.EnumPopup("脚手架类型", _businessType);
            _namespacePrefix = EditorGUILayout.TextField("前置 Namespace", _namespacePrefix);

            EditorGUILayout.BeginHorizontal();
            _outputRoot = EditorGUILayout.TextField("输出根目录", _outputRoot);
            if (GUILayout.Button("选择", GUILayout.Width(60f)))
            {
                string selected = EditorUtility.OpenFolderPanel("选择输出目录", Application.dataPath, "");
                if (!string.IsNullOrEmpty(selected) && selected.Contains("Assets"))
                {
                    _outputRoot = "Assets" + selected.Substring(Application.dataPath.Length);
                }
            }

            EditorGUILayout.EndHorizontal();

            _featureName = EditorGUILayout.TextField("模块名 (英文)", _featureName);
            _displayName = EditorGUILayout.TextField("显示名 (中文)", _displayName);

            if (_businessType == ScaffoldBusinessType.RoomComponent)
            {
                _componentId = EditorGUILayout.IntField("组件 Id", _componentId);
            }

            _c2sMsgId = EditorGUILayout.IntField("C2S MsgId", _c2sMsgId);
            _s2cMsgId = EditorGUILayout.IntField("S2C MsgId", _s2cMsgId);

            EditorGUILayout.Space(20f);

            if (GUILayout.Button("生成源码", GUILayout.Height(40f)))
            {
                GenerateScaffold();
            }
        }

        /// <summary>
        /// 生成业务脚手架源码。
        /// </summary>
        private void GenerateScaffold()
        {
            string fullNamespace = string.IsNullOrEmpty(_namespacePrefix) ? DefaultBaseNamespace : $"{_namespacePrefix}.{DefaultBaseNamespace}";
            string scope = _businessType == ScaffoldBusinessType.RoomComponent ? "NetScope.Room" : "NetScope.Global";

            // 1. 生成 Protocol
            string protocolTemplate = LoadTemplate("ProtocolTemplate.txt");
            if (protocolTemplate != null)
            {
                string content = ReplacePlaceholders(protocolTemplate, fullNamespace, scope);
                WriteFile(Path.Combine(_outputRoot, $"Shared/Protocol/{_featureName}Protocols.cs"), content);
            }

            // 2. 生成 Server & Client
            if (_businessType == ScaffoldBusinessType.RoomComponent)
            {
                string serverTemplate = LoadTemplate("ServerRoomComponentTemplate.txt");
                if (serverTemplate != null)
                    WriteFile(Path.Combine(_outputRoot, $"Server/Components/Server{_featureName}Component.cs"),
                        ReplacePlaceholders(serverTemplate, fullNamespace, scope));

                string clientTemplate = LoadTemplate("ClientRoomComponentTemplate.txt");
                if (clientTemplate != null)
                    WriteFile(Path.Combine(_outputRoot, $"Client/Components/Client{_featureName}Component.cs"),
                        ReplacePlaceholders(clientTemplate, fullNamespace, scope));
            }
            else
            {
                string serverTemplate = LoadTemplate("ServerModuleTemplate.txt");
                if (serverTemplate != null)
                    WriteFile(Path.Combine(_outputRoot, $"Server/Modules/Server{_featureName}Module.cs"),
                        ReplacePlaceholders(serverTemplate, fullNamespace, scope));

                string clientTemplate = LoadTemplate("ClientModuleTemplate.txt");
                if (clientTemplate != null)
                    WriteFile(Path.Combine(_outputRoot, $"Client/Modules/Client{_featureName}Module.cs"),
                        ReplacePlaceholders(clientTemplate, fullNamespace, scope));
            }

            AssetDatabase.Refresh();
            LiteProtocolScanner.ManualRun();
            EditorUtility.DisplayDialog("生成完成", $"业务脚手架生成成功！\n请检查目录: {_outputRoot}", "确定");
        }

        /// <summary>
        /// 替换模板占位符。
        /// </summary>
        private string ReplacePlaceholders(string template, string fullNamespace, string scope)
        {
            return template
                .Replace("#NAMESPACE#", fullNamespace)
                .Replace("#FEATURE_NAME#", _featureName)
                .Replace("#DISPLAY_NAME#", _displayName)
                .Replace("#COMPONENT_ID#", _componentId.ToString())
                .Replace("#C2S_MSG_ID#", _c2sMsgId.ToString())
                .Replace("#S2C_MSG_ID#", _s2cMsgId.ToString())
                .Replace("#SCOPE#", scope);
        }

        /// <summary>
        /// 读取指定模板文件。
        /// </summary>
        private string LoadTemplate(string fileName)
        {
            string path = Path.Combine(TemplatesPath, fileName);
            if (!File.Exists(path))
            {
                Debug.LogError($"[Scaffold] 找不到模板文件: {path}");
                return null;
            }

            return File.ReadAllText(path);
        }

        /// <summary>
        /// 写入生成后的源码文件。
        /// </summary>
        private void WriteFile(string path, string content)
        {
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
    }
}
