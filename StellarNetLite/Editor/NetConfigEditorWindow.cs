#if UNITY_EDITOR
using System.IO;
using Newtonsoft.Json;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEditor;
using UnityEngine;

namespace StellarNet.Lite.Editor
{
    public class NetConfigEditorWindow : EditorWindow
    {
        private NetConfig _currentConfig = new NetConfig();
        private ConfigRootPath _targetRoot = ConfigRootPath.StreamingAssets;

        [MenuItem("StellarNet/Lite 网络配置 (NetConfig)")]
        public static void ShowWindow()
        {
            var window = GetWindow<NetConfigEditorWindow>("NetConfig Editor");
            window.minSize = new Vector2(400, 450);
            window.Show();
        }

        private void OnEnable()
        {
            LoadFromCurrentRoot();
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            EditorGUILayout.LabelField("StellarNet Lite 全局网络配置", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("修改后点击保存，将自动写入到对应的 NetConfig/netconfig.json 文件中。", MessageType.Info);
            GUILayout.Space(10);

            EditorGUI.BeginChangeCheck();
            _targetRoot = (ConfigRootPath)EditorGUILayout.EnumPopup("存储目录 (Root Path):", _targetRoot);
            if (EditorGUI.EndChangeCheck())
            {
                LoadFromCurrentRoot();
            }

            if (_currentConfig == null)
            {
                _currentConfig = new NetConfig();
            }

            GUILayout.Space(10);
            EditorGUILayout.BeginVertical("box");
            _currentConfig.Ip = EditorGUILayout.TextField("服务器 IP:", _currentConfig.Ip);

            int port = EditorGUILayout.IntField("端口 (Port):", _currentConfig.Port);
            _currentConfig.Port = (ushort)Mathf.Clamp(port, 0, 65535);

            _currentConfig.MaxConnections = EditorGUILayout.IntField("最大连接数:", _currentConfig.MaxConnections);
            _currentConfig.TickRate = EditorGUILayout.IntField("服务器帧率 (TickRate):", _currentConfig.TickRate);

            GUILayout.Space(5);
            EditorGUILayout.LabelField("生产环境防御配置 (GC & 熔断)", EditorStyles.boldLabel);
            _currentConfig.MaxRoomLifetimeHours = EditorGUILayout.IntField("房间最大存活(小时):", _currentConfig.MaxRoomLifetimeHours);
            _currentConfig.MaxReplayFiles = EditorGUILayout.IntField("最大录像保留数:", _currentConfig.MaxReplayFiles);
            _currentConfig.OfflineTimeoutLobbyMinutes = EditorGUILayout.IntField("大厅离线GC(分钟):", _currentConfig.OfflineTimeoutLobbyMinutes);
            _currentConfig.OfflineTimeoutRoomMinutes = EditorGUILayout.IntField("房间离线GC(分钟):", _currentConfig.OfflineTimeoutRoomMinutes);
            _currentConfig.EmptyRoomTimeoutMinutes = EditorGUILayout.IntField("空房间熔断(分钟):", _currentConfig.EmptyRoomTimeoutMinutes);

            GUILayout.Space(5);
            EditorGUILayout.LabelField("版本控制策略", EditorStyles.boldLabel);
            _currentConfig.MinClientVersion = EditorGUILayout.TextField("最低客户端版本:", _currentConfig.MinClientVersion);
            EditorGUILayout.EndVertical();

            GUILayout.Space(20);
            GUI.color = Color.green;
            if (GUILayout.Button("保存配置 (Save Config)", GUILayout.Height(35)))
            {
                SaveToCurrentRoot();
            }

            GUI.color = Color.white;

            GUILayout.Space(10);
            if (GUILayout.Button("在资源管理器中打开目录"))
            {
                OpenFolderInExplorer();
            }
        }

        private void LoadFromCurrentRoot()
        {
            _currentConfig = NetConfigLoader.LoadEditorSync(_targetRoot);
            if (_currentConfig == null)
            {
                NetLogger.LogError("NetConfigEditorWindow", $"加载配置失败: 返回 config 为空, Root:{_targetRoot}");
                _currentConfig = new NetConfig();
            }
        }

        private void SaveToCurrentRoot()
        {
            if (_currentConfig == null)
            {
                NetLogger.LogError("NetConfigEditorWindow", "保存失败: _currentConfig 为空");
                return;
            }

            NormalizeCurrentConfig();

            string basePath = _targetRoot == ConfigRootPath.StreamingAssets
                ? Application.streamingAssetsPath
                : Application.persistentDataPath;

            if (string.IsNullOrEmpty(basePath))
            {
                NetLogger.LogError("NetConfigEditorWindow", $"保存失败: basePath 为空, Root:{_targetRoot}");
                return;
            }

            string folderPath = Path.Combine(basePath, NetConfigLoader.ConfigFolderName).Replace("\\", "/");
            string fullPath = Path.Combine(folderPath, NetConfigLoader.ConfigFileName).Replace("\\", "/");

            if (string.IsNullOrEmpty(folderPath) || string.IsNullOrEmpty(fullPath))
            {
                NetLogger.LogError("NetConfigEditorWindow", $"保存失败: 路径非法, Folder:{folderPath}, FullPath:{fullPath}, Root:{_targetRoot}");
                return;
            }

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            string json = JsonConvert.SerializeObject(_currentConfig, Formatting.Indented);
            if (string.IsNullOrEmpty(json))
            {
                NetLogger.LogError("NetConfigEditorWindow", $"保存失败: 序列化结果为空, FullPath:{fullPath}");
                return;
            }

            File.WriteAllText(fullPath, json);
            AssetDatabase.Refresh();
            NetLogger.LogInfo("NetConfigEditorWindow", $"配置保存成功, Path:{fullPath}");
        }

        private void NormalizeCurrentConfig()
        {
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
            string basePath = _targetRoot == ConfigRootPath.StreamingAssets
                ? Application.streamingAssetsPath
                : Application.persistentDataPath;

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
            }

            EditorUtility.RevealInFinder(folderPath);
        }
    }
}
#endif