using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Modules
{
    [ServerModule("ServerLobbyModule", "大厅信息与全局社交模块")]
    /// <summary>
    /// 服务端大厅模块。
    /// 负责房间列表、在线玩家列表、全局聊天和公告分发。
    /// </summary>
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

            S2C_RoomListResponse response = BuildRoomListResponse(_app);
            _app.SendMessageToSession(session, response);
        }

        [NetHandler]
        public void OnC2S_GlobalChat(Session session, C2S_GlobalChat msg)
        {
            if (session == null)
            {
                NetLogger.LogError("ServerLobbyModule", "处理聊天失败: Session 为空");
                return;
            }

            if (string.IsNullOrWhiteSpace(msg.Content))
            {
                return;
            }

            string safeContent = msg.Content.Length > 100 ? msg.Content.Substring(0, 100) + "..." : msg.Content;
            string displayName = string.IsNullOrEmpty(session.AccountId) ? "Unknown" : session.AccountId;

            var syncMsg = new S2C_GlobalChatSync
            {
                SenderSessionId = session.SessionId,
                SenderDisplayName = displayName,
                Content = safeContent,
                SendUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            foreach (var kvp in _app.Sessions)
            {
                Session targetSession = kvp.Value;
                if (targetSession != null && targetSession.IsOnline)
                {
                    _app.SendMessageToSession(targetSession, syncMsg);
                }
            }
        }

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

        public static S2C_OnlinePlayerListSync BuildOnlinePlayerListResponse(ServerApp app)
        {
            if (app == null)
            {
                return new S2C_OnlinePlayerListSync { Players = Array.Empty<OnlinePlayerInfo>() };
            }

            var playerList = new List<OnlinePlayerInfo>();
            foreach (KeyValuePair<string, Session> kvp in app.Sessions)
            {
                Session session = kvp.Value;
                if (session == null)
                {
                    continue;
                }

                playerList.Add(BuildPlayerInfo(session));
            }

            return new S2C_OnlinePlayerListSync { Players = playerList.ToArray() };
        }

        public static void BroadcastOnlinePlayerList(ServerApp app)
        {
            if (app == null)
            {
                NetLogger.LogError("ServerLobbyModule", "全量广播玩家列表失败: app 为空");
                return;
            }

            S2C_OnlinePlayerListSync response = BuildOnlinePlayerListResponse(app);
            if (response == null)
            {
                NetLogger.LogError("ServerLobbyModule", "全量广播玩家列表失败: response 为空");
                return;
            }

            foreach (KeyValuePair<string, Session> kvp in app.Sessions)
            {
                Session session = kvp.Value;
                if (session != null && session.IsOnline)
                {
                    app.SendMessageToSession(session, response);
                }
            }
        }

        public static void BroadcastPlayerStateChange(ServerApp app, Session targetSession, bool isRemoved)
        {
            if (app == null || targetSession == null)
            {
                NetLogger.LogError("ServerLobbyModule", "增量广播玩家状态失败: 参数为空");
                return;
            }

            var syncMsg = new S2C_GlobalPlayerStateIncrementalSync
            {
                IsRemoved = isRemoved,
                Player = BuildPlayerInfo(targetSession)
            };

            foreach (var kvp in app.Sessions)
            {
                Session session = kvp.Value;
                if (session != null && session.IsOnline)
                {
                    app.SendMessageToSession(session, syncMsg);
                }
            }
        }

        public static void BroadcastAnnouncement(ServerApp app, string title, string content)
        {
            if (app == null || string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            var msg = new S2C_GlobalAnnouncement
            {
                Title = title ?? "系统公告",
                Content = content,
                PublishUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            foreach (var kvp in app.Sessions)
            {
                Session session = kvp.Value;
                if (session != null && session.IsOnline)
                {
                    app.SendMessageToSession(session, msg);
                }
            }
        }

        private static OnlinePlayerInfo BuildPlayerInfo(Session session)
        {
            bool isInRoom = !string.IsNullOrEmpty(session.CurrentRoomId);
            string roomId = isInRoom ? session.CurrentRoomId : string.Empty;
            string accountId = string.IsNullOrEmpty(session.AccountId) ? string.Empty : session.AccountId;
            string displayName = string.IsNullOrEmpty(accountId) ? "Unknown" : accountId;

            return new OnlinePlayerInfo
            {
                SessionId = session.SessionId ?? string.Empty,
                AccountId = accountId,
                DisplayName = displayName,
                IsOnline = session.IsOnline,
                IsInRoom = isInRoom,
                RoomId = roomId
            };
        }
    }
}
