using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Replay;

namespace StellarNet.Lite.Server.Infrastructure
{
    /// <summary>
    /// 服务端录像存储器。
    /// 负责录制帧、保存成品录像和滚动清理。
    /// </summary>
    public static class ServerReplayStorage
    {
        public const string ReplayFolderName = "Replays";
        private const int MessageFrameMsgIdPlaceholder = 0;
        private const int FrameHeaderBytes = sizeof(byte) + sizeof(int) + sizeof(int) + sizeof(int);
        private static string _replayFolderPath = string.Empty;

        private sealed class ReplayWriteItem
        {
            public ReplayFrameKind FrameKind;
            public int Tick;
            public int MsgId;
            public byte[] PayloadBuffer;
            public int PayloadLength;
        }

        private sealed class RecordContext
        {
            public FileStream FS;
            public GZipStream GZ;
            public BinaryWriter Writer;
            public string TempFilePath;
            public BlockingCollection<ReplayWriteItem> PendingWrites;
            public Task WorkerTask;
            public Exception WorkerException;
            public long PendingBytes;
        }

        private static readonly ConcurrentDictionary<string, RecordContext> ActiveRecords = new ConcurrentDictionary<string, RecordContext>();

        public static void InitializePaths(string persistentDataPath)
        {
            if (string.IsNullOrWhiteSpace(persistentDataPath))
            {
                NetLogger.LogError("ServerReplayStorage", "初始化路径失败: persistentDataPath 为空");
                _replayFolderPath = string.Empty;
                return;
            }

            _replayFolderPath = Path.Combine(persistentDataPath, ReplayFolderName).Replace("\\", "/");
        }

        public static string GetReplayFolderPath()
        {
            if (string.IsNullOrWhiteSpace(_replayFolderPath))
            {
                NetLogger.LogError("ServerReplayStorage", "录像目录尚未初始化，请先在主线程调用 InitializePaths");
                return string.Empty;
            }

            return _replayFolderPath;
        }

        public static void StartRecord(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("ServerReplayStorage", "启动录制失败: roomId 为空");
                return;
            }

            string folderPath = GetReplayFolderPath();
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
                CloseRecordContext(roomId, oldContext);
                TryDeleteTempFile(oldContext.TempFilePath);
                ActiveRecords.TryRemove(roomId, out _);
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
            var context = new RecordContext
            {
                FS = fs,
                GZ = gz,
                Writer = writer,
                TempFilePath = tempPath,
                PendingWrites = new BlockingCollection<ReplayWriteItem>(new ConcurrentQueue<ReplayWriteItem>())
            };
            context.WorkerTask = Task.Run(() => ProcessWriteQueue(roomId, context));
            ActiveRecords[roomId] = context;
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

            TryEnqueueFrame(ctx, roomId, ReplayFrameKind.Message, tick, msgId, payloadBuffer, payloadLength);
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

            TryEnqueueFrame(ctx, roomId, ReplayFrameKind.ObjectSnapshot, snapshotFrame.Tick, MessageFrameMsgIdPlaceholder, payload, payload.Length);
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

            ActiveRecords.TryRemove(roomId, out _);
            CloseRecordContext(roomId, ctx);
            if (ctx.WorkerException != null)
            {
                NetLogger.LogError("ServerReplayStorage", $"结束录制失败: 后台写入线程异常, RoomId:{roomId}, ReplayId:{replayId}, Error:{ctx.WorkerException.Message}");
                TryDeleteTempFile(ctx.TempFilePath);
                return;
            }

            string folderPath = GetReplayFolderPath();
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
                headerWriter.Write(ReplayFormatDefines.VersionWithTickRate);
                headerWriter.Write(finalDisplayName);
                headerWriter.Write(roomId);
                int compCount = componentIds?.Length ?? 0;
                headerWriter.Write(compCount);
                for (int i = 0; i < compCount; i++)
                {
                    headerWriter.Write(componentIds[i]);
                }

                headerWriter.Write(totalTicks);
                int recordedTickRate = config != null && config.TickRate > 0
                    ? config.TickRate
                    : ReplayFormatDefines.DefaultTickRateFallback;
                headerWriter.Write(recordedTickRate);
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

            ActiveRecords.TryRemove(roomId, out _);
            CloseRecordContext(roomId, ctx);
            TryDeleteTempFile(ctx.TempFilePath);
        }

        private static void TryEnqueueFrame(RecordContext ctx, string roomId, ReplayFrameKind frameKind, int tick, int msgId, byte[] payloadBuffer,
            int payloadLength)
        {
            if (ctx == null || ctx.PendingWrites == null)
            {
                return;
            }

            if (ctx.WorkerException != null)
            {
                return;
            }

            byte[] payloadCopy = null;
            if (payloadLength > 0)
            {
                payloadCopy = ArrayPool<byte>.Shared.Rent(payloadLength);
                Buffer.BlockCopy(payloadBuffer, 0, payloadCopy, 0, payloadLength);
            }

            var item = new ReplayWriteItem
            {
                FrameKind = frameKind,
                Tick = tick,
                MsgId = msgId,
                PayloadBuffer = payloadCopy,
                PayloadLength = payloadLength
            };

            try
            {
                ctx.PendingWrites.Add(item);
                Interlocked.Add(ref ctx.PendingBytes, FrameHeaderBytes + payloadLength);
            }
            catch (Exception ex)
            {
                if (payloadCopy != null)
                {
                    ArrayPool<byte>.Shared.Return(payloadCopy);
                }

                NetLogger.LogError("ServerReplayStorage", $"录像入队失败: RoomId:{roomId}, Tick:{tick}, MsgId:{msgId}, Error:{ex.Message}");
            }
        }

        private static void ProcessWriteQueue(string roomId, RecordContext ctx)
        {
            if (ctx == null || ctx.PendingWrites == null || ctx.Writer == null)
            {
                return;
            }

            try
            {
                foreach (ReplayWriteItem item in ctx.PendingWrites.GetConsumingEnumerable())
                {
                    try
                    {
                        WriteFrameHeader(ctx.Writer, item.FrameKind, item.Tick, item.MsgId, item.PayloadLength);
                        if (item.PayloadLength > 0)
                        {
                            ctx.Writer.Write(item.PayloadBuffer, 0, item.PayloadLength);
                        }
                    }
                    finally
                    {
                        if (item.PayloadBuffer != null)
                        {
                            ArrayPool<byte>.Shared.Return(item.PayloadBuffer);
                        }

                        Interlocked.Add(ref ctx.PendingBytes, -(FrameHeaderBytes + item.PayloadLength));
                    }
                }

                ctx.Writer.Flush();
                ctx.GZ?.Flush();
                ctx.FS?.Flush(true);
            }
            catch (Exception ex)
            {
                ctx.WorkerException = ex;
                NetLogger.LogError("ServerReplayStorage", $"后台写入录像失败: RoomId:{roomId}, Error:{ex.Message}");
                try
                {
                    ctx.PendingWrites.CompleteAdding();
                }
                catch
                {
                }
            }
            finally
            {
                while (ctx.PendingWrites.TryTake(out ReplayWriteItem remaining))
                {
                    if (remaining.PayloadBuffer != null)
                    {
                        ArrayPool<byte>.Shared.Return(remaining.PayloadBuffer);
                    }
                }
            }
        }

        private static void CloseRecordContext(string roomId, RecordContext ctx)
        {
            if (ctx == null)
            {
                return;
            }

            try
            {
                ctx.PendingWrites?.CompleteAdding();
            }
            catch
            {
            }

            try
            {
                ctx.WorkerTask?.Wait();
            }
            catch (AggregateException ex)
            {
                ctx.WorkerException = ex.Flatten().InnerException ?? ex;
                NetLogger.LogError("ServerReplayStorage", $"等待录像后台线程结束失败: RoomId:{roomId}, Error:{ctx.WorkerException.Message}");
            }

            ctx.Writer?.Dispose();
            ctx.GZ?.Dispose();
            ctx.FS?.Dispose();
            ctx.PendingWrites?.Dispose();
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
