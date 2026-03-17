using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Server.Infrastructure;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Modules
{
    public class ReplayDownloadTask
    {
        public string ReplayId;
        public byte[] FileData;
        public int CurrentOffset;
    }

    [GlobalModule("ServerReplayModule", "录像下载与分发模块")]
    public sealed class ServerReplayModule
    {
        private readonly ServerApp _app;

        // 核心：每块 64KB，远低于 Mirror 300KB 的限制，绝对安全
        private const int ChunkSize = 64 * 1024;

        // 维护每个玩家的下载进度：SessionId -> Task
        private readonly Dictionary<string, ReplayDownloadTask> _downloadTasks = new Dictionary<string, ReplayDownloadTask>();

        public ServerReplayModule(ServerApp app)
        {
            _app = app;
        }

        [NetHandler]
        public void OnC2S_GetReplayList(Session session, C2S_GetReplayList msg)
        {
            if (session == null) return;
            string folderPath = Path.Combine(Application.persistentDataPath, ServerReplayStorage.ReplayFolderName).Replace("\\", "/");
            string[] replayIds = new string[0];

            if (Directory.Exists(folderPath))
            {
                try
                {
                    var files = new DirectoryInfo(folderPath).GetFiles("*.json")
                        .OrderByDescending(f => f.CreationTimeUtc)
                        .Take(10)
                        .ToArray();
                    replayIds = files.Select(f => Path.GetFileNameWithoutExtension(f.Name)).ToArray();
                }
                catch (Exception e)
                {
                    NetLogger.LogError("ServerReplayModule", $"读取录像列表异常: {e.Message}", "-", session.SessionId);
                }
            }

            var res = new S2C_ReplayList { ReplayIds = replayIds };
            _app.SendMessageToSession(session, res);
        }

        [NetHandler]
        public void OnC2S_DownloadReplay(Session session, C2S_DownloadReplay msg)
        {
            if (session == null || string.IsNullOrEmpty(msg.ReplayId)) return;

            // 1. 清理死任务，防止玩家断线导致内存泄漏
            CleanupDeadTasks();

            string folderPath = Path.Combine(Application.persistentDataPath, ServerReplayStorage.ReplayFolderName).Replace("\\", "/");
            string fullPath = Path.Combine(folderPath, $"{msg.ReplayId}.json").Replace("\\", "/");

            if (!File.Exists(fullPath))
            {
                NetLogger.LogWarning("ServerReplayModule", $"请求的录像文件不存在: {msg.ReplayId}", "-", session.SessionId);
                var notFoundRes = new S2C_DownloadReplayStart
                {
                    Success = false,
                    ReplayId = msg.ReplayId,
                    Reason = "录像文件不存在或已被清理"
                };
                _app.SendMessageToSession(session, notFoundRes);
                return;
            }

            try
            {
                // 一次性读入内存（几十MB内直接读 byte[] 性能最好）
                byte[] fileData = File.ReadAllBytes(fullPath);

                // 核心：处理断点续传的偏移量
                int startOffset = msg.StartOffset;
                if (startOffset < 0 || startOffset > fileData.Length)
                {
                    startOffset = 0; // 如果偏移量非法，强制从头开始
                }

                var task = new ReplayDownloadTask
                {
                    ReplayId = msg.ReplayId,
                    FileData = fileData,
                    CurrentOffset = startOffset
                };
                _downloadTasks[session.SessionId] = task;

                var startMsg = new S2C_DownloadReplayStart
                {
                    Success = true,
                    ReplayId = msg.ReplayId,
                    TotalBytes = fileData.Length,
                    AcceptedOffset = startOffset, // 核心新增：告诉客户端实际接受的偏移量
                    Reason = string.Empty
                };
                _app.SendMessageToSession(session, startMsg);

                NetLogger.LogInfo("ServerReplayModule", $"开始流式下发录像 {msg.ReplayId}, 总大小: {fileData.Length} bytes, 起始偏移: {startOffset} bytes", "-", session.SessionId);

                // 立刻发送第一个 Chunk
                SendNextChunk(session, task);
            }
            catch (Exception e)
            {
                NetLogger.LogError("ServerReplayModule", $"读取录像文件异常: {e.Message}", "-", session.SessionId);
                var errorRes = new S2C_DownloadReplayStart { Success = false, ReplayId = msg.ReplayId, Reason = "服务器读取文件失败" };
                _app.SendMessageToSession(session, errorRes);
            }
        }

        [NetHandler]
        public void OnC2S_DownloadReplayChunkAck(Session session, C2S_DownloadReplayChunkAck msg)
        {
            if (session == null || string.IsNullOrEmpty(msg.ReplayId)) return;

            // 收到客户端的确认后，继续发送下一块
            if (_downloadTasks.TryGetValue(session.SessionId, out var task) && task.ReplayId == msg.ReplayId)
            {
                SendNextChunk(session, task);
            }
        }

        private void SendNextChunk(Session session, ReplayDownloadTask task)
        {
            int remaining = task.FileData.Length - task.CurrentOffset;
            if (remaining <= 0)
            {
                // 传输完成
                _downloadTasks.Remove(session.SessionId);
                NetLogger.LogInfo("ServerReplayModule", $"录像 {task.ReplayId} 流式下发完成", "-", session.SessionId);
                return;
            }

            int size = Math.Min(ChunkSize, remaining);
            byte[] chunk = new byte[size];
            Buffer.BlockCopy(task.FileData, task.CurrentOffset, chunk, 0, size);

            task.CurrentOffset += size;

            var chunkMsg = new S2C_DownloadReplayChunk
            {
                ReplayId = task.ReplayId,
                ChunkData = chunk
            };
            _app.SendMessageToSession(session, chunkMsg);
        }

        private void CleanupDeadTasks()
        {
            var deadSessions = new List<string>();
            foreach (var kvp in _downloadTasks)
            {
                if (!_app.Sessions.TryGetValue(kvp.Key, out var activeSession) || !activeSession.IsOnline)
                {
                    deadSessions.Add(kvp.Key);
                }
            }

            foreach (var id in deadSessions)
            {
                _downloadTasks.Remove(id);
            }
        }
    }
}