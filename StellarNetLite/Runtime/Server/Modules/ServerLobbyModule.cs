using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Modules
{
    [ServerModule("ServerLobbyModule", "大厅信息与全局社交模块")]
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

            // 防御性截断，防止恶意超长文本攻击
            string safeContent = msg.Content.Length > 100 ? msg.Content.Substring(0, 100) + "..." : msg.Content;
            string displayName = string.IsNullOrEmpty(session.Uid) ? "Unknown" : session.Uid;

            var syncMsg = new S2C_GlobalChatSync
            {
                SenderSessionId = session.SessionId,
                SenderDisplayName = displayName,
                Content = safeContent,
                SendUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            // 广播给所有在线玩家
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

        /// <summary>
        /// 全量广播在线玩家列表（开销较大，仅建议在必要时调用）
        /// </summary>
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

        /// <summary>
        /// 增量广播单点玩家状态变化（推荐使用此方法降低带宽与 GC）
        /// </summary>
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

        /// <summary>
        /// 广播全局系统公告
        /// </summary>
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
            string uid = string.IsNullOrEmpty(session.Uid) ? string.Empty : session.Uid;
            string displayName = string.IsNullOrEmpty(uid) ? "Unknown" : uid;

            return new OnlinePlayerInfo
            {
                SessionId = session.SessionId ?? string.Empty,
                Uid = uid,
                DisplayName = displayName,
                IsOnline = session.IsOnline,
                IsInRoom = isInRoom,
                RoomId = roomId
            };
        }
    }
}