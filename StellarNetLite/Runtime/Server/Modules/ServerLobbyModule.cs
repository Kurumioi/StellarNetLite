using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Modules
{
    // 服务端大厅模块，负责聚合大厅列表类数据。
    [ServerModule("ServerLobbyModule", "大厅信息模块")]
    public sealed class ServerLobbyModule
    {
        private readonly ServerApp _app;

        public ServerLobbyModule(ServerApp app)
        {
            _app = app;
        }

        [NetHandler]
        public void OnC2S_GetRoomList(Session session, C2S_GetRoomList msg)
        {
            if (session == null)
            {
                NetLogger.LogError("ServerLobbyModule", "收到非法请求: Session 为空");
                return;
            }

            // 主动拉取与后续被动推送共用同一份组装逻辑。
            S2C_RoomListResponse response = BuildRoomListResponse(_app);
            _app.SendMessageToSession(session, response);
        }

        // 构建大厅房间列表。
        public static S2C_RoomListResponse BuildRoomListResponse(ServerApp app)
        {
            if (app == null)
            {
                return new S2C_RoomListResponse { Rooms = Array.Empty<RoomBriefInfo>() };
            }

            var roomList = new List<RoomBriefInfo>();
            foreach (KeyValuePair<string, Room> kvp in app.Rooms)
            {
                Room room = kvp.Value;
                if (room == null || room.State == RoomState.Finished)
                {
                    // 已结束房间不再出现在大厅列表。
                    continue;
                }

                string displayName = string.IsNullOrEmpty(room.Config.RoomName)
                    ? $"房间_{room.RoomId.Substring(0, Math.Min(6, room.RoomId.Length))}"
                    : room.Config.RoomName;

                roomList.Add(new RoomBriefInfo
                {
                    RoomId = room.RoomId,
                    RoomName = displayName,
                    MemberCount = room.MemberCount,
                    MaxMembers = room.Config.MaxMembers,
                    IsPrivate = room.Config.IsPrivate,
                    State = (int)room.State
                });
            }

            return new S2C_RoomListResponse { Rooms = roomList.ToArray() };
        }

        // 构建在线玩家列表。
        public static S2C_OnlinePlayerListSync BuildOnlinePlayerListResponse(ServerApp app)
        {
            if (app == null)
            {
                return new S2C_OnlinePlayerListSync
                {
                    Players = Array.Empty<OnlinePlayerInfo>()
                };
            }

            var playerList = new List<OnlinePlayerInfo>();

            foreach (KeyValuePair<string, Session> kvp in app.Sessions)
            {
                Session session = kvp.Value;
                if (session == null)
                {
                    continue;
                }

                if (!session.IsOnline)
                {
                    continue;
                }

                bool isInRoom = !string.IsNullOrEmpty(session.CurrentRoomId);
                string roomId = isInRoom ? session.CurrentRoomId : string.Empty;
                string uid = string.IsNullOrEmpty(session.Uid) ? string.Empty : session.Uid;
                // 默认直接用 uid 作为展示名。
                string displayName = string.IsNullOrEmpty(uid) ? "Unknown" : uid;

                playerList.Add(new OnlinePlayerInfo
                {
                    SessionId = session.SessionId ?? string.Empty,
                    Uid = uid,
                    DisplayName = displayName,
                    IsInRoom = isInRoom,
                    RoomId = roomId
                });
            }

            return new S2C_OnlinePlayerListSync
            {
                Players = playerList.ToArray()
            };
        }

        // 向所有在线会话广播在线玩家列表。
        public static void BroadcastOnlinePlayerList(ServerApp app)
        {
            if (app == null)
            {
                NetLogger.LogError("ServerLobbyModule", "广播在线玩家列表失败: app 为空");
                return;
            }

            S2C_OnlinePlayerListSync response = BuildOnlinePlayerListResponse(app);
            if (response == null)
            {
                NetLogger.LogError("ServerLobbyModule", "广播在线玩家列表失败: response 为空");
                return;
            }

            foreach (KeyValuePair<string, Session> kvp in app.Sessions)
            {
                Session session = kvp.Value;
                if (session == null)
                {
                    continue;
                }

                if (!session.IsOnline)
                {
                    continue;
                }

                // 仅向在线连接推送大厅同步。
                app.SendMessageToSession(session, response);
            }
        }
    }
}
