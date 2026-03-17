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
    [GlobalModule("ClientReplayModule", "客户端回放模块")]
    public sealed class ClientReplayModule
    {
        private readonly ClientApp _app;

        // 客户端流式接收状态
        private string _downloadingReplayId;
        private int _expectedTotalBytes;
        private FileStream _fileStream;

        // 录像缓存目录
        public static string CacheFolderPath => Path.Combine(Application.persistentDataPath, "ClientReplays").Replace("\\", "/");

        public ClientReplayModule(ClientApp app)
        {
            _app = app;
        }

        /// <summary>
        /// 供 UI 调用的静态入口，封装了本地缓存检查与断点续传逻辑
        /// </summary>
        public static void RequestDownload(ClientApp app, string replayId)
        {
            if (!Directory.Exists(CacheFolderPath))
            {
                Directory.CreateDirectory(CacheFolderPath);
            }

            string finalPath = Path.Combine(CacheFolderPath, $"{replayId}.json").Replace("\\", "/");
            string tmpPath = Path.Combine(CacheFolderPath, $"{replayId}.tmp").Replace("\\", "/");

            // 1. 检查是否已经下载完成（本地缓存秒开）
            if (File.Exists(finalPath))
            {
                NetLogger.LogInfo("ClientReplayModule", $"命中本地缓存，直接读取录像: {replayId}");
                try
                {
                    string json = File.ReadAllText(finalPath);
                    GlobalTypeNetEvent.Broadcast(new S2C_DownloadReplayResult
                    {
                        Success = true,
                        ReplayId = replayId,
                        ReplayFileData = json,
                        Reason = string.Empty
                    });
                    return;
                }
                catch (Exception e)
                {
                    NetLogger.LogError("ClientReplayModule", $"读取本地缓存失败，将重新下载: {e.Message}");
                    File.Delete(finalPath);
                }
            }

            // 2. 检查是否有未完成的临时文件（断点续传）
            int startOffset = 0;
            if (File.Exists(tmpPath))
            {
                startOffset = (int)new FileInfo(tmpPath).Length;
                NetLogger.LogInfo("ClientReplayModule", $"发现未完成的下载，发起断点续传请求，起始偏移: {startOffset} bytes");
            }

            // 3. 向服务端发起请求
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
            CloseFileStream(); // 确保清理旧流

            if (!msg.Success)
            {
                NetLogger.LogError("ClientReplayModule", $"录像下载请求失败: {msg.Reason}");
                // 抛出旧的 Result 事件，让 UI 统一处理失败逻辑
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
                // 核心防御 1：如果服务端拒绝了我们的偏移量（比如文件已在服务端被覆盖更新），必须清空本地脏数据！
                if (msg.AcceptedOffset == 0 && File.Exists(tmpPath))
                {
                    File.Delete(tmpPath);
                    NetLogger.LogWarning("ClientReplayModule", "服务端重置了偏移量，已清空本地临时脏数据，重新下载");
                }

                // 以追加模式 (Append) 打开文件，实现断点续传写入
                _fileStream = new FileStream(tmpPath, FileMode.Append, FileAccess.Write, FileShare.None);
                NetLogger.LogInfo("ClientReplayModule", $"开始接收录像流: {msg.ReplayId}, 总大小: {msg.TotalBytes} bytes, 当前本地大小: {_fileStream.Length} bytes");

                // 核心防御 2：如果刚好下载满但没来得及重命名就闪退了，此时长度已满，直接触发完成！
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
            if (msg == null || msg.ReplayId != _downloadingReplayId || msg.ChunkData == null || _fileStream == null) return;

            try
            {
                // 写入当前块
                _fileStream.Write(msg.ChunkData, 0, msg.ChunkData.Length);

                // 检查是否接收完毕
                if (_fileStream.Length >= _expectedTotalBytes)
                {
                    FinishDownload(msg.ReplayId);
                }
                else
                {
                    // 未接收完，发送 ACK 请求下一块
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
            NetLogger.LogInfo("ClientReplayModule", $"录像 {replayId} 接收完毕，开始装配缓存并派发给 UI");

            CloseFileStream();

            string tmpPath = Path.Combine(CacheFolderPath, $"{replayId}.tmp").Replace("\\", "/");
            string finalPath = Path.Combine(CacheFolderPath, $"{replayId}.json").Replace("\\", "/");

            // 重命名临时文件为正式文件
            if (File.Exists(finalPath)) File.Delete(finalPath);
            File.Move(tmpPath, finalPath);

            // 读取并派发
            string json = File.ReadAllText(finalPath);
            _downloadingReplayId = null;

            // 组装完毕后，在本地抛出旧的 Result 事件，无缝对接表现层 UI
            GlobalTypeNetEvent.Broadcast(new S2C_DownloadReplayResult
            {
                Success = true,
                ReplayId = replayId,
                ReplayFileData = json,
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

        // 兼容保留旧的接口，防止意外调用
        [NetHandler]
        public void OnS2C_DownloadReplayResult(S2C_DownloadReplayResult msg)
        {
            if (msg == null) return;
            GlobalTypeNetEvent.Broadcast(msg);
        }
    }
}