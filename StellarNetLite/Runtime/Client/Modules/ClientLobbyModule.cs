using System;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Modules
{
    [ClientModule("ClientLobbyModule", "客户端大厅与全局社交模块")]
    /// <summary>
    /// 客户端大厅模块。
    /// 负责把大厅协议转发给全局事件总线。
    /// </summary>
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

            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_OnlinePlayerListSync(S2C_OnlinePlayerListSync msg)
        {
            if (msg == null || msg.Players == null)
            {
                NetLogger.LogError("ClientLobbyModule", "收到全量玩家列表失败: msg 或 Players 为空");
                return;
            }

            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_GlobalPlayerStateIncrementalSync(S2C_GlobalPlayerStateIncrementalSync msg)
        {
            if (msg == null || msg.Player == null)
            {
                NetLogger.LogError("ClientLobbyModule", "收到增量玩家状态失败: msg 或 Player 为空");
                return;
            }

            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_GlobalAnnouncement(S2C_GlobalAnnouncement msg)
        {
            if (msg == null)
            {
                NetLogger.LogError("ClientLobbyModule", "收到全局公告失败: msg 为空");
                return;
            }

            // 直接转换为本地系统提示抛出，或者抛出原始协议供专门的公告 UI 订阅
            GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = $"【公告】{msg.Title}\n{msg.Content}" });
            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_GlobalChatSync(S2C_GlobalChatSync msg)
        {
            if (msg == null)
            {
                NetLogger.LogError("ClientLobbyModule", "收到全局聊天失败: msg 为空");
                return;
            }

            GlobalTypeNetEvent.Broadcast(msg);
        }
    }
}
