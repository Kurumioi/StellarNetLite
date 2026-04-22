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
    /// 回放扩展全局配置。
    /// </summary>
    [Serializable]
    public sealed class ReplayGlobalConfig
    {
        public int MaxReplayFiles = 100;
        public bool EnableReplayRecording = true;
    }

    /// <summary>
    /// 回放配置加载器。
    /// 与 Runtime 的 NetConfig 分离存储，且兼容旧版扁平 netconfig.json 字段。
    /// </summary>
    public static class ReplayConfigLoader
    {
        public const string ConfigFileName = "replay_config.json";
        private static ReplayGlobalConfig _cachedConfig;

        public static ReplayGlobalConfig Current => _cachedConfig ?? (_cachedConfig = LoadRuntimeConfigSync());

        public static ReplayGlobalConfig LoadRuntimeConfigSync()
        {
            ConfigRootPath rootPath = NetConfigLoader.LoadRuntimeRootSync();
            ReplayGlobalConfig config = LoadSync(rootPath);
            _cachedConfig = config;
            return config;
        }

        public static async System.Threading.Tasks.Task<ReplayGlobalConfig> LoadRuntimeConfigAsync()
        {
            ConfigRootPath rootPath = await NetConfigLoader.LoadRuntimeRootAsync();
            ReplayGlobalConfig config = await LoadAsync(rootPath);
            _cachedConfig = config;
            return config;
        }

        public static ReplayGlobalConfig LoadEditorSync(ConfigRootPath rootPath)
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

        private static ReplayGlobalConfig LoadSync(ConfigRootPath rootPath)
        {
            string fullPath = GetFullPath(rootPath);
            if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
            {
                string json = File.ReadAllText(fullPath);
                ReplayGlobalConfig config = DeserializeOrDefault(json, fullPath, "ReplayConfig Sync");
                Normalize(config, fullPath);
                return config;
            }

            return LoadLegacyFallback(rootPath, "ReplayConfig LegacySync");
        }

        private static async System.Threading.Tasks.Task<ReplayGlobalConfig> LoadAsync(ConfigRootPath rootPath)
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
                            ReplayGlobalConfig config = DeserializeOrDefault(request.downloadHandler.text, fullPath, "ReplayConfig Android");
                            Normalize(config, fullPath);
                            return config;
                        }
                    }
                }
                else if (File.Exists(fullPath))
                {
                    string json = File.ReadAllText(fullPath);
                    ReplayGlobalConfig config = DeserializeOrDefault(json, fullPath, "ReplayConfig Async");
                    Normalize(config, fullPath);
                    return config;
                }
            }

            return LoadLegacyFallback(rootPath, "ReplayConfig LegacyAsync");
        }

        private static ReplayGlobalConfig LoadLegacyFallback(ConfigRootPath rootPath, string stage)
        {
            string legacyPath = GetLegacyRuntimeConfigPath(rootPath);
            if (string.IsNullOrEmpty(legacyPath) || !File.Exists(legacyPath))
            {
                ReplayGlobalConfig config = new ReplayGlobalConfig();
                Normalize(config, legacyPath);
                return config;
            }

            try
            {
                JObject root = JObject.Parse(File.ReadAllText(legacyPath));
                var config = new ReplayGlobalConfig
                {
                    MaxReplayFiles = root.TryGetValue("MaxReplayFiles", out JToken maxReplayToken) ? maxReplayToken.Value<int>() : 100,
                    EnableReplayRecording = root.TryGetValue("EnableReplayRecording", out JToken enabledToken)
                        ? enabledToken.Value<bool>()
                        : true
                };
                Normalize(config, legacyPath);
                NetLogger.LogInfo("ReplayConfigLoader", $"使用旧版扁平配置回退加载。Stage:{stage}, Path:{legacyPath}");
                return config;
            }
            catch (Exception ex)
            {
                NetLogger.LogWarning("ReplayConfigLoader", $"旧版配置回退加载失败，已使用默认值。Stage:{stage}, Path:{legacyPath}, Error:{ex.Message}");
                ReplayGlobalConfig config = new ReplayGlobalConfig();
                Normalize(config, legacyPath);
                return config;
            }
        }

        private static ReplayGlobalConfig DeserializeOrDefault(string json, string path, string stage)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                NetLogger.LogWarning("ReplayConfigLoader", $"配置为空，使用默认值。Stage:{stage}, Path:{path}");
                return new ReplayGlobalConfig();
            }

            try
            {
                ReplayGlobalConfig config = JsonConvert.DeserializeObject<ReplayGlobalConfig>(json);
                return config ?? new ReplayGlobalConfig();
            }
            catch (Exception ex)
            {
                NetLogger.LogWarning("ReplayConfigLoader", $"配置解析失败，使用默认值。Stage:{stage}, Path:{path}, Error:{ex.Message}");
                return new ReplayGlobalConfig();
            }
        }

        private static void Normalize(ReplayGlobalConfig config, string path)
        {
            if (config == null)
            {
                return;
            }

            if (config.MaxReplayFiles < 0)
            {
                NetLogger.LogWarning("ReplayConfigLoader", $"配置修正: MaxReplayFiles 非法，已回退默认值 100。Path:{path}, Value:{config.MaxReplayFiles}");
                config.MaxReplayFiles = 100;
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
