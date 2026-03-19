using System;
using System.IO;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Client.Core.Events;

namespace StellarNet.Lite.Client.Modules
{
    [ClientModule("ClientReplayModule", "客户端回放模块")]
    public sealed class ClientReplayModule
    {
        private readonly ClientApp _app;
        private string _downloadingReplayId;
        private int _expectedTotalBytes;
        private FileStream _fileStream;

        public static string CacheFolderPath => Path.Combine(Application.persistentDataPath, "ClientReplays").Replace("\\", "/");

        public ClientReplayModule(ClientApp app)
        {
            _app = app;
        }

        public static void RequestDownload(ClientApp app, string replayId)
        {
            if (!Directory.Exists(CacheFolderPath))
            {
                Directory.CreateDirectory(CacheFolderPath);
            }

            string finalPath = Path.Combine(CacheFolderPath, $"{replayId}.replay").Replace("\\", "/");
            string tmpPath = Path.Combine(CacheFolderPath, $"{replayId}.tmp").Replace("\\", "/");

            if (File.Exists(finalPath))
            {
                NetLogger.LogInfo("ClientReplayModule", $"命中本地缓存，直接加载录像: {replayId}");
                GlobalTypeNetEvent.Broadcast(new S2C_DownloadReplayResult
                {
                    Success = true,
                    ReplayId = replayId,
                    ReplayFileData = finalPath, // 核心修改：传递文件路径，而非全量 JSON 字符串
                    Reason = string.Empty
                });
                return;
            }

            int startOffset = 0;
            if (File.Exists(tmpPath))
            {
                startOffset = (int)new FileInfo(tmpPath).Length;
                NetLogger.LogInfo("ClientReplayModule", $"发现未完成的下载，发起断点续传请求，起始偏移: {startOffset} bytes");
            }

            app.SendMessage(new C2S_DownloadReplay { ReplayId = replayId, StartOffset = startOffset });
        }

        [NetHandler]
        public void OnS2C_ReplayList(S2C_ReplayList msg)
        {
            if (msg == null) return;
            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_DownloadReplayStart(S2C_DownloadReplayStart msg)
        {
            if (msg == null) return;

            CloseFileStream();

            if (!msg.Success)
            {
                NetLogger.LogError("ClientReplayModule", $"录像下载请求失败: {msg.Reason}");
                GlobalTypeNetEvent.Broadcast(new S2C_DownloadReplayResult
                {
                    Success = false,
                    ReplayId = msg.ReplayId,
                    Reason = msg.Reason
                });
                return;
            }

            _downloadingReplayId = msg.ReplayId;
            _expectedTotalBytes = msg.TotalBytes;
            string tmpPath = Path.Combine(CacheFolderPath, $"{msg.ReplayId}.tmp").Replace("\\", "/");

            try
            {
                if (msg.AcceptedOffset == 0 && File.Exists(tmpPath))
                {
                    File.Delete(tmpPath);
                }

                _fileStream = new FileStream(tmpPath, FileMode.Append, FileAccess.Write, FileShare.None);

                if (_fileStream.Length >= _expectedTotalBytes)
                {
                    FinishDownload(msg.ReplayId);
                }
            }
            catch (Exception e)
            {
                NetLogger.LogError("ClientReplayModule", $"无法打开临时文件进行写入: {e.Message}");
                CloseFileStream();
            }
        }

        [NetHandler]
        public void OnS2C_DownloadReplayChunk(S2C_DownloadReplayChunk msg)
        {
            if (msg == null || msg.ChunkData == null) return;
            if (msg.ReplayId != _downloadingReplayId || _fileStream == null) return;

            try
            {
                _fileStream.Write(msg.ChunkData, 0, msg.ChunkData.Length);

                if (_fileStream.Length >= _expectedTotalBytes)
                {
                    FinishDownload(msg.ReplayId);
                }
                else
                {
                    _app.SendMessage(new C2S_DownloadReplayChunkAck { ReplayId = _downloadingReplayId });
                }
            }
            catch (Exception e)
            {
                NetLogger.LogError("ClientReplayModule", $"写入录像块时发生异常: {e.Message}");
                CloseFileStream();
            }
        }

        private void FinishDownload(string replayId)
        {
            CloseFileStream();

            string tmpPath = Path.Combine(CacheFolderPath, $"{replayId}.tmp").Replace("\\", "/");
            string finalPath = Path.Combine(CacheFolderPath, $"{replayId}.replay").Replace("\\", "/");

            if (File.Exists(finalPath)) File.Delete(finalPath);
            File.Move(tmpPath, finalPath);

            _downloadingReplayId = null;

            GlobalTypeNetEvent.Broadcast(new S2C_DownloadReplayResult
            {
                Success = true,
                ReplayId = replayId,
                ReplayFileData = finalPath, // 传递文件路径
                Reason = string.Empty
            });
        }

        private void CloseFileStream()
        {
            if (_fileStream != null)
            {
                _fileStream.Dispose();
                _fileStream = null;
            }
        }

        [NetHandler]
        public void OnS2C_DownloadReplayResult(S2C_DownloadReplayResult msg)
        {
            if (msg == null) return;
            GlobalTypeNetEvent.Broadcast(msg);
        }
    }
}