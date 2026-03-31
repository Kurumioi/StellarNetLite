using System;
using System.IO;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;

namespace StellarNet.Lite.Client.Modules
{
    [ClientModule("ClientReplayModule", "客户端回放模块")]
    public sealed class ClientReplayModule
    {
        private readonly ClientApp _app;
        // 当前正在下载的录像上下文。
        private string _downloadingReplayId;
        private int _expectedTotalBytes;
        private FileStream _fileStream;

        public static string CacheFolderPath =>
            Path.Combine(Application.persistentDataPath, "ClientReplays").Replace("\\", "/");

        public ClientReplayModule(ClientApp app)
        {
            _app = app;
            // 连接被硬中止时，下载文件流也必须立即释放。
            GlobalTypeNetEvent.Register<Local_ConnectionAborted>(OnConnectionAborted);
        }

        private void OnConnectionAborted(Local_ConnectionAborted evt)
        {
            if (_fileStream != null)
            {
                NetLogger.LogWarning("ClientReplayModule", "检测到连接硬中止，强制释放正在下载的录像文件流");
                CloseFileStream();
            }

            _downloadingReplayId = string.Empty;
            _expectedTotalBytes = 0;
        }

        public static void RequestDownload(ClientApp app, string replayId)
        {
            if (app == null)
            {
                NetLogger.LogError("ClientReplayModule", $"请求下载失败: app 为空, ReplayId:{replayId}");
                return;
            }

            if (string.IsNullOrEmpty(replayId))
            {
                NetLogger.LogError("ClientReplayModule", "请求下载失败: replayId 为空");
                return;
            }

            EnsureCacheFolderExists();

            // 先查本地缓存，命中则直接进入回放，不再向服务端拉取。
            string finalPath = Path.Combine(CacheFolderPath, $"{replayId}.replay").Replace("\\", "/");
            string tmpPath = Path.Combine(CacheFolderPath, $"{replayId}.tmp").Replace("\\", "/");

            if (File.Exists(finalPath))
            {
                NetLogger.LogInfo("ClientReplayModule", $"命中本地缓存，直接加载录像: {replayId}");
                GlobalTypeNetEvent.Broadcast(new S2C_DownloadReplayResult
                {
                    Success = true,
                    ReplayId = replayId,
                    ReplayFileData = finalPath,
                    Reason = string.Empty
                });
                return;
            }

            int startOffset = 0;
            // 存在临时文件时使用断点续传。
            if (File.Exists(tmpPath))
            {
                startOffset = (int)new FileInfo(tmpPath).Length;
                NetLogger.LogInfo("ClientReplayModule",
                    $"发现未完成下载，发起断点续传。ReplayId:{replayId}, StartOffset:{startOffset}");
            }

            app.SendMessage(new C2S_DownloadReplay
            {
                ReplayId = replayId,
                StartOffset = startOffset
            });
        }

        [NetHandler]
        public void OnS2C_ReplayList(S2C_ReplayList msg)
        {
            if (msg == null)
            {
                NetLogger.LogError("ClientReplayModule", "处理录像列表失败: msg 为空");
                return;
            }

            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_DownloadReplayStart(S2C_DownloadReplayStart msg)
        {
            if (msg == null)
            {
                NetLogger.LogError("ClientReplayModule", "处理下载开始失败: msg 为空");
                return;
            }

            CloseFileStream();

            // Start 包决定本次下载是否开始、从哪个 offset 继续写。
            if (!msg.Success)
            {
                _downloadingReplayId = string.Empty;
                _expectedTotalBytes = 0;
                NetLogger.LogError("ClientReplayModule", $"录像下载请求失败: ReplayId:{msg.ReplayId}, Reason:{msg.Reason}");
                GlobalTypeNetEvent.Broadcast(new S2C_DownloadReplayResult
                {
                    Success = false,
                    ReplayId = msg.ReplayId,
                    Reason = msg.Reason
                });
                return;
            }

            EnsureCacheFolderExists();

            _downloadingReplayId = msg.ReplayId ?? string.Empty;
            _expectedTotalBytes = msg.TotalBytes;

            if (string.IsNullOrEmpty(_downloadingReplayId))
            {
                NetLogger.LogError("ClientReplayModule", "处理下载开始失败: ReplayId 为空");
                CloseFileStream();
                return;
            }

            if (_expectedTotalBytes < 0)
            {
                NetLogger.LogError("ClientReplayModule",
                    $"处理下载开始失败: TotalBytes 非法, ReplayId:{_downloadingReplayId}, TotalBytes:{_expectedTotalBytes}");
                _downloadingReplayId = string.Empty;
                _expectedTotalBytes = 0;
                return;
            }

            // 服务端 acceptedOffset 为 0 时，说明本次从头开始写 tmp。
            string tmpPath = Path.Combine(CacheFolderPath, $"{_downloadingReplayId}.tmp").Replace("\\", "/");
            if (msg.AcceptedOffset == 0 && File.Exists(tmpPath))
            {
                File.Delete(tmpPath);
            }

            _fileStream = new FileStream(tmpPath, FileMode.Append, FileAccess.Write, FileShare.None);

            GlobalTypeNetEvent.Broadcast(new Local_ReplayDownloadProgress
            {
                ReplayId = _downloadingReplayId,
                DownloadedBytes = msg.AcceptedOffset,
                TotalBytes = _expectedTotalBytes
            });

            if (_fileStream.Length >= _expectedTotalBytes)
            {
                FinishDownload(_downloadingReplayId);
            }
        }

        [NetHandler]
        public void OnS2C_DownloadReplayChunk(S2C_DownloadReplayChunk msg)
        {
            if (msg == null)
            {
                NetLogger.LogError("ClientReplayModule", "处理录像分块失败: msg 为空");
                return;
            }

            if (msg.ChunkData == null)
            {
                NetLogger.LogError("ClientReplayModule", $"处理录像分块失败: ChunkData 为空, ReplayId:{msg.ReplayId}");
                FailCurrentDownload(msg.ReplayId, "录像分块数据为空");
                return;
            }

            if (string.IsNullOrEmpty(_downloadingReplayId))
            {
                NetLogger.LogWarning("ClientReplayModule", $"处理录像分块跳过: 当前无活跃下载, ReplayId:{msg.ReplayId}");
                return;
            }

            if (msg.ReplayId != _downloadingReplayId)
            {
                NetLogger.LogWarning(
                    "ClientReplayModule",
                    $"处理录像分块跳过: ReplayId 不匹配, Incoming:{msg.ReplayId}, Current:{_downloadingReplayId}");
                return;
            }

            if (_fileStream == null)
            {
                NetLogger.LogError("ClientReplayModule", $"处理录像分块失败: _fileStream 为空, ReplayId:{msg.ReplayId}");
                FailCurrentDownload(msg.ReplayId, "客户端文件流异常丢失");
                return;
            }

            // 每收到一个 chunk 就立即落盘，并回 Ack 请求下一个分块。
            _fileStream.Write(msg.ChunkData, 0, msg.ChunkData.Length);
            _fileStream.Flush();

            GlobalTypeNetEvent.Broadcast(new Local_ReplayDownloadProgress
            {
                ReplayId = _downloadingReplayId,
                DownloadedBytes = (int)_fileStream.Length,
                TotalBytes = _expectedTotalBytes
            });

            if (_fileStream.Length >= _expectedTotalBytes)
            {
                FinishDownload(msg.ReplayId);
                return;
            }

            _app?.SendMessage(new C2S_DownloadReplayChunkAck
            {
                ReplayId = _downloadingReplayId
            });
        }

        private void FinishDownload(string replayId)
        {
            if (string.IsNullOrEmpty(replayId))
            {
                NetLogger.LogError("ClientReplayModule", "完成下载失败: replayId 为空");
                return;
            }

            CloseFileStream();

            // 下载完成后把 tmp 原子切成 replay 文件。
            string tmpPath = Path.Combine(CacheFolderPath, $"{replayId}.tmp").Replace("\\", "/");
            string finalPath = Path.Combine(CacheFolderPath, $"{replayId}.replay").Replace("\\", "/");

            if (!File.Exists(tmpPath))
            {
                NetLogger.LogError("ClientReplayModule", $"完成下载失败: tmp 文件不存在, ReplayId:{replayId}, TmpPath:{tmpPath}");
                FailCurrentDownload(replayId, "临时录像文件不存在");
                return;
            }

            if (File.Exists(finalPath))
            {
                File.Delete(finalPath);
            }

            File.Move(tmpPath, finalPath);

            _downloadingReplayId = string.Empty;
            _expectedTotalBytes = 0;

            GlobalTypeNetEvent.Broadcast(new S2C_DownloadReplayResult
            {
                Success = true,
                ReplayId = replayId,
                ReplayFileData = finalPath,
                Reason = string.Empty
            });
        }

        private void FailCurrentDownload(string replayId, string reason)
        {
            CloseFileStream();

            _downloadingReplayId = string.Empty;
            _expectedTotalBytes = 0;

            GlobalTypeNetEvent.Broadcast(new S2C_DownloadReplayResult
            {
                Success = false,
                ReplayId = replayId ?? string.Empty,
                ReplayFileData = string.Empty,
                Reason = reason ?? "未知错误"
            });
        }

        private void CloseFileStream()
        {
            if (_fileStream == null)
            {
                return;
            }

            _fileStream.Dispose();
            _fileStream = null;
        }

        private static void EnsureCacheFolderExists()
        {
            if (Directory.Exists(CacheFolderPath))
            {
                return;
            }

            Directory.CreateDirectory(CacheFolderPath);
        }

        [NetHandler]
        public void OnS2C_DownloadReplayResult(S2C_DownloadReplayResult msg)
        {
            if (msg == null)
            {
                NetLogger.LogError("ClientReplayModule", "处理下载结果失败: msg 为空");
                return;
            }

            GlobalTypeNetEvent.Broadcast(msg);
        }
    }
}
