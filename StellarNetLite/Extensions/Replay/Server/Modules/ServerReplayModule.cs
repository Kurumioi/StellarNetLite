using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Server.Infrastructure;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;

namespace StellarNet.Lite.Server.Modules
{
    /// <summary>
    /// 单个会话的录像下载任务。
    /// </summary>
    public class ReplayDownloadTask : IDisposable
    {
        /// <summary>
        /// 当前下载中的录像 Id。
        /// </summary>
        public string ReplayId;

        /// <summary>
        /// 当前打开的录像文件流。
        /// </summary>
        public FileStream FS;

        /// <summary>
        /// 录像总字节数。
        /// </summary>
        public long TotalLength;

        /// <summary>
        /// 当前已发送到的偏移。
        /// </summary>
        public long CurrentOffset;

        /// <summary>
        /// 最近活跃时间。
        /// </summary>
        public DateTime LastActiveTimeUtc;

        public void Dispose()
        {
            FS?.Dispose();
            FS = null;
        }
    }

    [ServerModule("ServerReplayModule", "录像下载与分发模块")]
    /// <summary>
    /// 服务端录像模块。
    /// 负责录像列表、重命名和分片下载。
    /// </summary>
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
            string folderPath = Path.Combine(Application.persistentDataPath, ServerReplayStorage.ReplayFolderName).Replace("\\", "/");
            var replayList = new List<ReplayBriefInfo>();

            if (Directory.Exists(folderPath))
            {
                FileInfo[] files = new DirectoryInfo(folderPath).GetFiles("*.replay").OrderByDescending(f => f.CreationTimeUtc).Take(10).ToArray();
                for (int i = 0; i < files.Length; i++)
                {
                    string nameWithoutExt = Path.GetFileNameWithoutExtension(files[i].Name);
                    string[] parts = nameWithoutExt.Split('@');
                    string replayId = parts.Length > 0 ? parts[0] : string.Empty;
                    string displayName = replayId;
                    int totalTicks = 0;

                    if (parts.Length > 1)
                    {
                        string b64 = parts[1].Replace('-', '+').Replace('_', '/');
                        int mod4 = b64.Length % 4;
                        if (mod4 > 0) b64 += new string('=', 4 - mod4);
                        try
                        {
                            displayName = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                        }
                        catch
                        {
                            displayName = "名称解析失败";
                        }
                    }

                    if (parts.Length > 2) int.TryParse(parts[2], out totalTicks);

                    replayList.Add(new ReplayBriefInfo
                    {
                        ReplayId = replayId,
                        DisplayName = displayName,
                        Timestamp = new DateTimeOffset(files[i].CreationTimeUtc).ToUnixTimeSeconds(),
                        TotalTicks = totalTicks
                    });
                }
            }

            _app.SendMessageToSession(session, new S2C_ReplayList { Replays = replayList.ToArray() });
        }

        [NetHandler]
        public void OnC2S_RenameReplay(Session session, C2S_RenameReplay msg)
        {
            if (string.IsNullOrEmpty(msg.ReplayId)) return;

            string folderPath = Path.Combine(Application.persistentDataPath, ServerReplayStorage.ReplayFolderName).Replace("\\", "/");
            if (!Directory.Exists(folderPath)) return;

            FileInfo[] files = new DirectoryInfo(folderPath).GetFiles($"{msg.ReplayId}@*.replay");
            if (files.Length == 0) files = new DirectoryInfo(folderPath).GetFiles($"{msg.ReplayId}.replay");
            if (files.Length == 0) return;

            FileInfo targetFile = files[0];
            if ((DateTime.UtcNow - targetFile.CreationTimeUtc).TotalMinutes > 5) return;

            string newName = string.IsNullOrWhiteSpace(msg.NewName) ? "未命名录像" : msg.NewName.Trim();
            string safeBase64Name = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(newName)).Replace('+', '-').Replace('/', '_');

            string[] parts = Path.GetFileNameWithoutExtension(targetFile.Name).Split('@');
            string ticksSuffix = parts.Length > 2 ? $"@{parts[2]}" : string.Empty;
            string newPath = Path.Combine(folderPath, $"{msg.ReplayId}@{safeBase64Name}{ticksSuffix}.replay").Replace("\\", "/");

            File.Move(targetFile.FullName.Replace("\\", "/"), newPath);
        }

        [NetHandler]
        public void OnC2S_DownloadReplay(Session session, C2S_DownloadReplay msg)
        {
            if (string.IsNullOrEmpty(msg.ReplayId)) return;

            CleanupDeadTasks();
            if (_downloadTasks.TryGetValue(session.SessionId, out ReplayDownloadTask oldTask))
            {
                oldTask.Dispose();
                _downloadTasks.Remove(session.SessionId);
            }

            string folderPath = Path.Combine(Application.persistentDataPath, ServerReplayStorage.ReplayFolderName).Replace("\\", "/");
            if (!Directory.Exists(folderPath))
            {
                _app.SendMessageToSession(session, new S2C_DownloadReplayStart { Success = false, ReplayId = msg.ReplayId, Reason = "录像目录不存在" });
                return;
            }

            FileInfo[] files = new DirectoryInfo(folderPath).GetFiles($"{msg.ReplayId}@*.replay");
            if (files.Length == 0) files = new DirectoryInfo(folderPath).GetFiles($"{msg.ReplayId}.replay");
            if (files.Length == 0)
            {
                _app.SendMessageToSession(session, new S2C_DownloadReplayStart { Success = false, ReplayId = msg.ReplayId, Reason = "录像文件不存在" });
                return;
            }

            var fs = new FileStream(files[0].FullName.Replace("\\", "/"), FileMode.Open, FileAccess.Read, FileShare.Read);
            long startOffset = msg.StartOffset < 0 || msg.StartOffset > fs.Length ? 0 : msg.StartOffset;
            fs.Position = startOffset;

            var task = new ReplayDownloadTask
            {
                ReplayId = msg.ReplayId,
                FS = fs,
                TotalLength = fs.Length,
                CurrentOffset = startOffset,
                LastActiveTimeUtc = DateTime.UtcNow
            };
            _downloadTasks[session.SessionId] = task;

            _app.SendMessageToSession(session,
                new S2C_DownloadReplayStart
                    { Success = true, ReplayId = msg.ReplayId, TotalBytes = (int)fs.Length, AcceptedOffset = (int)startOffset });
            SendNextChunk(session, task);
        }

        [NetHandler]
        public void OnC2S_DownloadReplayChunkAck(Session session, C2S_DownloadReplayChunkAck msg)
        {
            if (!_downloadTasks.TryGetValue(session.SessionId, out ReplayDownloadTask task) || task.ReplayId != msg.ReplayId) return;
            task.LastActiveTimeUtc = DateTime.UtcNow;
            SendNextChunk(session, task);
        }

        private void SendNextChunk(Session session, ReplayDownloadTask task)
        {
            if (task.FS == null)
            {
                CleanupTask(session.SessionId);
                return;
            }

            long remaining = task.TotalLength - task.CurrentOffset;
            if (remaining <= 0)
            {
                CleanupTask(session.SessionId);
                return;
            }

            int size = (int)Math.Min(ChunkSize, remaining);
            byte[] chunk = new byte[size];
            int bytesRead = task.FS.Read(chunk, 0, size);

            if (bytesRead <= 0)
            {
                CleanupTask(session.SessionId);
                return;
            }

            task.CurrentOffset += bytesRead;
            task.LastActiveTimeUtc = DateTime.UtcNow;

            byte[] actualChunk = chunk;
            if (bytesRead < size)
            {
                actualChunk = new byte[bytesRead];
                Buffer.BlockCopy(chunk, 0, actualChunk, 0, bytesRead);
            }

            _app.SendMessageToSession(session, new S2C_DownloadReplayChunk { ReplayId = task.ReplayId, ChunkData = actualChunk });
        }

        private void CleanupDeadTasks()
        {
            var deadSessions = new List<string>();
            foreach (var kvp in _downloadTasks)
            {
                if (kvp.Value == null || !_app.Sessions.TryGetValue(kvp.Key, out Session activeSession) || !activeSession.IsOnline ||
                    (DateTime.UtcNow - kvp.Value.LastActiveTimeUtc).TotalMinutes > 5)
                {
                    deadSessions.Add(kvp.Key);
                }
            }

            for (int i = 0; i < deadSessions.Count; i++) CleanupTask(deadSessions[i]);
        }

        private void CleanupTask(string sessionId)
        {
            if (_downloadTasks.TryGetValue(sessionId, out ReplayDownloadTask task))
            {
                task?.Dispose();
                _downloadTasks.Remove(sessionId);
            }
        }
    }
}
