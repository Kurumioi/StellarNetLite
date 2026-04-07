using System.IO;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;

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
            GlobalTypeNetEvent.Register<Local_ConnectionAborted>(OnConnectionAborted);
        }

        private void OnConnectionAborted(Local_ConnectionAborted evt)
        {
            CloseFileStream();
            _downloadingReplayId = string.Empty;
            _expectedTotalBytes = 0;
        }

        public static void RequestDownload(ClientApp app, string replayId)
        {
            if (string.IsNullOrEmpty(replayId)) return;
            EnsureCacheFolderExists();

            string finalPath = Path.Combine(CacheFolderPath, $"{replayId}.replay").Replace("\\", "/");
            string tmpPath = Path.Combine(CacheFolderPath, $"{replayId}.tmp").Replace("\\", "/");

            if (File.Exists(finalPath))
            {
                GlobalTypeNetEvent.Broadcast(new S2C_DownloadReplayResult { Success = true, ReplayId = replayId, ReplayFileData = finalPath });
                return;
            }

            int startOffset = File.Exists(tmpPath) ? (int)new FileInfo(tmpPath).Length : 0;
            app.SendMessage(new C2S_DownloadReplay { ReplayId = replayId, StartOffset = startOffset });
        }

        [NetHandler]
        public void OnS2C_ReplayList(S2C_ReplayList msg) => GlobalTypeNetEvent.Broadcast(msg);

        [NetHandler]
        public void OnS2C_DownloadReplayStart(S2C_DownloadReplayStart msg)
        {
            CloseFileStream();
            if (!msg.Success)
            {
                _downloadingReplayId = string.Empty;
                _expectedTotalBytes = 0;
                GlobalTypeNetEvent.Broadcast(new S2C_DownloadReplayResult { Success = false, ReplayId = msg.ReplayId, Reason = msg.Reason });
                return;
            }

            EnsureCacheFolderExists();
            _downloadingReplayId = msg.ReplayId ?? string.Empty;
            _expectedTotalBytes = msg.TotalBytes;

            string tmpPath = Path.Combine(CacheFolderPath, $"{_downloadingReplayId}.tmp").Replace("\\", "/");
            if (msg.AcceptedOffset == 0 && File.Exists(tmpPath)) File.Delete(tmpPath);

            _fileStream = new FileStream(tmpPath, FileMode.Append, FileAccess.Write, FileShare.None);
            GlobalTypeNetEvent.Broadcast(new Local_ReplayDownloadProgress
                { ReplayId = _downloadingReplayId, DownloadedBytes = msg.AcceptedOffset, TotalBytes = _expectedTotalBytes });

            if (_fileStream.Length >= _expectedTotalBytes) FinishDownload(_downloadingReplayId);
        }

        [NetHandler]
        public void OnS2C_DownloadReplayChunk(S2C_DownloadReplayChunk msg)
        {
            if (msg.ChunkData == null || msg.ReplayId != _downloadingReplayId || _fileStream == null) return;

            _fileStream.Write(msg.ChunkData, 0, msg.ChunkData.Length);
            _fileStream.Flush();

            GlobalTypeNetEvent.Broadcast(new Local_ReplayDownloadProgress
                { ReplayId = _downloadingReplayId, DownloadedBytes = (int)_fileStream.Length, TotalBytes = _expectedTotalBytes });

            if (_fileStream.Length >= _expectedTotalBytes)
            {
                FinishDownload(msg.ReplayId);
                return;
            }

            _app.SendMessage(new C2S_DownloadReplayChunkAck { ReplayId = _downloadingReplayId });
        }

        private void FinishDownload(string replayId)
        {
            CloseFileStream();
            string tmpPath = Path.Combine(CacheFolderPath, $"{replayId}.tmp").Replace("\\", "/");
            string finalPath = Path.Combine(CacheFolderPath, $"{replayId}.replay").Replace("\\", "/");

            if (!File.Exists(tmpPath))
            {
                FailCurrentDownload(replayId, "临时录像文件不存在");
                return;
            }

            if (File.Exists(finalPath)) File.Delete(finalPath);
            File.Move(tmpPath, finalPath);

            _downloadingReplayId = string.Empty;
            _expectedTotalBytes = 0;
            GlobalTypeNetEvent.Broadcast(new S2C_DownloadReplayResult { Success = true, ReplayId = replayId, ReplayFileData = finalPath });
        }

        private void FailCurrentDownload(string replayId, string reason)
        {
            CloseFileStream();
            _downloadingReplayId = string.Empty;
            _expectedTotalBytes = 0;
            GlobalTypeNetEvent.Broadcast(new S2C_DownloadReplayResult { Success = false, ReplayId = replayId ?? string.Empty, Reason = reason });
        }

        private void CloseFileStream()
        {
            _fileStream?.Dispose();
            _fileStream = null;
        }

        private static void EnsureCacheFolderExists()
        {
            if (!Directory.Exists(CacheFolderPath)) Directory.CreateDirectory(CacheFolderPath);
        }

        [NetHandler]
        public void OnS2C_DownloadReplayResult(S2C_DownloadReplayResult msg) => GlobalTypeNetEvent.Broadcast(msg);
    }
}