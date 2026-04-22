using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;
using UnityEngine.Networking;

namespace StellarNet.Lite.Shared.Infrastructure
{
    /// <summary>
    /// ObjectSync 扩展全局配置。
    /// </summary>
    [Serializable]
    public sealed class ObjectSyncGlobalConfig
    {
        public int ReplayObjectSyncRecordIntervalTicks = 3;
        public int ObjectSyncOnlineIntervalTicks = 2;
        public bool EnableAdaptiveObjectSync = true;
        public int ObjectSyncFullResyncIntervalTicks = 60;
    }

    /// <summary>
    /// ObjectSync 配置加载器。
    /// 与 Runtime 的 NetConfig 分离存储，且兼容旧版扁平 netconfig.json 字段。
    /// </summary>
    public static class ObjectSyncConfigLoader
    {
        public const string ConfigFileName = "objectsync_config.json";
        private static ObjectSyncGlobalConfig _cachedConfig;

        public static ObjectSyncGlobalConfig Current => _cachedConfig ?? (_cachedConfig = LoadRuntimeConfigSync());

        public static ObjectSyncGlobalConfig LoadRuntimeConfigSync()
        {
            ConfigRootPath rootPath = NetConfigLoader.LoadRuntimeRootSync();
            ObjectSyncGlobalConfig config = LoadSync(rootPath);
            _cachedConfig = config;
            return config;
        }

        public static async System.Threading.Tasks.Task<ObjectSyncGlobalConfig> LoadRuntimeConfigAsync()
        {
            ConfigRootPath rootPath = await NetConfigLoader.LoadRuntimeRootAsync();
            ObjectSyncGlobalConfig config = await LoadAsync(rootPath);
            _cachedConfig = config;
            return config;
        }

        public static ObjectSyncGlobalConfig LoadEditorSync(ConfigRootPath rootPath)
        {
            return LoadSync(rootPath);
        }

        public static string GetFullPath(ConfigRootPath rootPath)
        {
            string basePath = rootPath == ConfigRootPath.StreamingAssets
                ? Application.streamingAssetsPath
                : Application.persistentDataPath;
            if (string.IsNullOrEmpty(basePath))
            {
                return string.Empty;
            }

            return Path.Combine(basePath, NetConfigLoader.ConfigFolderName, ConfigFileName).Replace("\\", "/");
        }

        private static ObjectSyncGlobalConfig LoadSync(ConfigRootPath rootPath)
        {
            string fullPath = GetFullPath(rootPath);
            if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
            {
                string json = File.ReadAllText(fullPath);
                ObjectSyncGlobalConfig config = DeserializeOrDefault(json, fullPath, "ObjectSync Sync");
                Normalize(config, fullPath);
                return config;
            }

            return LoadLegacyFallback(rootPath, "ObjectSync LegacySync");
        }

        private static async System.Threading.Tasks.Task<ObjectSyncGlobalConfig> LoadAsync(ConfigRootPath rootPath)
        {
            string fullPath = GetFullPath(rootPath);
            if (!string.IsNullOrEmpty(fullPath))
            {
                if (rootPath == ConfigRootPath.StreamingAssets && Application.platform == RuntimePlatform.Android)
                {
                    using (var request = UnityWebRequest.Get(fullPath))
                    {
                        var operation = request.SendWebRequest();
                        while (!operation.isDone)
                        {
                            await System.Threading.Tasks.Task.Yield();
                        }

                        if (request.result == UnityWebRequest.Result.Success)
                        {
                            ObjectSyncGlobalConfig config = DeserializeOrDefault(request.downloadHandler.text, fullPath, "ObjectSync Android");
                            Normalize(config, fullPath);
                            return config;
                        }
                    }
                }
                else if (File.Exists(fullPath))
                {
                    string json = File.ReadAllText(fullPath);
                    ObjectSyncGlobalConfig config = DeserializeOrDefault(json, fullPath, "ObjectSync Async");
                    Normalize(config, fullPath);
                    return config;
                }
            }

            return LoadLegacyFallback(rootPath, "ObjectSync LegacyAsync");
        }

        private static ObjectSyncGlobalConfig LoadLegacyFallback(ConfigRootPath rootPath, string stage)
        {
            string legacyPath = GetLegacyRuntimeConfigPath(rootPath);
            if (string.IsNullOrEmpty(legacyPath) || !File.Exists(legacyPath))
            {
                ObjectSyncGlobalConfig config = new ObjectSyncGlobalConfig();
                Normalize(config, legacyPath);
                return config;
            }

            try
            {
                JObject root = JObject.Parse(File.ReadAllText(legacyPath));
                var config = new ObjectSyncGlobalConfig
                {
                    ReplayObjectSyncRecordIntervalTicks = root.TryGetValue("ReplayObjectSyncRecordIntervalTicks", out JToken replayRecordToken)
                        ? replayRecordToken.Value<int>()
                        : 3,
                    ObjectSyncOnlineIntervalTicks = root.TryGetValue("ObjectSyncOnlineIntervalTicks", out JToken onlineToken)
                        ? onlineToken.Value<int>()
                        : 2,
                    EnableAdaptiveObjectSync = !root.TryGetValue("EnableAdaptiveObjectSync", out JToken adaptiveToken) || adaptiveToken.Value<bool>(),
                    ObjectSyncFullResyncIntervalTicks = root.TryGetValue("ObjectSyncFullResyncIntervalTicks", out JToken fullResyncToken)
                        ? fullResyncToken.Value<int>()
                        : 60
                };
                Normalize(config, legacyPath);
                NetLogger.LogInfo("ObjectSyncConfigLoader", $"使用旧版扁平配置回退加载。Stage:{stage}, Path:{legacyPath}");
                return config;
            }
            catch (Exception ex)
            {
                NetLogger.LogWarning("ObjectSyncConfigLoader", $"旧版配置回退加载失败，已使用默认值。Stage:{stage}, Path:{legacyPath}, Error:{ex.Message}");
                ObjectSyncGlobalConfig config = new ObjectSyncGlobalConfig();
                Normalize(config, legacyPath);
                return config;
            }
        }

        private static ObjectSyncGlobalConfig DeserializeOrDefault(string json, string path, string stage)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                NetLogger.LogWarning("ObjectSyncConfigLoader", $"配置为空，使用默认值。Stage:{stage}, Path:{path}");
                return new ObjectSyncGlobalConfig();
            }

            try
            {
                ObjectSyncGlobalConfig config = JsonConvert.DeserializeObject<ObjectSyncGlobalConfig>(json);
                return config ?? new ObjectSyncGlobalConfig();
            }
            catch (Exception ex)
            {
                NetLogger.LogWarning("ObjectSyncConfigLoader", $"配置解析失败，使用默认值。Stage:{stage}, Path:{path}, Error:{ex.Message}");
                return new ObjectSyncGlobalConfig();
            }
        }

        private static void Normalize(ObjectSyncGlobalConfig config, string path)
        {
            if (config == null)
            {
                return;
            }

            if (config.ReplayObjectSyncRecordIntervalTicks < 0)
            {
                NetLogger.LogWarning("ObjectSyncConfigLoader",
                    $"配置修正: ReplayObjectSyncRecordIntervalTicks 非法，已回退默认值 3。Path:{path}, Value:{config.ReplayObjectSyncRecordIntervalTicks}");
                config.ReplayObjectSyncRecordIntervalTicks = 3;
            }

            if (config.ObjectSyncOnlineIntervalTicks <= 0)
            {
                NetLogger.LogWarning("ObjectSyncConfigLoader",
                    $"配置修正: ObjectSyncOnlineIntervalTicks 非法，已回退默认值 2。Path:{path}, Value:{config.ObjectSyncOnlineIntervalTicks}");
                config.ObjectSyncOnlineIntervalTicks = 2;
            }

            if (config.ObjectSyncFullResyncIntervalTicks <= 0)
            {
                NetLogger.LogWarning("ObjectSyncConfigLoader",
                    $"配置修正: ObjectSyncFullResyncIntervalTicks 非法，已回退默认值 60。Path:{path}, Value:{config.ObjectSyncFullResyncIntervalTicks}");
                config.ObjectSyncFullResyncIntervalTicks = 60;
            }
        }

        private static string GetLegacyRuntimeConfigPath(ConfigRootPath rootPath)
        {
            string basePath = rootPath == ConfigRootPath.StreamingAssets
                ? Application.streamingAssetsPath
                : Application.persistentDataPath;
            if (string.IsNullOrEmpty(basePath))
            {
                return string.Empty;
            }

            return Path.Combine(basePath, NetConfigLoader.ConfigFolderName, NetConfigLoader.ConfigFileName).Replace("\\", "/");
        }
    }
}
