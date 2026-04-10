#if UNITY_EDITOR
using System.IO;
using Newtonsoft.Json;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEditor;
using UnityEngine;

namespace StellarNet.Lite.Editor
{
    /// <summary>
    /// 网络配置编辑器窗口。
    /// </summary>
    public sealed class NetConfigEditorWindow : EditorWindow
    {
        // 当前编辑中的配置对象和目标根目录。
        private NetConfig _currentConfig = new NetConfig();
        private ConfigRootPath _targetRoot = ConfigRootPath.StreamingAssets;
        // GUI 状态缓存。
        private string _loadedSnapshotJson = string.Empty;
        private string _currentResolvedPath = string.Empty;
        private Vector2 _scrollPos;
        private GUIStyle _headerStyle;
        private GUIStyle _pathStyle;
        private bool _stylesInitialized;

        private enum SaveResultState : byte
        {
            NoChange = 0,
            Saved = 1,
            Failed = 2
        }

        [MenuItem("StellarNetLite/网络配置 (NetConfig)")]
        public static void ShowWindow()
        {
            NetConfigEditorWindow window = GetWindow<NetConfigEditorWindow>("NetConfig Editor");
            window.minSize = new Vector2(460, 520);
            window.Show();
        }

        private void OnEnable()
        {
            // 打开窗口时按当前根目录读取一次配置。
            LoadFromCurrentRoot();
        }

        private void OnGUI()
        {
            InitializeStyles();

            GUILayout.Space(10);
            GUILayout.Label("StellarNet Lite 全局网络配置", _headerStyle);
            EditorGUILayout.HelpBox("修改后点击保存，将自动写入到对应目录下的 NetConfig/netconfig.json 文件中。", MessageType.Info);

            DrawToolbar();
            DrawResolvedPathInfo();

            GUILayout.Space(6);
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            EnsureConfigInstance();
            DrawConfigFields();

            EditorGUILayout.EndScrollView();

            GUILayout.Space(10);
            DrawActionButtons();
        }

        private void InitializeStyles()
        {
            if (_stylesInitialized)
            {
                return;
            }

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter
            };

            _pathStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                normal = { textColor = Color.gray }
            };

            _stylesInitialized = true;
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginVertical("box");

            EditorGUI.BeginChangeCheck();
            ConfigRootPath nextRoot = (ConfigRootPath)EditorGUILayout.EnumPopup("存储目录 (Root Path):", _targetRoot);
            if (EditorGUI.EndChangeCheck())
            {
                if (IsDirty())
                {
                    bool confirmSwitch = EditorUtility.DisplayDialog(
                        "切换目录确认",
                        "当前配置存在未保存修改，切换目录会丢失当前编辑内容，是否继续？",
                        "继续切换",
                        "取消");

                    if (!confirmSwitch)
                    {
                        return;
                    }
                }

                _targetRoot = nextRoot;
                LoadFromCurrentRoot();
            }

            string dirtyText = IsDirty() ? "有未保存修改" : "当前内容已保存";
            MessageType dirtyType = IsDirty() ? MessageType.Warning : MessageType.Info;
            EditorGUILayout.HelpBox(dirtyText, dirtyType);

            EditorGUILayout.EndVertical();
        }

        private void DrawResolvedPathInfo()
        {
            EditorGUILayout.BeginVertical("box");

            GUILayout.Label("当前配置目标路径", EditorStyles.boldLabel);

            if (string.IsNullOrEmpty(_currentResolvedPath))
            {
                EditorGUILayout.HelpBox($"当前 Root 无法解析有效路径。Root:{_targetRoot}", MessageType.Error);
                return;
            }

            EditorGUILayout.SelectableLabel(_currentResolvedPath, _pathStyle, GUILayout.Height(34f));

            bool configExists = File.Exists(_currentResolvedPath);
            EditorGUILayout.HelpBox(
                configExists ? "当前目标文件已存在" : "当前目标文件不存在，首次保存时将自动创建",
                configExists ? MessageType.None : MessageType.Warning);

            EditorGUILayout.EndVertical();
        }

        private void DrawConfigFields()
        {
            EditorGUILayout.BeginVertical("box");

            EditorGUI.BeginChangeCheck();

            _currentConfig.Ip = EditorGUILayout.TextField("服务器 IP:", _currentConfig.Ip);

            int port = EditorGUILayout.IntField("端口 (Port):", _currentConfig.Port);
            _currentConfig.Port = (ushort)Mathf.Clamp(port, 0, 65535);

            _currentConfig.MaxConnections = EditorGUILayout.IntField("最大连接数:", _currentConfig.MaxConnections);
            _currentConfig.TickRate = EditorGUILayout.IntField("服务器帧率 (TickRate):", _currentConfig.TickRate);

            GUILayout.Space(6);
            EditorGUILayout.LabelField("生产环境防御配置 (GC & 熔断)", EditorStyles.boldLabel);

            _currentConfig.MaxRoomLifetimeHours = EditorGUILayout.IntField("房间最大存活(小时):", _currentConfig.MaxRoomLifetimeHours);
            _currentConfig.MaxReplayFiles = EditorGUILayout.IntField("最大录像保留数:", _currentConfig.MaxReplayFiles);
            _currentConfig.OfflineTimeoutLobbyMinutes = EditorGUILayout.IntField("大厅离线GC(分钟):", _currentConfig.OfflineTimeoutLobbyMinutes);
            _currentConfig.OfflineTimeoutRoomMinutes = EditorGUILayout.IntField("房间离线GC(分钟):", _currentConfig.OfflineTimeoutRoomMinutes);
            _currentConfig.EmptyRoomTimeoutMinutes = EditorGUILayout.IntField("空房间熔断(分钟):", _currentConfig.EmptyRoomTimeoutMinutes);

            GUILayout.Space(6);
            EditorGUILayout.LabelField("版本控制策略", EditorStyles.boldLabel);
            _currentConfig.MinClientVersion = EditorGUILayout.TextField("最低客户端版本:", _currentConfig.MinClientVersion);

            if (EditorGUI.EndChangeCheck())
            {
                Repaint();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginVertical("box");

            GUI.enabled = true;
            GUI.color = IsDirty() ? new Color(0.2f, 0.85f, 0.2f) : new Color(0.6f, 0.8f, 0.6f);
            if (GUILayout.Button("保存配置 (Save Config)", GUILayout.Height(36)))
            {
                SaveToCurrentRoot();
            }

            GUI.color = Color.white;

            if (GUILayout.Button("重新加载当前目录配置", GUILayout.Height(28)))
            {
                if (IsDirty())
                {
                    bool confirmReload = EditorUtility.DisplayDialog(
                        "重新加载确认",
                        "当前存在未保存修改，重新加载会覆盖当前内容，是否继续？",
                        "重新加载",
                        "取消");

                    if (!confirmReload)
                    {
                        EditorGUILayout.EndVertical();
                        GUI.enabled = true;
                        return;
                    }
                }

                LoadFromCurrentRoot();
            }

            if (GUILayout.Button("在资源管理器中打开目录", GUILayout.Height(28)))
            {
                OpenFolderInExplorer();
            }

            GUI.enabled = true;
            EditorGUILayout.EndVertical();
        }

        private void EnsureConfigInstance()
        {
            if (_currentConfig != null)
            {
                return;
            }

            NetLogger.LogError("NetConfigEditorWindow", $"配置实例为空，已自动回退默认配置。Root:{_targetRoot}");
            _currentConfig = new NetConfig();
        }

        private void LoadFromCurrentRoot()
        {
            _currentResolvedPath = ResolveCurrentFullPath();

            _currentConfig = NetConfigLoader.LoadEditorSync(_targetRoot);
            if (_currentConfig == null)
            {
                NetLogger.LogError("NetConfigEditorWindow", $"加载配置失败: 返回 config 为空, Root:{_targetRoot}, Path:{_currentResolvedPath}");
                _currentConfig = new NetConfig();
            }

            NormalizeCurrentConfig();
            _loadedSnapshotJson = SerializeConfigSnapshot(_currentConfig);

            Repaint();
        }

        private void SaveToCurrentRoot()
        {
            if (_currentConfig == null)
            {
                NetLogger.LogError("NetConfigEditorWindow", $"保存失败: _currentConfig 为空, Root:{_targetRoot}");
                return;
            }

            NormalizeCurrentConfig();

            SaveResultState saveResult = SaveConfigToCurrentRoot(_currentConfig);
            if (saveResult == SaveResultState.Failed)
            {
                return;
            }

            if (saveResult == SaveResultState.NoChange)
            {
                NetLogger.LogInfo("NetConfigEditorWindow", $"保存跳过: 配置内容未变化, Root:{_targetRoot}, Path:{_currentResolvedPath}");
                EditorUtility.DisplayDialog("无需保存", "当前配置内容未发生变化。", "确定");
                return;
            }

            _loadedSnapshotJson = SerializeConfigSnapshot(_currentConfig);
            Repaint();

            NetLogger.LogInfo("NetConfigEditorWindow", $"配置保存成功, Root:{_targetRoot}, Path:{_currentResolvedPath}");
            EditorUtility.DisplayDialog("保存成功", $"配置已写入:\n{_currentResolvedPath}", "确定");
        }

        private SaveResultState SaveConfigToCurrentRoot(NetConfig config)
        {
            if (config == null)
            {
                NetLogger.LogError("NetConfigEditorWindow", $"保存失败: config 为空, Root:{_targetRoot}");
                return SaveResultState.Failed;
            }

            string basePath = ResolveCurrentBasePath();
            if (string.IsNullOrEmpty(basePath))
            {
                NetLogger.LogError("NetConfigEditorWindow", $"保存失败: basePath 为空, Root:{_targetRoot}");
                return SaveResultState.Failed;
            }

            string folderPath = Path.Combine(basePath, NetConfigLoader.ConfigFolderName).Replace("\\", "/");
            string fullPath = Path.Combine(folderPath, NetConfigLoader.ConfigFileName).Replace("\\", "/");
            _currentResolvedPath = fullPath;

            if (string.IsNullOrEmpty(folderPath) || string.IsNullOrEmpty(fullPath))
            {
                NetLogger.LogError("NetConfigEditorWindow", $"保存失败: 路径非法, Folder:{folderPath}, FullPath:{fullPath}, Root:{_targetRoot}");
                return SaveResultState.Failed;
            }

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
                if (!Directory.Exists(folderPath))
                {
                    NetLogger.LogError("NetConfigEditorWindow", $"保存失败: 目录创建失败, Folder:{folderPath}, Root:{_targetRoot}");
                    return SaveResultState.Failed;
                }
            }

            string json = SerializeConfigSnapshot(config);
            if (string.IsNullOrEmpty(json))
            {
                NetLogger.LogError("NetConfigEditorWindow", $"保存失败: 序列化结果为空, FullPath:{fullPath}, Root:{_targetRoot}");
                return SaveResultState.Failed;
            }

            string oldContent = File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;
            if (oldContent == json)
            {
                return SaveResultState.NoChange;
            }

            File.WriteAllText(fullPath, json);
            AssetDatabase.Refresh();
            return SaveResultState.Saved;
        }

        private void NormalizeCurrentConfig()
        {
            if (_currentConfig == null)
            {
                NetLogger.LogError("NetConfigEditorWindow", $"归一化失败: _currentConfig 为空, Root:{_targetRoot}");
                _currentConfig = new NetConfig();
            }

            if (string.IsNullOrWhiteSpace(_currentConfig.Ip))
            {
                _currentConfig.Ip = "127.0.0.1";
            }

            if (_currentConfig.Port == 0)
            {
                _currentConfig.Port = 7777;
            }

            if (_currentConfig.MaxConnections <= 0)
            {
                _currentConfig.MaxConnections = 200;
            }

            if (_currentConfig.TickRate <= 0)
            {
                _currentConfig.TickRate = 60;
            }

            if (_currentConfig.MaxRoomLifetimeHours <= 0)
            {
                _currentConfig.MaxRoomLifetimeHours = 24;
            }

            if (_currentConfig.MaxReplayFiles < 0)
            {
                _currentConfig.MaxReplayFiles = 100;
            }

            if (_currentConfig.OfflineTimeoutLobbyMinutes < 0)
            {
                _currentConfig.OfflineTimeoutLobbyMinutes = 5;
            }

            if (_currentConfig.OfflineTimeoutRoomMinutes < 0)
            {
                _currentConfig.OfflineTimeoutRoomMinutes = 60;
            }

            if (_currentConfig.EmptyRoomTimeoutMinutes < 0)
            {
                _currentConfig.EmptyRoomTimeoutMinutes = 5;
            }

            if (string.IsNullOrWhiteSpace(_currentConfig.MinClientVersion))
            {
                _currentConfig.MinClientVersion = "0.0.1";
            }
        }

        private void OpenFolderInExplorer()
        {
            string basePath = ResolveCurrentBasePath();
            if (string.IsNullOrEmpty(basePath))
            {
                NetLogger.LogError("NetConfigEditorWindow", $"打开目录失败: basePath 为空, Root:{_targetRoot}");
                return;
            }

            string folderPath = Path.Combine(basePath, NetConfigLoader.ConfigFolderName).Replace("\\", "/");
            if (string.IsNullOrEmpty(folderPath))
            {
                NetLogger.LogError("NetConfigEditorWindow", $"打开目录失败: folderPath 为空, Root:{_targetRoot}");
                return;
            }

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
                if (!Directory.Exists(folderPath))
                {
                    NetLogger.LogError("NetConfigEditorWindow", $"打开目录失败: 目录创建失败, Folder:{folderPath}, Root:{_targetRoot}");
                    return;
                }
            }

            EditorUtility.RevealInFinder(folderPath);
        }

        private bool IsDirty()
        {
            EnsureConfigInstance();

            string currentSnapshot = SerializeConfigSnapshot(_currentConfig);
            if (string.IsNullOrEmpty(currentSnapshot) && string.IsNullOrEmpty(_loadedSnapshotJson))
            {
                return false;
            }

            return currentSnapshot != _loadedSnapshotJson;
        }

        private string SerializeConfigSnapshot(NetConfig config)
        {
            if (config == null)
            {
                NetLogger.LogError("NetConfigEditorWindow", $"快照序列化失败: config 为空, Root:{_targetRoot}");
                return string.Empty;
            }

            return JsonConvert.SerializeObject(config, Formatting.Indented);
        }

        private string ResolveCurrentBasePath()
        {
            return _targetRoot == ConfigRootPath.StreamingAssets
                ? Application.streamingAssetsPath
                : Application.persistentDataPath;
        }

        private string ResolveCurrentFullPath()
        {
            string basePath = ResolveCurrentBasePath();
            if (string.IsNullOrEmpty(basePath))
            {
                return string.Empty;
            }

            return Path.Combine(basePath, NetConfigLoader.ConfigFolderName, NetConfigLoader.ConfigFileName).Replace("\\", "/");
        }
    }
}
#endif
