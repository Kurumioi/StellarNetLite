using System;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Modules
{
    // 核心修复 P0-5：使用 ClientModule 明确端侧归属
    [ClientModule("ClientLobbyModule", "客户端大厅模块")]
    public sealed class ClientLobbyModule
    {
        private readonly ClientApp _app;

        public ClientLobbyModule(ClientApp app)
        {
            _app = app;
        }

        [NetHandler]
        public void OnS2C_RoomListResponse(S2C_RoomListResponse msg)
        {
            // 核心修复 P0-2：补充阻断日志，拒绝静默 return
            if (msg == null)
            {
                NetLogger.LogError("ClientLobbyModule", "收到非法同步包: Msg 为空");
                return;
            }

            GlobalTypeNetEvent.Broadcast(msg);
        }
    }
}