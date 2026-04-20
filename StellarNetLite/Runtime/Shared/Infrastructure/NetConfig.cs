using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace StellarNet.Lite.Shared.Infrastructure
{
    /// <summary>
    /// 配置文件根目录类型。
    /// </summary>
    public enum ConfigRootPath
    {
        StreamingAssets,
        PersistentDataPath
    }

    /// <summary>
    /// 框架运行时配置。
    /// </summary>
    [Serializable]
    public sealed class NetConfig
    {
        /// <summary>
        /// 服务端监听地址或客户端目标地址。
        /// </summary>
        public string Ip = "127.0.0.1";

        /// <summary>
        /// 服务端监听端口。
        /// </summary>
        public ushort Port = 7777;

        /// <summary>
        /// 最大物理连接数。
        /// </summary>
        public int MaxConnections = 200;

        /// <summary>
        /// 服务端主 Tick 频率。
        /// </summary>
        public int TickRate = 60;

        /// <summary>
        /// 房间最大生命周期，单位小时。
        /// </summary>
        public int MaxRoomLifetimeHours = 24;

        /// <summary>
        /// 最大保留录像文件数。
        /// </summary>
        public int MaxReplayFiles = 100;

        /// <summary>
        /// 是否启用服务端录像录制。
        /// </summary>
        public bool EnableReplayRecording = true;

        /// <summary>
        /// 对象同步消息写入录像的 Tick 间隔。
        /// 0 表示不记录高频对象同步消息，仅保留关键帧和其它业务消息。
        /// </summary>
        public int ReplayObjectSyncRecordIntervalTicks = 3;

        /// <summary>
        /// 大厅态离线保留时间，单位分钟。
        /// </summary>
        public int OfflineTimeoutLobbyMinutes = 5;

        /// <summary>
        /// 房间态离线保留时间，单位分钟。
        /// </summary>
        public int OfflineTimeoutRoomMinutes = 60;

        /// <summary>
        /// 空房间自动清理时间，单位分钟。
        /// </summary>
        public int EmptyRoomTimeoutMinutes = 5;

        /// <summary>
        /// 最低客户端版本号。
        /// </summary>
        public string MinClientVersion = "0.0.1";
    }

    /// <summary>
    /// NetConfig 的加载器。
    /// 负责同步和异步读取，并对非法值做兜底归一化。
    /// </summary>
    public static class NetConfigLoader
    {
        public const string ConfigFolderName = "NetConfig";
        public const string ConfigFileName = "netconfig.json";
        public const string RuntimeRootFileName = "netconfig_root.json";

        [Serializable]
        private sealed class RuntimeRootConfig
        {
            public ConfigRootPath ActiveRoot = ConfigRootPath.StreamingAssets;
        }

        /// <summary>
        /// 按当前运行时根目录加载配置。
        /// 根目录选择由 StreamingAssets 下的引导文件决定。
        /// </summary>
        public static async Task<NetConfig> LoadRuntimeConfigAsync()
        {
            ConfigRootPath rootPath = await LoadRuntimeRootAsync();
            return await LoadAsync(rootPath);
        }

        /// <summary>
        /// 按当前运行时根目录同步加载配置。
        /// 根目录选择由 StreamingAssets 下的引导文件决定。
        /// </summary>
        public static NetConfig LoadRuntimeConfigSync()
        {
            ConfigRootPath rootPath = LoadRuntimeRootSync();
            return LoadServerConfigSync(rootPath);
        }

        /// <summary>
        /// 异步读取当前运行时配置根目录。
        /// </summary>
        public static async Task<ConfigRootPath> LoadRuntimeRootAsync()
        {
            string bootstrapPath = GetRuntimeRootFullPath();
            if (string.IsNullOrEmpty(bootstrapPath))
            {
                return ConfigRootPath.StreamingAssets;
            }

            if (Application.platform == RuntimePlatform.Android)
            {
                string bootstrapJson = await ReadViaWebRequestAsync(bootstrapPath);
                return DeserializeRuntimeRootOrDefault(bootstrapJson, bootstrapPath, "Android 运行时根目录异步读取");
            }

            if (!File.Exists(bootstrapPath))
            {
                return ConfigRootPath.StreamingAssets;
            }

            string jsonContent = File.ReadAllText(bootstrapPath);
            return DeserializeRuntimeRootOrDefault(jsonContent, bootstrapPath, "运行时根目录异步读取");
        }

        /// <summary>
        /// 同步读取当前运行时配置根目录。
        /// </summary>
        public static ConfigRootPath LoadRuntimeRootSync()
        {
            string bootstrapPath = GetRuntimeRootFullPath();
            if (string.IsNullOrEmpty(bootstrapPath))
            {
                return ConfigRootPath.StreamingAssets;
            }

            if (Application.platform == RuntimePlatform.Android)
            {
                NetLogger.LogWarning(
                    "NetConfigLoader",
                    "Android 平台禁止同步读取 StreamingAssets 根目录引导文件，已回退默认根目录 StreamingAssets。");
                return ConfigRootPath.StreamingAssets;
            }

            if (!File.Exists(bootstrapPath))
            {
                return ConfigRootPath.StreamingAssets;
            }

            string jsonContent = File.ReadAllText(bootstrapPath);
            return DeserializeRuntimeRootOrDefault(jsonContent, bootstrapPath, "运行时根目录同步读取");
        }

        /// <summary>
        /// 异步加载配置。
        /// Android 的 StreamingAssets 必须走 WebRequest。
        /// </summary>
        public static async Task<NetConfig> LoadAsync(ConfigRootPath rootPath)
        {
            string fullPath = GetFullPath(rootPath);
            if (string.IsNullOrEmpty(fullPath))
            {
                NetLogger.LogError("NetConfigLoader", $"异步读取失败: 路径为空, RootPath:{rootPath}");
                return CreateDefaultConfigWithLog("异步读取路径为空");
            }

            if (rootPath == ConfigRootPath.StreamingAssets && Application.platform == RuntimePlatform.Android)
            {
                string androidJson = await ReadViaWebRequestAsync(fullPath);
                return DeserializeOrDefault(androidJson, fullPath, "Android StreamingAssets 异步读取");
            }

            if (!File.Exists(fullPath))
            {
                NetLogger.LogWarning("NetConfigLoader", $"配置文件不存在，使用默认配置。Path:{fullPath}");
                return CreateDefaultConfigWithLog("配置文件不存在");
            }

            string jsonContent = File.ReadAllText(fullPath);
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                NetLogger.LogWarning("NetConfigLoader", $"配置文件为空，使用默认配置。Path:{fullPath}");
                return CreateDefaultConfigWithLog("配置文件为空");
            }

            return DeserializeOrDefault(jsonContent, fullPath, "普通异步读取");
        }

        /// <summary>
        /// 同步加载配置。
        /// 主要用于框架早期初始化阶段。
        /// </summary>
        public static NetConfig LoadServerConfigSync(ConfigRootPath rootPath)
        {
            if (rootPath == ConfigRootPath.StreamingAssets && Application.platform == RuntimePlatform.Android)
            {
                NetLogger.LogWarning(
                    "NetConfigLoader",
                    "Android 平台禁止同步读取 StreamingAssets，已回退默认配置。请改用 LoadAsync 异步加载后覆盖。");
                return CreateDefaultConfigWithLog("Android 同步读取 StreamingAssets 被阻断");
            }

            string fullPath = GetFullPath(rootPath);
            if (string.IsNullOrEmpty(fullPath))
            {
                NetLogger.LogError("NetConfigLoader", $"同步读取失败: 路径为空, RootPath:{rootPath}");
                return CreateDefaultConfigWithLog("同步读取路径为空");
            }

            if (!File.Exists(fullPath))
            {
                NetLogger.LogWarning("NetConfigLoader", $"未找到配置文件，使用默认配置。Path:{fullPath}");
                return CreateDefaultConfigWithLog("配置文件不存在");
            }

            string jsonContent = File.ReadAllText(fullPath);
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                NetLogger.LogWarning("NetConfigLoader", $"配置文件为空，使用默认配置。Path:{fullPath}");
                return CreateDefaultConfigWithLog("配置文件为空");
            }

            return DeserializeOrDefault(jsonContent, fullPath, "同步读取");
        }

        private static string GetFullPath(ConfigRootPath rootPath)
        {
            string basePath = rootPath == ConfigRootPath.StreamingAssets
                ? Application.streamingAssetsPath
                : Application.persistentDataPath;

            if (string.IsNullOrEmpty(basePath))
            {
                return string.Empty;
            }

            return Path.Combine(basePath, ConfigFolderName, ConfigFileName).Replace("\\", "/");
        }

        private static string GetRuntimeRootFullPath()
        {
            if (string.IsNullOrEmpty(Application.streamingAssetsPath))
            {
                return string.Empty;
            }

            return Path.Combine(Application.streamingAssetsPath, ConfigFolderName, RuntimeRootFileName).Replace("\\", "/");
        }

        private static ConfigRootPath DeserializeRuntimeRootOrDefault(string jsonContent, string fullPath, string stage)
        {
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                return ConfigRootPath.StreamingAssets;
            }

            try
            {
                RuntimeRootConfig rootConfig = JsonConvert.DeserializeObject<RuntimeRootConfig>(jsonContent);
                if (rootConfig == null)
                {
                    return ConfigRootPath.StreamingAssets;
                }

                return rootConfig.ActiveRoot;
            }
            catch (Exception ex)
            {
                NetLogger.LogWarning("NetConfigLoader", $"运行时根目录解析失败，已回退 StreamingAssets。Stage:{stage}, Path:{fullPath}, Error:{ex.Message}");
                return ConfigRootPath.StreamingAssets;
            }
        }

        private static NetConfig DeserializeOrDefault(string jsonContent, string fullPath, string stage)
        {
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                NetLogger.LogWarning("NetConfigLoader", $"反序列化前内容为空，使用默认配置。Stage:{stage}, Path:{fullPath}");
                return CreateDefaultConfigWithLog("反序列化前内容为空");
            }

            // 反序列化成功后还要做一次兜底归一化。
            NetConfig config = JsonConvert.DeserializeObject<NetConfig>(jsonContent);
            if (config == null)
            {
                NetLogger.LogError("NetConfigLoader", $"反序列化失败: 结果为空。Stage:{stage}, Path:{fullPath}");
                return CreateDefaultConfigWithLog("反序列化结果为空");
            }

            NormalizeConfig(config, fullPath);
            return config;
        }

        private static void NormalizeConfig(NetConfig config, string fullPath)
        {
            if (config == null)
            {
                NetLogger.LogError("NetConfigLoader", $"配置归一化失败: config 为空。Path:{fullPath}");
                return;
            }

            // 所有非法值都回退到安全默认值，避免运行时炸掉主链。
            if (string.IsNullOrWhiteSpace(config.Ip))
            {
                NetLogger.LogWarning("NetConfigLoader", $"配置修正: Ip 为空，已回退默认值 127.0.0.1。Path:{fullPath}");
                config.Ip = "127.0.0.1";
            }

            if (config.Port == 0)
            {
                NetLogger.LogWarning("NetConfigLoader", $"配置修正: Port 为 0，已回退默认值 7777。Path:{fullPath}");
                config.Port = 7777;
            }

            if (config.MaxConnections <= 0)
            {
                NetLogger.LogWarning("NetConfigLoader", $"配置修正: MaxConnections 非法，已回退默认值 200。Path:{fullPath}, Value:{config.MaxConnections}");
                config.MaxConnections = 200;
            }

            if (config.TickRate <= 0)
            {
                NetLogger.LogWarning("NetConfigLoader", $"配置修正: TickRate 非法，已回退默认值 60。Path:{fullPath}, Value:{config.TickRate}");
                config.TickRate = 60;
            }

            if (config.MaxRoomLifetimeHours <= 0)
            {
                NetLogger.LogWarning("NetConfigLoader", $"配置修正: MaxRoomLifetimeHours 非法，已回退默认值 24。Path:{fullPath}, Value:{config.MaxRoomLifetimeHours}");
                config.MaxRoomLifetimeHours = 24;
            }

            if (config.MaxReplayFiles < 0)
            {
                NetLogger.LogWarning("NetConfigLoader", $"配置修正: MaxReplayFiles 非法，已回退默认值 100。Path:{fullPath}, Value:{config.MaxReplayFiles}");
                config.MaxReplayFiles = 100;
            }

            if (config.ReplayObjectSyncRecordIntervalTicks < 0)
            {
                NetLogger.LogWarning("NetConfigLoader",
                    $"配置修正: ReplayObjectSyncRecordIntervalTicks 非法，已回退默认值 3。Path:{fullPath}, Value:{config.ReplayObjectSyncRecordIntervalTicks}");
                config.ReplayObjectSyncRecordIntervalTicks = 3;
            }

            if (config.OfflineTimeoutLobbyMinutes < 0)
            {
                NetLogger.LogWarning("NetConfigLoader", $"配置修正: OfflineTimeoutLobbyMinutes 非法，已回退默认值 5。Path:{fullPath}, Value:{config.OfflineTimeoutLobbyMinutes}");
                config.OfflineTimeoutLobbyMinutes = 5;
            }

            if (config.OfflineTimeoutRoomMinutes < 0)
            {
                NetLogger.LogWarning("NetConfigLoader", $"配置修正: OfflineTimeoutRoomMinutes 非法，已回退默认值 60。Path:{fullPath}, Value:{config.OfflineTimeoutRoomMinutes}");
                config.OfflineTimeoutRoomMinutes = 60;
            }

            if (config.EmptyRoomTimeoutMinutes < 0)
            {
                NetLogger.LogWarning("NetConfigLoader", $"配置修正: EmptyRoomTimeoutMinutes 非法，已回退默认值 5。Path:{fullPath}, Value:{config.EmptyRoomTimeoutMinutes}");
                config.EmptyRoomTimeoutMinutes = 5;
            }

            if (string.IsNullOrWhiteSpace(config.MinClientVersion))
            {
                NetLogger.LogWarning("NetConfigLoader", $"配置修正: MinClientVersion 为空，已回退默认值 0.0.1。Path:{fullPath}");
                config.MinClientVersion = "0.0.1";
            }
        }

        private static NetConfig CreateDefaultConfigWithLog(string reason)
        {
            NetLogger.LogInfo("NetConfigLoader", $"返回默认配置。Reason:{reason}");
            return new NetConfig();
        }

        private static async Task<string> ReadViaWebRequestAsync(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                NetLogger.LogError("NetConfigLoader", "WebRequest 读取失败: url 为空");
                return string.Empty;
            }

            using (var request = UnityWebRequest.Get(url))
            {
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (request.result == UnityWebRequest.Result.ConnectionError ||
                    request.result == UnityWebRequest.Result.ProtocolError)
                {
                    NetLogger.LogError("NetConfigLoader", $"WebRequest 读取失败: {request.error}, Url:{url}");
                    return string.Empty;
                }

                return request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            }
        }

#if UNITY_EDITOR
        public static void SaveRuntimeRootSelection(ConfigRootPath rootPath)
        {
            string bootstrapPath = GetRuntimeRootFullPath();
            if (string.IsNullOrEmpty(bootstrapPath))
            {
                NetLogger.LogError("NetConfigLoader", $"保存运行时根目录失败: bootstrapPath 为空, Root:{rootPath}");
                return;
            }

            string folderPath = Path.GetDirectoryName(bootstrapPath);
            if (string.IsNullOrEmpty(folderPath))
            {
                NetLogger.LogError("NetConfigLoader", $"保存运行时根目录失败: folderPath 为空, Path:{bootstrapPath}, Root:{rootPath}");
                return;
            }

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            var rootConfig = new RuntimeRootConfig
            {
                ActiveRoot = rootPath
            };

            string json = JsonConvert.SerializeObject(rootConfig, Formatting.Indented);
            File.WriteAllText(bootstrapPath, json);
        }

        public static NetConfig LoadEditorSync(ConfigRootPath rootPath)
        {
            return LoadServerConfigSync(rootPath);
        }
#endif
    }
}
