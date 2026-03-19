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
    public class ReplayDownloadTask : IDisposable
    {
        public string ReplayId;
        public FileStream FS;
        public long TotalLength;
        public long CurrentOffset;
        public DateTime LastActiveTimeUtc;

        public void Dispose()
        {
            if (FS == null)
            {
                return;
            }

            FS.Dispose();
            FS = null;
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
                NetLogger.LogError("ServerReplayModule", "获取录像列表失败: session 为空");
                return;
            }

            string folderPath = Path.Combine(Application.persistentDataPath, ServerReplayStorage.ReplayFolderName).Replace("\\", "/");
            var replayList = new List<ReplayBriefInfo>();

            if (Directory.Exists(folderPath))
            {
                FileInfo[] files = new DirectoryInfo(folderPath)
                    .GetFiles("*.replay")
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .Take(10)
                    .ToArray();

                for (int i = 0; i < files.Length; i++)
                {
                    FileInfo file = files[i];
                    if (file == null)
                    {
                        continue;
                    }

                    string nameWithoutExt = Path.GetFileNameWithoutExtension(file.Name);
                    string[] parts = nameWithoutExt.Split('@');
                    string replayId = parts.Length > 0 ? parts[0] : string.Empty;
                    string displayName = replayId;
                    int totalTicks = 0;

                    if (parts.Length > 1)
                    {
                        string b64 = parts[1].Replace('-', '+').Replace('_', '/');
                        int mod4 = b64.Length % 4;
                        if (mod4 > 0)
                        {
                            b64 += new string('=', 4 - mod4);
                        }

                        string decodedName = "名称解析失败";
                        try
                        {
                            decodedName = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                        }
                        catch
                        {
                            decodedName = "名称解析失败";
                        }

                        displayName = decodedName;
                    }

                    if (parts.Length > 2)
                    {
                        int.TryParse(parts[2], out totalTicks);
                    }

                    replayList.Add(new ReplayBriefInfo
                    {
                        ReplayId = replayId,
                        DisplayName = displayName,
                        Timestamp = new DateTimeOffset(file.CreationTimeUtc).ToUnixTimeSeconds(),
                        TotalTicks = totalTicks
                    });
                }
            }

            _app.SendMessageToSession(session, new S2C_ReplayList
            {
                Replays = replayList.ToArray()
            });
        }

        [NetHandler]
        public void OnC2S_RenameReplay(Session session, C2S_RenameReplay msg)
        {
            if (session == null || msg == null || string.IsNullOrEmpty(msg.ReplayId))
            {
                NetLogger.LogError("ServerReplayModule", $"重命名录像失败: 参数非法, Session:{session?.SessionId ?? "null"}, ReplayId:{msg?.ReplayId ?? "null"}");
                return;
            }

            string folderPath = Path.Combine(Application.persistentDataPath, ServerReplayStorage.ReplayFolderName).Replace("\\", "/");
            if (!Directory.Exists(folderPath))
            {
                NetLogger.LogWarning("ServerReplayModule", $"重命名录像失败: 目录不存在, Folder:{folderPath}", "-", session.SessionId);
                return;
            }

            FileInfo[] files = new DirectoryInfo(folderPath).GetFiles($"{msg.ReplayId}@*.replay");
            if (files.Length == 0)
            {
                files = new DirectoryInfo(folderPath).GetFiles($"{msg.ReplayId}.replay");
            }

            if (files.Length == 0)
            {
                NetLogger.LogWarning("ServerReplayModule", $"重命名录像失败: 文件不存在, ReplayId:{msg.ReplayId}", "-", session.SessionId);
                return;
            }

            FileInfo targetFile = files[0];
            if ((DateTime.UtcNow - targetFile.CreationTimeUtc).TotalMinutes > 5)
            {
                NetLogger.LogWarning("ServerReplayModule", $"重命名拦截: 超过 5 分钟保护期, ReplayId:{msg.ReplayId}", "-", session.SessionId);
                return;
            }

            string newName = string.IsNullOrWhiteSpace(msg.NewName) ? "未命名录像" : msg.NewName.Trim();
            string base64Name = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(newName));
            string safeBase64Name = base64Name.Replace('+', '-').Replace('/', '_');

            string oldPath = targetFile.FullName.Replace("\\", "/");
            string nameWithoutExt = Path.GetFileNameWithoutExtension(targetFile.Name);
            string[] parts = nameWithoutExt.Split('@');
            string ticksSuffix = parts.Length > 2 ? $"@{parts[2]}" : string.Empty;

            string newPath = Path.Combine(folderPath, $"{msg.ReplayId}@{safeBase64Name}{ticksSuffix}.replay").Replace("\\", "/");
            File.Move(oldPath, newPath);

            NetLogger.LogInfo("ServerReplayModule", $"录像重命名成功: ReplayId:{msg.ReplayId}, NewName:{newName}", "-", session.SessionId);
        }

        [NetHandler]
        public void OnC2S_DownloadReplay(Session session, C2S_DownloadReplay msg)
        {
            if (session == null || msg == null || string.IsNullOrEmpty(msg.ReplayId))
            {
                NetLogger.LogError("ServerReplayModule", $"下载录像失败: 参数非法, Session:{session?.SessionId ?? "null"}, ReplayId:{msg?.ReplayId ?? "null"}");
                return;
            }

            CleanupDeadTasks();

            if (_downloadTasks.TryGetValue(session.SessionId, out ReplayDownloadTask oldTask))
            {
                oldTask.Dispose();
                _downloadTasks.Remove(session.SessionId);
            }

            string folderPath = Path.Combine(Application.persistentDataPath, ServerReplayStorage.ReplayFolderName).Replace("\\", "/");
            if (!Directory.Exists(folderPath))
            {
                NetLogger.LogWarning("ServerReplayModule", $"下载录像失败: 目录不存在, Folder:{folderPath}", "-", session.SessionId);
                _app.SendMessageToSession(session, new S2C_DownloadReplayStart
                {
                    Success = false,
                    ReplayId = msg.ReplayId,
                    Reason = "录像目录不存在"
                });
                return;
            }

            FileInfo[] files = new DirectoryInfo(folderPath).GetFiles($"{msg.ReplayId}@*.replay");
            if (files.Length == 0)
            {
                files = new DirectoryInfo(folderPath).GetFiles($"{msg.ReplayId}.replay");
            }

            if (files.Length == 0)
            {
                NetLogger.LogWarning("ServerReplayModule", $"下载录像失败: 文件不存在, ReplayId:{msg.ReplayId}", "-", session.SessionId);
                _app.SendMessageToSession(session, new S2C_DownloadReplayStart
                {
                    Success = false,
                    ReplayId = msg.ReplayId,
                    Reason = "录像文件不存在或已被清理"
                });
                return;
            }

            string fullPath = files[0].FullName.Replace("\\", "/");
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
                CurrentOffset = startOffset,
                LastActiveTimeUtc = DateTime.UtcNow
            };

            _downloadTasks[session.SessionId] = task;

            _app.SendMessageToSession(session, new S2C_DownloadReplayStart
            {
                Success = true,
                ReplayId = msg.ReplayId,
                TotalBytes = (int)totalLength,
                AcceptedOffset = (int)startOffset,
                Reason = string.Empty
            });

            SendNextChunk(session, task);
        }

        [NetHandler]
        public void OnC2S_DownloadReplayChunkAck(Session session, C2S_DownloadReplayChunkAck msg)
        {
            if (session == null || msg == null || string.IsNullOrEmpty(msg.ReplayId))
            {
                NetLogger.LogError("ServerReplayModule", $"处理分块确认失败: 参数非法, Session:{session?.SessionId ?? "null"}, ReplayId:{msg?.ReplayId ?? "null"}");
                return;
            }

            if (!_downloadTasks.TryGetValue(session.SessionId, out ReplayDownloadTask task) || task == null)
            {
                NetLogger.LogWarning("ServerReplayModule", $"处理分块确认跳过: 未找到下载任务, ReplayId:{msg.ReplayId}", "-", session.SessionId);
                return;
            }

            if (task.ReplayId != msg.ReplayId)
            {
                NetLogger.LogWarning(
                    "ServerReplayModule",
                    $"处理分块确认跳过: ReplayId 不匹配, Task:{task.ReplayId}, Msg:{msg.ReplayId}",
                    "-",
                    session.SessionId);
                return;
            }

            task.LastActiveTimeUtc = DateTime.UtcNow;
            SendNextChunk(session, task);
        }

        private void SendNextChunk(Session session, ReplayDownloadTask task)
        {
            if (session == null || task == null)
            {
                NetLogger.LogError("ServerReplayModule", $"发送录像分块失败: 参数为空, Session:{session?.SessionId ?? "null"}, TaskReplay:{task?.ReplayId ?? "null"}");
                return;
            }

            if (task.FS == null)
            {
                NetLogger.LogError("ServerReplayModule", $"发送录像分块失败: 文件流为空, ReplayId:{task.ReplayId}", "-", session.SessionId);
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

            _app.SendMessageToSession(session, new S2C_DownloadReplayChunk
            {
                ReplayId = task.ReplayId,
                ChunkData = actualChunk
            });
        }

        private void CleanupDeadTasks()
        {
            var deadSessions = new List<string>();

            foreach (KeyValuePair<string, ReplayDownloadTask> kvp in _downloadTasks)
            {
                string sessionId = kvp.Key;
                ReplayDownloadTask task = kvp.Value;

                if (task == null)
                {
                    deadSessions.Add(sessionId);
                    continue;
                }

                bool invalidSession = !_app.Sessions.TryGetValue(sessionId, out Session activeSession) || activeSession == null || !activeSession.IsOnline;
                bool timeout = (DateTime.UtcNow - task.LastActiveTimeUtc).TotalMinutes > 5;

                if (invalidSession || timeout)
                {
                    deadSessions.Add(sessionId);
                }
            }

            for (int i = 0; i < deadSessions.Count; i++)
            {
                CleanupTask(deadSessions[i]);
            }
        }

        private void CleanupTask(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            if (!_downloadTasks.TryGetValue(sessionId, out ReplayDownloadTask task))
            {
                return;
            }

            task?.Dispose();
            _downloadTasks.Remove(sessionId);
        }
    }
}