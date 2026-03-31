using System;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Modules
{
    // 客户端大厅模块，负责转发大厅级同步消息。
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
            if (msg == null)
            {
                NetLogger.LogError("ClientLobbyModule", "收到非法同步包: Msg 为空");
                return;
            }

            // 大厅面板通过全局事件自己决定如何渲染列表。
            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_OnlinePlayerListSync(S2C_OnlinePlayerListSync msg)
        {
            if (msg == null)
            {
                NetLogger.LogError("ClientLobbyModule", "收到在线玩家列表失败: msg 为空");
                return;
            }

            if (msg.Players == null)
            {
                NetLogger.LogError("ClientLobbyModule", "收到在线玩家列表失败: Players 为空");
                return;
            }

            // 在线玩家列表也走事件总线，避免模块直接依赖具体 UI。
            GlobalTypeNetEvent.Broadcast(msg);
        }
    }
}
