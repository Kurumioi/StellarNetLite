using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Infrastructure
{
    /// <summary>
    /// 服务端录像存储服务。
    /// 核心重构：引入 UrlSafe Base64 文件名编码，实现 0 I/O 的元数据读取。
    /// </summary>
    public static class ServerReplayStorage
    {
        public const string ReplayFolderName = "Replays";

        public static void SaveReplay(ReplayFile replay, NetConfig config)
        {
            if (replay == null || config == null)
            {
                NetLogger.LogError("[ServerReplayStorage] ", $" 保存失败: 传入的录像文件或配置为空");
                return;
            }

            if (string.IsNullOrEmpty(replay.ReplayId))
            {
                NetLogger.LogError("[ServerReplayStorage] ", $" 保存失败: ReplayId 为空");
                return;
            }

            string basePath = Application.persistentDataPath;
            string folderPath = Path.Combine(basePath, ReplayFolderName).Replace("\\", "/");

            try
            {
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                // 核心重构：对 DisplayName 进行 UrlSafe Base64 编码，防止破坏文件系统
                string displayName = string.IsNullOrEmpty(replay.DisplayName) ? "未命名录像" : replay.DisplayName;
                string base64Name = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(displayName));
                // 替换标准 Base64 中的特殊字符，使其变为 UrlSafe
                string safeBase64Name = base64Name.Replace('+', '-').Replace('/', '_');

                // 物理文件名格式：{ReplayId}@{SafeBase64Name}.replay
                string fileName = $"{replay.ReplayId}@{safeBase64Name}.replay";
                string fullPath = Path.Combine(folderPath, fileName).Replace("\\", "/");

                string json = JsonConvert.SerializeObject(replay, Formatting.None);
                byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);

                using (FileStream fs = new FileStream(fullPath, FileMode.Create))
                using (GZipStream gz = new GZipStream(fs, CompressionMode.Compress))
                {
                    gz.Write(jsonBytes, 0, jsonBytes.Length);
                }

                NetLogger.LogInfo($"[ServerReplayStorage] ", $" 录像压缩保存成功: {fileName}, 帧数: {replay.Frames.Count}");

                EnforceRollingLimit(folderPath, config.MaxReplayFiles);
            }
            catch (Exception e)
            {
                NetLogger.LogError($"[ServerReplayStorage] ", $" 录像保存异常: {e.Message}");
            }
        }

        private static void EnforceRollingLimit(string folderPath, int maxFiles)
        {
            if (maxFiles <= 0) return;

            try
            {
                var dirInfo = new DirectoryInfo(folderPath);
                var files = dirInfo.GetFiles("*.replay");

                if (files.Length <= maxFiles)
                {
                    return;
                }

                var sortedFiles = files.OrderByDescending(f => f.CreationTimeUtc).ToList();
                for (int i = maxFiles; i < sortedFiles.Count; i++)
                {
                    sortedFiles[i].Delete();
                    NetLogger.LogInfo($"[ServerReplayStorage] ", $" 滚动清理: 已删除过期录像文件 {sortedFiles[i].Name}");
                }
            }
            catch (Exception e)
            {
                NetLogger.LogError($"[ServerReplayStorage] ", $" 滚动清理异常: {e.Message}");
            }
        }
    }
}