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
    public class ReplayDownloadTask : IDisposable
    {
        public string ReplayId;
        public FileStream FS;
        public long TotalLength;
        public long CurrentOffset;

        public void Dispose()
        {
            if (FS != null)
            {
                FS.Dispose();
                FS = null;
            }
        }
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
            var replyList = new List<ReplayBriefInfo>();

            if (Directory.Exists(folderPath))
            {
                try
                {
                    var files = new DirectoryInfo(folderPath).GetFiles("*.replay")
                        .OrderByDescending(f => f.CreationTimeUtc)
                        .Take(10)
                        .ToArray();

                    foreach (var f in files)
                    {
                        string nameWithoutExt = Path.GetFileNameWithoutExtension(f.Name);
                        var parts = nameWithoutExt.Split('@');

                        string rId = parts[0];
                        string dName = rId;
                        int tTicks = 0;

                        if (parts.Length > 1)
                        {
                            try
                            {
                                string b64 = parts[1].Replace('-', '+').Replace('_', '/');
                                int mod4 = b64.Length % 4;
                                if (mod4 > 0) b64 += new string('=', 4 - mod4);
                                dName = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                            }
                            catch
                            {
                                dName = "名称解析失败";
                            }
                        }

                        // 核心升级：0 I/O 解析文件命中携带的 TotalTicks
                        if (parts.Length > 2)
                        {
                            int.TryParse(parts[2], out tTicks);
                        }

                        replyList.Add(new ReplayBriefInfo
                        {
                            ReplayId = rId,
                            DisplayName = dName,
                            Timestamp = new DateTimeOffset(f.CreationTimeUtc).ToUnixTimeSeconds(),
                            TotalTicks = tTicks
                        });
                    }
                }
                catch (Exception e)
                {
                    NetLogger.LogError("ServerReplayModule", $"读取录像列表异常: {e.Message}", "-", session.SessionId);
                }
            }

            var res = new S2C_ReplayList { Replays = replyList.ToArray() };
            _app.SendMessageToSession(session, res);
        }

        [NetHandler]
        public void OnC2S_RenameReplay(Session session, C2S_RenameReplay msg)
        {
            if (session == null || msg == null || string.IsNullOrEmpty(msg.ReplayId)) return;

            string folderPath = Path.Combine(Application.persistentDataPath, ServerReplayStorage.ReplayFolderName).Replace("\\", "/");
            if (!Directory.Exists(folderPath)) return;

            try
            {
                var files = new DirectoryInfo(folderPath).GetFiles($"{msg.ReplayId}@*.replay");
                if (files.Length == 0)
                {
                    files = new DirectoryInfo(folderPath).GetFiles($"{msg.ReplayId}.replay");
                }

                if (files.Length > 0)
                {
                    if ((DateTime.UtcNow - files[0].CreationTimeUtc).TotalMinutes > 5)
                    {
                        NetLogger.LogWarning("ServerReplayModule", $"重命名拦截: 录像 {msg.ReplayId} 已超过5分钟修改保护期", "-", session.SessionId);
                        return;
                    }

                    string oldPath = files[0].FullName;
                    string newName = string.IsNullOrEmpty(msg.NewName) ? "未命名录像" : msg.NewName;
                    string base64Name = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(newName));
                    string safeBase64Name = base64Name.Replace('+', '-').Replace('/', '_');

                    // 保持原有的 TotalTicks 后缀（如果存在）
                    string nameWithoutExt = Path.GetFileNameWithoutExtension(files[0].Name);
                    var parts = nameWithoutExt.Split('@');
                    string ticksSuffix = parts.Length > 2 ? $"@{parts[2]}" : "";

                    string newPath = Path.Combine(folderPath, $"{msg.ReplayId}@{safeBase64Name}{ticksSuffix}.replay").Replace("\\", "/");
                    File.Move(oldPath, newPath);

                    NetLogger.LogInfo("ServerReplayModule", $"录像重命名成功: {msg.ReplayId} -> {newName}", "-", session.SessionId);
                }
            }
            catch (Exception e)
            {
                NetLogger.LogError("ServerReplayModule", $"重命名录像异常: {e.Message}", "-", session.SessionId);
            }
        }

        [NetHandler]
        public void OnC2S_DownloadReplay(Session session, C2S_DownloadReplay msg)
        {
            if (session == null || msg == null || string.IsNullOrEmpty(msg.ReplayId)) return;

            CleanupDeadTasks();

            if (_downloadTasks.TryGetValue(session.SessionId, out var oldTask))
            {
                oldTask.Dispose();
                _downloadTasks.Remove(session.SessionId);
            }

            string folderPath = Path.Combine(Application.persistentDataPath, ServerReplayStorage.ReplayFolderName).Replace("\\", "/");
            var files = new DirectoryInfo(folderPath).GetFiles($"{msg.ReplayId}@*.replay");
            if (files.Length == 0)
            {
                files = new DirectoryInfo(folderPath).GetFiles($"{msg.ReplayId}.replay");
            }

            if (files.Length == 0)
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
                string fullPath = files[0].FullName;
                var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                long totalLength = fs.Length;
                long startOffset = msg.StartOffset;

                if (startOffset < 0 || startOffset > totalLength)
                {
                    startOffset = 0;
                }

                fs.Position = startOffset;

                var task = new ReplayDownloadTask
                {
                    ReplayId = msg.ReplayId,
                    FS = fs,
                    TotalLength = totalLength,
                    CurrentOffset = startOffset
                };
                _downloadTasks[session.SessionId] = task;

                var startMsg = new S2C_DownloadReplayStart
                {
                    Success = true,
                    ReplayId = msg.ReplayId,
                    TotalBytes = (int)totalLength,
                    AcceptedOffset = (int)startOffset,
                    Reason = string.Empty
                };
                _app.SendMessageToSession(session, startMsg);

                SendNextChunk(session, task);
            }
            catch (Exception e)
            {
                NetLogger.LogError("ServerReplayModule", $"打开录像文件流异常: {e.Message}", "-", session.SessionId);
                var errorRes = new S2C_DownloadReplayStart { Success = false, ReplayId = msg.ReplayId, Reason = "服务器读取文件失败" };
                _app.SendMessageToSession(session, errorRes);
            }
        }

        [NetHandler]
        public void OnC2S_DownloadReplayChunkAck(Session session, C2S_DownloadReplayChunkAck msg)
        {
            if (session == null || msg == null || string.IsNullOrEmpty(msg.ReplayId)) return;

            if (_downloadTasks.TryGetValue(session.SessionId, out var task) && task.ReplayId == msg.ReplayId)
            {
                SendNextChunk(session, task);
            }
        }

        private void SendNextChunk(Session session, ReplayDownloadTask task)
        {
            long remaining = task.TotalLength - task.CurrentOffset;
            if (remaining <= 0)
            {
                task.Dispose();
                _downloadTasks.Remove(session.SessionId);
                return;
            }

            int size = (int)Math.Min(ChunkSize, remaining);
            byte[] chunk = new byte[size];

            try
            {
                int bytesRead = task.FS.Read(chunk, 0, size);
                if (bytesRead <= 0)
                {
                    task.Dispose();
                    _downloadTasks.Remove(session.SessionId);
                    return;
                }

                task.CurrentOffset += bytesRead;

                if (bytesRead < size)
                {
                    byte[] actualChunk = new byte[bytesRead];
                    Buffer.BlockCopy(chunk, 0, actualChunk, 0, bytesRead);
                    chunk = actualChunk;
                }

                var chunkMsg = new S2C_DownloadReplayChunk
                {
                    ReplayId = task.ReplayId,
                    ChunkData = chunk
                };
                _app.SendMessageToSession(session, chunkMsg);
            }
            catch (Exception e)
            {
                NetLogger.LogError("ServerReplayModule", $"读取录像块异常: {e.Message}", "-", session.SessionId);
                task.Dispose();
                _downloadTasks.Remove(session.SessionId);
            }
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
                if (_downloadTasks.TryGetValue(id, out var task))
                {
                    task.Dispose();
                    _downloadTasks.Remove(id);
                }
            }
        }
    }
}