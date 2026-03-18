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

                        replyList.Add(new ReplayBriefInfo
                        {
                            ReplayId = rId,
                            DisplayName = dName,
                            Timestamp = new DateTimeOffset(f.CreationTimeUtc).ToUnixTimeSeconds()
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
                    // 核心防御：时间窗口熔断。仅允许在录像生成后的 5 分钟内进行重命名，防止恶意玩家抓包篡改历史录像
                    if ((DateTime.UtcNow - files[0].CreationTimeUtc).TotalMinutes > 5)
                    {
                        NetLogger.LogWarning("ServerReplayModule", $"重命名拦截: 录像 {msg.ReplayId} 已超过5分钟修改保护期", "-", session.SessionId);
                        return;
                    }

                    string oldPath = files[0].FullName;
                    string newName = string.IsNullOrEmpty(msg.NewName) ? "未命名录像" : msg.NewName;

                    string base64Name = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(newName));
                    string safeBase64Name = base64Name.Replace('+', '-').Replace('/', '_');

                    string newPath = Path.Combine(folderPath, $"{msg.ReplayId}@{safeBase64Name}.replay").Replace("\\", "/");

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
            if (session == null || msg == null) return;
            if (string.IsNullOrEmpty(msg.ReplayId)) return;

            CleanupDeadTasks();

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
            if (session == null || msg == null || string.IsNullOrEmpty(msg.ReplayId)) return;

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