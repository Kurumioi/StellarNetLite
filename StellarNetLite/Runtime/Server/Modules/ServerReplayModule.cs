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

    [ServerModule("ServerReplayModule", "录像下载与分发模块")]
    public sealed class ServerReplayModule
    {
        private readonly ServerApp _app;
        private const int ChunkSize = 64 * 1024;
        private readonly Dictionary<string, ReplayDownloadTask> _downloadTasks = new Dictionary<string, ReplayDownloadTask>();

        public ServerReplayModule(ServerApp app)
        {
            _app = app;
        }

        [NetHandler]
        public void OnC2S_GetReplayList(Session session, C2S_GetReplayList msg)
        {
            if (session == null)
            {
                NetLogger.LogError("ServerReplayModule", "收到非法请求: Session 为空");
                return;
            }

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
            if (session == null || msg == null)
            {
                NetLogger.LogError("ServerReplayModule", "收到非法请求: Session 或 Msg 为空");
                return;
            }

            if (string.IsNullOrEmpty(msg.ReplayId))
            {
                NetLogger.LogError("ServerReplayModule", "收到非法请求: ReplayId 为空", "-", session.SessionId);
                return;
            }

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
                byte[] fileData = File.ReadAllBytes(fullPath);
                int startOffset = msg.StartOffset;

                if (startOffset < 0 || startOffset > fileData.Length)
                {
                    startOffset = 0;
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
                    AcceptedOffset = startOffset,
                    Reason = string.Empty
                };

                _app.SendMessageToSession(session, startMsg);
                NetLogger.LogInfo("ServerReplayModule", $"开始流式下发录像 {msg.ReplayId}, 总大小: {fileData.Length} bytes, 起始偏移: {startOffset} bytes", "-", session.SessionId);

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
            if (session == null || msg == null || string.IsNullOrEmpty(msg.ReplayId))
            {
                NetLogger.LogError("ServerReplayModule", "收到非法请求: Session 或 ReplayId 为空");
                return;
            }

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