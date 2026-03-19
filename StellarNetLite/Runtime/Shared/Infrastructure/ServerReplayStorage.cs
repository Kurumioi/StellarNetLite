using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Infrastructure
{
    public static class ServerReplayStorage
    {
        public const string ReplayFolderName = "Replays";
        private const uint MagicBytes = 0x50455253; // "SREP"

        private class RecordContext
        {
            public FileStream FS;
            public GZipStream GZ;
            public BinaryWriter Writer;
            public string TempFilePath;
        }

        private static readonly Dictionary<string, RecordContext> _activeRecords = new Dictionary<string, RecordContext>();

        public static void StartRecord(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return;

            string basePath = Application.persistentDataPath;
            string folderPath = Path.Combine(basePath, ReplayFolderName).Replace("\\", "/");
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            string tempPath = Path.Combine(folderPath, $"{roomId}_temp.replay").Replace("\\", "/");

            try
            {
                var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                var gz = new GZipStream(fs, CompressionMode.Compress, true);
                var writer = new BinaryWriter(gz);

                _activeRecords[roomId] = new RecordContext
                {
                    FS = fs,
                    GZ = gz,
                    Writer = writer,
                    TempFilePath = tempPath
                };
            }
            catch (Exception e)
            {
                NetLogger.LogError("[ServerReplayStorage]", $"启动流式录制异常: {e.Message}", roomId);
            }
        }

        public static void RecordFrame(string roomId, int tick, int msgId, byte[] payloadBuffer, int payloadLength)
        {
            if (!_activeRecords.TryGetValue(roomId, out var ctx)) return;

            try
            {
                ctx.Writer.Write(tick);
                ctx.Writer.Write(msgId);
                ctx.Writer.Write(payloadLength);

                if (payloadLength > 0 && payloadBuffer != null)
                {
                    ctx.Writer.Write(payloadBuffer, 0, payloadLength);
                }
            }
            catch (Exception e)
            {
                NetLogger.LogError("[ServerReplayStorage]", $"写入帧数据异常: {e.Message}", roomId);
            }
        }

        // 核心升级：接收 totalTicks 参数，并将其写入 Version 2 格式的 Header 与文件名中
        public static void StopRecordAndSave(string roomId, string replayId, string displayName, int[] componentIds, NetConfig config, int totalTicks)
        {
            if (!_activeRecords.TryGetValue(roomId, out var ctx)) return;

            _activeRecords.Remove(roomId);

            try
            {
                ctx.Writer.Dispose();
                ctx.GZ.Dispose();
                ctx.FS.Dispose();

                string folderPath = Path.Combine(Application.persistentDataPath, ReplayFolderName).Replace("\\", "/");
                string safeBase64Name = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(displayName))
                    .Replace('+', '-').Replace('/', '_');

                string finalPath = Path.Combine(folderPath, $"{replayId}@{safeBase64Name}@{totalTicks}.replay").Replace("\\", "/");

                using (var finalFs = new FileStream(finalPath, FileMode.Create, FileAccess.Write))
                {
                    byte[] headerBytes;
                    using (var ms = new MemoryStream())
                    using (var headerWriter = new BinaryWriter(ms))
                    {
                        headerWriter.Write(MagicBytes);
                        headerWriter.Write((byte)2);
                        headerWriter.Write(displayName ?? "");
                        headerWriter.Write(roomId ?? "");

                        int compCount = componentIds?.Length ?? 0;
                        headerWriter.Write(compCount);
                        for (int i = 0; i < compCount; i++)
                        {
                            headerWriter.Write(componentIds[i]);
                        }

                        headerWriter.Write(totalTicks);
                        headerWriter.Flush();
                        headerBytes = ms.ToArray();
                    }

                    byte[] lengthBytes = BitConverter.GetBytes(headerBytes.Length);
                    finalFs.Write(lengthBytes, 0, 4);
                    finalFs.Write(headerBytes, 0, headerBytes.Length);

                    using (var tempFs = new FileStream(ctx.TempFilePath, FileMode.Open, FileAccess.Read))
                    {
                        tempFs.CopyTo(finalFs);
                    }
                }

                // 核心修复：防御性删除，杜绝无意义的死代码引发异常
                if (File.Exists(ctx.TempFilePath))
                {
                    File.Delete(ctx.TempFilePath);
                }

                NetLogger.LogInfo("[ServerReplayStorage]", $"流式录像保存成功: {replayId}@{safeBase64Name}@{totalTicks}.replay", roomId);
                EnforceRollingLimit(folderPath, config.MaxReplayFiles);
            }
            catch (Exception e)
            {
                NetLogger.LogError("[ServerReplayStorage]", $"合并录像文件异常: {e.Message}", roomId);
            }
        }

        public static void AbortRecord(string roomId)
        {
            if (!_activeRecords.TryGetValue(roomId, out var ctx)) return;

            _activeRecords.Remove(roomId);

            try
            {
                ctx.Writer.Dispose();
                ctx.GZ.Dispose();
                ctx.FS.Dispose();

                if (File.Exists(ctx.TempFilePath))
                {
                    File.Delete(ctx.TempFilePath);
                }
            }
            catch (Exception e)
            {
                NetLogger.LogError("[ServerReplayStorage]", $"中止录像异常: {e.Message}", roomId);
            }
        }

        private static void EnforceRollingLimit(string folderPath, int maxFiles)
        {
            if (maxFiles <= 0) return;

            try
            {
                var dirInfo = new DirectoryInfo(folderPath);
                var files = dirInfo.GetFiles("*.replay");

                if (files.Length <= maxFiles) return;

                var sortedFiles = files.OrderByDescending(f => f.CreationTimeUtc).ToList();
                for (int i = maxFiles; i < sortedFiles.Count; i++)
                {
                    sortedFiles[i].Delete();
                    NetLogger.LogInfo("[ServerReplayStorage]", $"滚动清理: 已删除过期录像文件 {sortedFiles[i].Name}");
                }
            }
            catch (Exception e)
            {
                NetLogger.LogError("[ServerReplayStorage]", $"滚动清理异常: {e.Message}");
            }
        }
    }
}