using System;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Client.Core.Events;

namespace StellarNet.Lite.Client.Modules
{
    public sealed class ClientReplayModule
    {
        private readonly ClientApp _app;
        private readonly Action<Packet> _networkSender;
        private readonly Func<object, byte[]> _serializeFunc;

        public ClientReplayModule(ClientApp app, Action<Packet> networkSender, Func<object, byte[]> serializeFunc)
        {
            _app = app;
            _networkSender = networkSender;
            _serializeFunc = serializeFunc;
        }

        [NetHandler]
        public void OnS2C_ReplayList(S2C_ReplayList msg)
        {
            if (msg == null) return;
            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_DownloadReplayResult(S2C_DownloadReplayResult msg)
        {
            if (msg == null) return;

            if (!msg.Success)
            {
                LiteLogger.LogError("ClientReplayModule", $"录像下载失败: {msg.Reason}");
            }
            else if (string.IsNullOrEmpty(msg.ReplayFileData))
            {
                LiteLogger.LogError("ClientReplayModule", "录像下载失败: 服务端返回的录像数据为空");
            }
            else
            {
                LiteLogger.LogInfo("ClientReplayModule", $"录像下载成功，准备派发给表现层解析");
            }

            // 核心重构：不再在 Module 层做 JSON 反序列化，直接抛出协议，由真正需要播放的系统去解析
            GlobalTypeNetEvent.Broadcast(msg);
        }
    }
}