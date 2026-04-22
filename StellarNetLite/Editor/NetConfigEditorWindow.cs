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
    /// Runtime / Replay / ObjectSync 三套配置统一在这里编辑，但保存为独立文件。
    /// </summary>
    public sealed class NetConfigEditorWindow : EditorWindow
    {
        private NetConfig _runtimeConfig = new NetConfig();
        private ReplayGlobalConfig _replayConfig = new ReplayGlobalConfig();
        private ObjectSyncGlobalConfig _objectSyncConfig = new ObjectSyncGlobalConfig();
        private ConfigRootPath _targetRoot = ConfigRootPath.StreamingAssets;

        private string _loadedSnapshotJson = string.Empty;
        private string _runtimePath = string.Empty;
        private string _replayPath = string.Empty;
        private string _objectSyncPath = string.Empty;
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

        [MenuItem("StellarNetLite/网络配置", false, 0)]
        public static void ShowWindow()
        {
            NetConfigEditorWindow window = GetWindow<NetConfigEditorWindow>("NetConfig Editor");
            window.minSize = new Vector2(500, 620);
            window.Show();
        }

        private void OnEnable()
        {
            _targetRoot = NetConfigLoader.LoadRuntimeRootSync();
            LoadFromCurrentRoot();
        }

        private void OnGUI()
        {
            InitializeStyles();

            GUILayout.Space(10);
            GUILayout.Label("StellarNet Lite 配置中心", _headerStyle);
            EditorGUILayout.HelpBox("Runtime、Replay、ObjectSync 配置现在已经拆分保存，不再混写到同一个 netconfig.json。", MessageType.Info);

            DrawToolbar();
            DrawResolvedPathInfo();

            GUILayout.Space(6);
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            DrawRuntimeFields();
            GUILayout.Space(6);
            DrawReplayFields();
            GUILayout.Space(6);
            DrawObjectSyncFields();
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
            ConfigRootPath nextRoot = (ConfigRootPath)EditorGUILayout.EnumPopup("运行时读取根目录 (Root Path):", _targetRoot);
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
                ApplyRuntimeRootSelection();
                LoadFromCurrentRoot();
            }

            EditorGUILayout.HelpBox(IsDirty() ? "有未保存修改" : "当前内容已保存", IsDirty() ? MessageType.Warning : MessageType.Info);
            EditorGUILayout.EndVertical();
        }

        private void DrawResolvedPathInfo()
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("当前配置目标路径", EditorStyles.boldLabel);

            EditorGUILayout.SelectableLabel(_runtimePath, _pathStyle, GUILayout.Height(34f));
            EditorGUILayout.SelectableLabel(_replayPath, _pathStyle, GUILayout.Height(34f));
            EditorGUILayout.SelectableLabel(_objectSyncPath, _pathStyle, GUILayout.Height(34f));

            string runtimeRootBootstrapPath = Path
                .Combine(Application.streamingAssetsPath, NetConfigLoader.ConfigFolderName, NetConfigLoader.RuntimeRootFileName)
                .Replace("\\", "/");
            EditorGUILayout.SelectableLabel(runtimeRootBootstrapPath, _pathStyle, GUILayout.Height(34f));

            EditorGUILayout.LabelField($"当前运行时激活根目录: {_targetRoot}", EditorStyles.miniBoldLabel);
            if (_targetRoot == ConfigRootPath.PersistentDataPath)
            {
                EditorGUILayout.HelpBox("PersistentDataPath 是当前设备沙盒目录；保存到这里的配置不会自动跟随 Build 发布。", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawRuntimeFields()
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("Runtime Core", EditorStyles.boldLabel);
            _runtimeConfig.Ip = EditorGUILayout.TextField("服务器 IP:", _runtimeConfig.Ip);
            int port = EditorGUILayout.IntField("端口 (Port):", _runtimeConfig.Port);
            _runtimeConfig.Port = (ushort)Mathf.Clamp(port, 0, 65535);
            _runtimeConfig.MaxConnections = EditorGUILayout.IntField("最大连接数:", _runtimeConfig.MaxConnections);
            _runtimeConfig.TickRate = EditorGUILayout.IntField("服务器帧率 (TickRate):", _runtimeConfig.TickRate);
            _runtimeConfig.RoomWorkerCount = EditorGUILayout.IntField("房间工作线程数(0=自动):", _runtimeConfig.RoomWorkerCount);
            _runtimeConfig.RoomWorkerReserveCpuCount = EditorGUILayout.IntField("自动模式预留CPU数:", _runtimeConfig.RoomWorkerReserveCpuCount);
            _runtimeConfig.MaxRoomLifetimeHours = EditorGUILayout.IntField("房间最大存活(小时):", _runtimeConfig.MaxRoomLifetimeHours);
            _runtimeConfig.OfflineTimeoutLobbyMinutes = EditorGUILayout.IntField("大厅离线GC(分钟):", _runtimeConfig.OfflineTimeoutLobbyMinutes);
            _runtimeConfig.OfflineTimeoutRoomMinutes = EditorGUILayout.IntField("房间离线GC(分钟):", _runtimeConfig.OfflineTimeoutRoomMinutes);
            _runtimeConfig.EmptyRoomTimeoutMinutes = EditorGUILayout.IntField("空房间熔断(分钟):", _runtimeConfig.EmptyRoomTimeoutMinutes);
            _runtimeConfig.MinClientVersion = EditorGUILayout.TextField("最低客户端版本:", _runtimeConfig.MinClientVersion);
            EditorGUILayout.EndVertical();
        }

        private void DrawReplayFields()
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("Replay Extension", EditorStyles.boldLabel);
            _replayConfig.EnableReplayRecording = EditorGUILayout.Toggle("启用录像录制:", _replayConfig.EnableReplayRecording);
            _replayConfig.MaxReplayFiles = EditorGUILayout.IntField("最大录像保留数:", _replayConfig.MaxReplayFiles);
            EditorGUILayout.EndVertical();
        }

        private void DrawObjectSyncFields()
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("ObjectSync Extension", EditorStyles.boldLabel);
            _objectSyncConfig.ObjectSyncOnlineIntervalTicks =
                EditorGUILayout.IntField("对象同步广播间隔Tick:", _objectSyncConfig.ObjectSyncOnlineIntervalTicks);
            _objectSyncConfig.ObjectSyncFullResyncIntervalTicks =
                EditorGUILayout.IntField("对象同步全量校正间隔Tick:", _objectSyncConfig.ObjectSyncFullResyncIntervalTicks);
            _objectSyncConfig.EnableAdaptiveObjectSync =
                EditorGUILayout.Toggle("按人数自动降频对象同步:", _objectSyncConfig.EnableAdaptiveObjectSync);
            _objectSyncConfig.ReplayObjectSyncRecordIntervalTicks =
                EditorGUILayout.IntField("对象同步录像间隔Tick(0=仅关键帧):", _objectSyncConfig.ReplayObjectSyncRecordIntervalTicks);
            EditorGUILayout.EndVertical();
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginVertical("box");
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
                        return;
                    }
                }

                LoadFromCurrentRoot();
            }

            if (GUILayout.Button("在资源管理器中打开目录", GUILayout.Height(28)))
            {
                OpenFolderInExplorer();
            }

            EditorGUILayout.EndVertical();
        }

        private void LoadFromCurrentRoot()
        {
            _runtimeConfig = NetConfigLoader.LoadEditorSync(_targetRoot) ?? new NetConfig();
            _replayConfig = ReplayConfigLoader.LoadEditorSync(_targetRoot) ?? new ReplayGlobalConfig();
            _objectSyncConfig = ObjectSyncConfigLoader.LoadEditorSync(_targetRoot) ?? new ObjectSyncGlobalConfig();

            NormalizeConfigs();
            RefreshPaths();
            _loadedSnapshotJson = SerializeSnapshot();
            Repaint();
        }

        private void SaveToCurrentRoot()
        {
            NormalizeConfigs();
            RefreshPaths();

            SaveResultState runtimeResult = SaveJsonIfChanged(_runtimePath, JsonConvert.SerializeObject(_runtimeConfig, Formatting.Indented));
            SaveResultState replayResult = SaveJsonIfChanged(_replayPath, JsonConvert.SerializeObject(_replayConfig, Formatting.Indented));
            SaveResultState objectSyncResult =
                SaveJsonIfChanged(_objectSyncPath, JsonConvert.SerializeObject(_objectSyncConfig, Formatting.Indented));

            if (runtimeResult == SaveResultState.Failed || replayResult == SaveResultState.Failed || objectSyncResult == SaveResultState.Failed)
            {
                return;
            }

            if (runtimeResult == SaveResultState.NoChange &&
                replayResult == SaveResultState.NoChange &&
                objectSyncResult == SaveResultState.NoChange)
            {
                EditorUtility.DisplayDialog("无需保存", "当前配置内容未发生变化。", "确定");
                return;
            }

            ApplyRuntimeRootSelection();
            _loadedSnapshotJson = SerializeSnapshot();
            Repaint();
            EditorUtility.DisplayDialog(
                "保存成功",
                $"Runtime:\n{_runtimePath}\n\nReplay:\n{_replayPath}\n\nObjectSync:\n{_objectSyncPath}",
                "确定");
        }

        private void ApplyRuntimeRootSelection()
        {
            NetConfigLoader.SaveRuntimeRootSelection(_targetRoot);
            AssetDatabase.Refresh();
        }

        private SaveResultState SaveJsonIfChanged(string fullPath, string json)
        {
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(json))
            {
                return SaveResultState.Failed;
            }

            string folderPath = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(folderPath))
            {
                return SaveResultState.Failed;
            }

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
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

        private void NormalizeConfigs()
        {
            if (string.IsNullOrWhiteSpace(_runtimeConfig.Ip)) _runtimeConfig.Ip = "127.0.0.1";
            if (_runtimeConfig.Port == 0) _runtimeConfig.Port = 7777;
            if (_runtimeConfig.MaxConnections <= 0) _runtimeConfig.MaxConnections = 200;
            if (_runtimeConfig.TickRate <= 0) _runtimeConfig.TickRate = 60;
            if (_runtimeConfig.RoomWorkerCount < 0) _runtimeConfig.RoomWorkerCount = 0;
            if (_runtimeConfig.RoomWorkerReserveCpuCount < 0) _runtimeConfig.RoomWorkerReserveCpuCount = 1;
            if (_runtimeConfig.MaxRoomLifetimeHours <= 0) _runtimeConfig.MaxRoomLifetimeHours = 24;
            if (_runtimeConfig.OfflineTimeoutLobbyMinutes < 0) _runtimeConfig.OfflineTimeoutLobbyMinutes = 5;
            if (_runtimeConfig.OfflineTimeoutRoomMinutes < 0) _runtimeConfig.OfflineTimeoutRoomMinutes = 60;
            if (_runtimeConfig.EmptyRoomTimeoutMinutes < 0) _runtimeConfig.EmptyRoomTimeoutMinutes = 5;
            if (string.IsNullOrWhiteSpace(_runtimeConfig.MinClientVersion)) _runtimeConfig.MinClientVersion = "0.0.1";

            if (_replayConfig.MaxReplayFiles < 0) _replayConfig.MaxReplayFiles = 100;

            if (_objectSyncConfig.ReplayObjectSyncRecordIntervalTicks < 0) _objectSyncConfig.ReplayObjectSyncRecordIntervalTicks = 3;
            if (_objectSyncConfig.ObjectSyncOnlineIntervalTicks <= 0) _objectSyncConfig.ObjectSyncOnlineIntervalTicks = 2;
            if (_objectSyncConfig.ObjectSyncFullResyncIntervalTicks <= 0) _objectSyncConfig.ObjectSyncFullResyncIntervalTicks = 60;
        }

        private bool IsDirty()
        {
            return SerializeSnapshot() != _loadedSnapshotJson;
        }

        private string SerializeSnapshot()
        {
            return JsonConvert.SerializeObject(new
            {
                Runtime = _runtimeConfig,
                Replay = _replayConfig,
                ObjectSync = _objectSyncConfig,
                Root = _targetRoot
            }, Formatting.Indented);
        }

        private void RefreshPaths()
        {
            string basePath = _targetRoot == ConfigRootPath.StreamingAssets
                ? Application.streamingAssetsPath
                : Application.persistentDataPath;
            _runtimePath = string.IsNullOrEmpty(basePath)
                ? string.Empty
                : Path.Combine(basePath, NetConfigLoader.ConfigFolderName, NetConfigLoader.ConfigFileName).Replace("\\", "/");
            _replayPath = ReplayConfigLoader.GetFullPath(_targetRoot);
            _objectSyncPath = ObjectSyncConfigLoader.GetFullPath(_targetRoot);
        }

        private void OpenFolderInExplorer()
        {
            string basePath = _targetRoot == ConfigRootPath.StreamingAssets
                ? Application.streamingAssetsPath
                : Application.persistentDataPath;
            if (string.IsNullOrEmpty(basePath))
            {
                return;
            }

            string folderPath = Path.Combine(basePath, NetConfigLoader.ConfigFolderName).Replace("\\", "/");
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            EditorUtility.RevealInFinder(folderPath);
        }
    }
}
#endif
