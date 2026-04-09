using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Replay;
using UnityEngine;

namespace StellarNet.Lite.Server.Infrastructure
{
    public static class ServerReplayStorage
    {
        public const string ReplayFolderName = "Replays";
        private const int MessageFrameMsgIdPlaceholder = 0;

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
            GZipStream gz = new GZipStream(fs, CompressionMode.Compress, true);
            BinaryWriter writer = new BinaryWriter(gz);
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

            if (tick < 0)
            {
                NetLogger.LogError("ServerReplayStorage", $"记录帧失败: tick 非法, RoomId:{roomId}, Tick:{tick}, MsgId:{msgId}");
                return;
            }

            if (payloadLength < 0)
            {
                NetLogger.LogError("ServerReplayStorage",
                    $"记录帧失败: payloadLength 非法, RoomId:{roomId}, Tick:{tick}, MsgId:{msgId}, PayloadLength:{payloadLength}");
                return;
            }

            if (payloadLength > 0 && (payloadBuffer == null || payloadLength > payloadBuffer.Length))
            {
                NetLogger.LogError(
                    "ServerReplayStorage",
                    $"记录帧失败: payloadBuffer 非法, RoomId:{roomId}, Tick:{tick}, MsgId:{msgId}, PayloadLength:{payloadLength}, BufferLength:{payloadBuffer?.Length ?? 0}");
                return;
            }

            WriteFrameHeader(ctx.Writer, ReplayFrameKind.Message, tick, msgId, payloadLength);
            if (payloadLength > 0)
            {
                ctx.Writer.Write(payloadBuffer, 0, payloadLength);
            }
        }

        public static void RecordSnapshotFrame(string roomId, ReplaySnapshotFrame snapshotFrame)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("ServerReplayStorage", "记录快照关键帧失败: roomId 为空");
                return;
            }

            if (snapshotFrame == null)
            {
                NetLogger.LogError("ServerReplayStorage", $"记录快照关键帧失败: snapshotFrame 为空, RoomId:{roomId}");
                return;
            }

            if (!ActiveRecords.TryGetValue(roomId, out RecordContext ctx) || ctx == null || ctx.Writer == null)
            {
                return;
            }

            if (snapshotFrame.Tick < 0)
            {
                NetLogger.LogError("ServerReplayStorage", $"记录快照关键帧失败: Tick 非法, RoomId:{roomId}, Tick:{snapshotFrame.Tick}");
                return;
            }

            byte[] payload = EncodeSnapshotFrame(snapshotFrame);
            if (payload == null)
            {
                NetLogger.LogError("ServerReplayStorage", $"记录快照关键帧失败: payload 为空, RoomId:{roomId}, Tick:{snapshotFrame.Tick}");
                return;
            }

            WriteFrameHeader(ctx.Writer, ReplayFrameKind.ObjectSnapshot, snapshotFrame.Tick, MessageFrameMsgIdPlaceholder, payload.Length);
            if (payload.Length > 0)
            {
                ctx.Writer.Write(payload, 0, payload.Length);
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
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter headerWriter = new BinaryWriter(ms))
            {
                headerWriter.Write(ReplayFormatDefines.MagicBytes);
                headerWriter.Write(ReplayFormatDefines.VersionWithObjectSnapshot);
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

            using (FileStream finalFs = new FileStream(finalPath, FileMode.Create, FileAccess.Write))
            {
                byte[] lengthBytes = BitConverter.GetBytes(headerBytes.Length);
                finalFs.Write(lengthBytes, 0, 4);
                finalFs.Write(headerBytes, 0, headerBytes.Length);
                if (string.IsNullOrEmpty(ctx.TempFilePath) || !File.Exists(ctx.TempFilePath))
                {
                    NetLogger.LogError("ServerReplayStorage", $"结束录制失败: 临时文件不存在, RoomId:{roomId}, ReplayId:{replayId}, TempPath:{ctx.TempFilePath}");
                    return;
                }

                using (FileStream tempFs = new FileStream(ctx.TempFilePath, FileMode.Open, FileAccess.Read))
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

        private static void WriteFrameHeader(BinaryWriter writer, ReplayFrameKind frameKind, int tick, int msgId, int payloadLength)
        {
            if (writer == null)
            {
                NetLogger.LogError("ServerReplayStorage",
                    $"写入帧头失败: writer 为空, FrameKind:{frameKind}, Tick:{tick}, MsgId:{msgId}, PayloadLength:{payloadLength}");
                return;
            }

            writer.Write((byte)frameKind);
            writer.Write(tick);
            writer.Write(msgId);
            writer.Write(payloadLength);
        }

        private static byte[] EncodeSnapshotFrame(ReplaySnapshotFrame snapshotFrame)
        {
            if (snapshotFrame == null)
            {
                NetLogger.LogError("ServerReplayStorage", "编码快照关键帧失败: snapshotFrame 为空");
                return null;
            }

            using (MemoryStream ms = new MemoryStream(2048))
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                snapshotFrame.Serialize(writer);
                writer.Flush();
                return ms.ToArray();
            }
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