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

        /// <summary>
        /// 录制上下文。
        /// </summary>
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

        /// <summary>
        /// 记录普通消息帧。
        /// 我保留你现有调用面不变，只在底层帧格式里补入 FrameKind 和占位 MsgId，
        /// 这样现有 Room.BroadcastMessage(recordToReplay) 逻辑不用整体重写。
        /// </summary>
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

            WriteFrameHeader(ctx.Writer, ReplayFrameKind.Message, tick, msgId, payloadLength);

            if (payloadLength > 0)
            {
                ctx.Writer.Write(payloadBuffer, 0, payloadLength);
            }
        }

        /// <summary>
        /// 记录对象关键帧。
        /// 我新增独立入口而不是复用普通消息帧写法，是为了明确区分“协议事件流”和“世界恢复点”两种完全不同的录像语义。
        /// </summary>
        public static void RecordObjectSnapshotFrame(string roomId, ReplayObjectSnapshotFrame snapshotFrame)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("ServerReplayStorage", "记录对象关键帧失败: roomId 为空");
                return;
            }

            if (snapshotFrame == null)
            {
                NetLogger.LogError("ServerReplayStorage", $"记录对象关键帧失败: snapshotFrame 为空, RoomId:{roomId}");
                return;
            }

            if (!ActiveRecords.TryGetValue(roomId, out RecordContext ctx) || ctx == null || ctx.Writer == null)
            {
                return;
            }

            if (snapshotFrame.Tick < 0)
            {
                NetLogger.LogError("ServerReplayStorage", $"记录对象关键帧失败: Tick 非法, RoomId:{roomId}, Tick:{snapshotFrame.Tick}");
                return;
            }

            byte[] payload = EncodeSnapshotFrame(snapshotFrame);
            if (payload == null)
            {
                NetLogger.LogError("ServerReplayStorage", $"记录对象关键帧失败: payload 为空, RoomId:{roomId}, Tick:{snapshotFrame.Tick}");
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

        /// <summary>
        /// 统一写入录像帧头。
        /// 我显式把 FrameKind 写到每一帧头部，是为了让播放器顺序扫 Raw 文件时能区分“普通协议补播”和“对象世界恢复点”。
        /// </summary>
        private static void WriteFrameHeader(BinaryWriter writer, ReplayFrameKind frameKind, int tick, int msgId, int payloadLength)
        {
            if (writer == null)
            {
                NetLogger.LogError("ServerReplayStorage", $"写入帧头失败: writer 为空, FrameKind:{frameKind}, Tick:{tick}, MsgId:{msgId}, PayloadLength:{payloadLength}");
                return;
            }

            writer.Write((byte)frameKind);
            writer.Write(tick);
            writer.Write(msgId);
            writer.Write(payloadLength);
        }

        /// <summary>
        /// 我先在内存中编码关键帧，再一次性写入 Raw 流，
        /// 是为了让录像底层保持“统一帧头 + 原始 payload”的简单布局，便于客户端后续建索引和快速 Seek。
        /// </summary>
        private static byte[] EncodeSnapshotFrame(ReplayObjectSnapshotFrame snapshotFrame)
        {
            if (snapshotFrame == null)
            {
                NetLogger.LogError("ServerReplayStorage", "编码对象关键帧失败: snapshotFrame 为空");
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