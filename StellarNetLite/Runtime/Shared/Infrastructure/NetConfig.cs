using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace StellarNet.Lite.Shared.Infrastructure
{
    public enum ConfigRootPath
    {
        StreamingAssets,
        PersistentDataPath
    }

    [Serializable]
    public sealed class NetConfig
    {
        public string Ip = "127.0.0.1";
        public ushort Port = 7777;
        public int MaxConnections = 200;
        public int TickRate = 60;
        public int MaxRoomLifetimeHours = 24;
        public int MaxReplayFiles = 100;
        public int OfflineTimeoutLobbyMinutes = 5;
        public int OfflineTimeoutRoomMinutes = 60;
        public int EmptyRoomTimeoutMinutes = 5;
        public string MinClientVersion = "0.0.1";
    }

    public static class NetConfigLoader
    {
        public const string ConfigFolderName = "NetConfig";
        public const string ConfigFileName = "netconfig.json";

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

        private static NetConfig DeserializeOrDefault(string jsonContent, string fullPath, string stage)
        {
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                NetLogger.LogWarning("NetConfigLoader", $"反序列化前内容为空，使用默认配置。Stage:{stage}, Path:{fullPath}");
                return CreateDefaultConfigWithLog("反序列化前内容为空");
            }

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
        public static NetConfig LoadEditorSync(ConfigRootPath rootPath)
        {
            return LoadServerConfigSync(rootPath);
        }
#endif
    }
}