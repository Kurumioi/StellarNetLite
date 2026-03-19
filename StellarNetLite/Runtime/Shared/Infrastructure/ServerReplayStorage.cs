using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

namespace StellarNet.Lite.Server.Infrastructure
{
    public static class ServerReplayStorage
    {
        public const string ReplayFolderName = "Replays";
        private const uint MagicBytes = 0x50455253;

        private sealed class RecordContext
        {
            public FileStream FS;
            public GZipStream GZ;
            public BinaryWriter Writer;
            public string TempFilePath;
        }

        private static readonly Dictionary<string, RecordContext> ActiveRecords = new Dictionary<string, RecordContext>();

        public static void StartRecord(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("ServerReplayStorage", "启动录制失败: roomId 为空");
                return;
            }

            string folderPath = Path.Combine(Application.persistentDataPath, ReplayFolderName).Replace("\\", "/");
            if (string.IsNullOrEmpty(folderPath))
            {
                NetLogger.LogError("ServerReplayStorage", $"启动录制失败: folderPath 为空, RoomId:{roomId}");
                return;
            }

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            if (ActiveRecords.TryGetValue(roomId, out RecordContext oldContext) && oldContext != null)
            {
                oldContext.Writer?.Dispose();
                oldContext.GZ?.Dispose();
                oldContext.FS?.Dispose();
                ActiveRecords.Remove(roomId);
            }

            string tempPath = Path.Combine(folderPath, $"{roomId}_temp.replay").Replace("\\", "/");
            if (string.IsNullOrEmpty(tempPath))
            {
                NetLogger.LogError("ServerReplayStorage", $"启动录制失败: tempPath 为空, RoomId:{roomId}, Folder:{folderPath}");
                return;
            }

            FileStream fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
            var gz = new GZipStream(fs, CompressionMode.Compress, true);
            var writer = new BinaryWriter(gz);

            ActiveRecords[roomId] = new RecordContext
            {
                FS = fs,
                GZ = gz,
                Writer = writer,
                TempFilePath = tempPath
            };
        }

        public static void RecordFrame(string roomId, int tick, int msgId, byte[] payloadBuffer, int payloadLength)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("ServerReplayStorage", $"记录帧失败: roomId 为空, Tick:{tick}, MsgId:{msgId}");
                return;
            }

            if (!ActiveRecords.TryGetValue(roomId, out RecordContext ctx) || ctx == null || ctx.Writer == null)
            {
                return;
            }

            if (payloadLength < 0)
            {
                NetLogger.LogError("ServerReplayStorage", $"记录帧失败: payloadLength 非法, RoomId:{roomId}, Tick:{tick}, MsgId:{msgId}, PayloadLength:{payloadLength}");
                return;
            }

            if (payloadLength > 0 && (payloadBuffer == null || payloadLength > payloadBuffer.Length))
            {
                NetLogger.LogError(
                    "ServerReplayStorage",
                    $"记录帧失败: payloadBuffer 非法, RoomId:{roomId}, Tick:{tick}, MsgId:{msgId}, PayloadLength:{payloadLength}, BufferLength:{payloadBuffer?.Length ?? 0}");
                return;
            }

            ctx.Writer.Write(tick);
            ctx.Writer.Write(msgId);
            ctx.Writer.Write(payloadLength);

            if (payloadLength > 0)
            {
                ctx.Writer.Write(payloadBuffer, 0, payloadLength);
            }
        }

        public static void StopRecordAndSave(string roomId, string replayId, string displayName, int[] componentIds, NetConfig config, int totalTicks)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("ServerReplayStorage", $"结束录制失败: roomId 为空, ReplayId:{replayId}");
                return;
            }

            if (!ActiveRecords.TryGetValue(roomId, out RecordContext ctx) || ctx == null)
            {
                NetLogger.LogWarning("ServerReplayStorage", $"结束录制跳过: 不存在活跃上下文, RoomId:{roomId}, ReplayId:{replayId}");
                return;
            }

            ActiveRecords.Remove(roomId);

            ctx.Writer?.Dispose();
            ctx.GZ?.Dispose();
            ctx.FS?.Dispose();

            string folderPath = Path.Combine(Application.persistentDataPath, ReplayFolderName).Replace("\\", "/");
            if (string.IsNullOrEmpty(folderPath))
            {
                NetLogger.LogError("ServerReplayStorage", $"结束录制失败: folderPath 为空, RoomId:{roomId}, ReplayId:{replayId}");
                TryDeleteTempFile(ctx.TempFilePath);
                return;
            }

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            string finalDisplayName = string.IsNullOrWhiteSpace(displayName) ? "未命名录像" : displayName.Trim();
            string safeBase64Name = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(finalDisplayName))
                .Replace('+', '-')
                .Replace('/', '_');

            string finalPath = Path.Combine(folderPath, $"{replayId}@{safeBase64Name}@{totalTicks}.replay").Replace("\\", "/");
            if (string.IsNullOrEmpty(finalPath))
            {
                NetLogger.LogError("ServerReplayStorage", $"结束录制失败: finalPath 为空, RoomId:{roomId}, ReplayId:{replayId}, Folder:{folderPath}");
                TryDeleteTempFile(ctx.TempFilePath);
                return;
            }

            byte[] headerBytes;
            using (var ms = new MemoryStream())
            using (var headerWriter = new BinaryWriter(ms))
            {
                headerWriter.Write(MagicBytes);
                headerWriter.Write((byte)2);
                headerWriter.Write(finalDisplayName);
                headerWriter.Write(roomId);

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

            using (var finalFs = new FileStream(finalPath, FileMode.Create, FileAccess.Write))
            {
                byte[] lengthBytes = BitConverter.GetBytes(headerBytes.Length);
                finalFs.Write(lengthBytes, 0, 4);
                finalFs.Write(headerBytes, 0, headerBytes.Length);

                if (string.IsNullOrEmpty(ctx.TempFilePath) || !File.Exists(ctx.TempFilePath))
                {
                    NetLogger.LogError("ServerReplayStorage", $"结束录制失败: 临时文件不存在, RoomId:{roomId}, ReplayId:{replayId}, TempPath:{ctx.TempFilePath}");
                    return;
                }

                using (var tempFs = new FileStream(ctx.TempFilePath, FileMode.Open, FileAccess.Read))
                {
                    tempFs.CopyTo(finalFs);
                }
            }

            TryDeleteTempFile(ctx.TempFilePath);

            NetLogger.LogInfo("ServerReplayStorage", $"录像保存成功: {Path.GetFileName(finalPath)}", roomId);
            EnforceRollingLimit(folderPath, config != null ? config.MaxReplayFiles : 100);
        }

        public static void AbortRecord(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("ServerReplayStorage", "中止录制失败: roomId 为空");
                return;
            }

            if (!ActiveRecords.TryGetValue(roomId, out RecordContext ctx) || ctx == null)
            {
                return;
            }

            ActiveRecords.Remove(roomId);

            ctx.Writer?.Dispose();
            ctx.GZ?.Dispose();
            ctx.FS?.Dispose();

            TryDeleteTempFile(ctx.TempFilePath);
        }

        private static void EnforceRollingLimit(string folderPath, int maxFiles)
        {
            if (string.IsNullOrEmpty(folderPath) || maxFiles <= 0 || !Directory.Exists(folderPath))
            {
                return;
            }

            FileInfo[] files = new DirectoryInfo(folderPath).GetFiles("*.replay");
            if (files.Length <= maxFiles)
            {
                return;
            }

            List<FileInfo> sortedFiles = files.OrderByDescending(f => f.CreationTimeUtc).ToList();
            for (int i = maxFiles; i < sortedFiles.Count; i++)
            {
                FileInfo file = sortedFiles[i];
                if (file == null)
                {
                    continue;
                }

                file.Delete();
                NetLogger.LogInfo("ServerReplayStorage", $"滚动清理: 已删除过期录像 {file.Name}");
            }
        }

        private static void TryDeleteTempFile(string tempPath)
        {
            if (string.IsNullOrEmpty(tempPath))
            {
                return;
            }

            if (!File.Exists(tempPath))
            {
                return;
            }

            File.Delete(tempPath);
        }
    }
}