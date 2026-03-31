using System;
using System.Collections.Generic;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Server.Modules
{
    [ServerModule("ServerRoomModule", "房间生命周期模块")]
    public sealed class ServerRoomModule
    {
        private readonly ServerApp _app;

        public ServerRoomModule(ServerApp app)
        {
            _app = app;
        }

        [NetHandler]
        public void OnC2S_CreateRoom(Session session, C2S_CreateRoom msg)
        {
            if (_app == null)
            {
                NetLogger.LogError("ServerRoomModule", "创建房间失败: _app 为空");
                return;
            }

            if (session == null || msg == null)
            {
                NetLogger.LogError("ServerRoomModule",
                    $"创建房间失败: session 或 msg 为空, Session:{session?.SessionId ?? "null"}");
                return;
            }

            if (!string.IsNullOrEmpty(session.CurrentRoomId))
            {
                _app.SendMessageToSession(session, new S2C_CreateRoomResult
                {
                    Success = false,
                    Reason = "已在房间中"
                });
                return;
            }

            int[] uniqueComponentIds = DeduplicateComponentIds(msg.ComponentIds);
            if (uniqueComponentIds.Length == 0)
            {
                NetLogger.LogError("ServerRoomModule", $"创建房间失败: 组件清单为空, SessionId:{session.SessionId}");
                _app.SendMessageToSession(session, new S2C_CreateRoomResult
                {
                    Success = false,
                    Reason = "房间组件清单不能为空"
                });
                return;
            }

            string roomId = Guid.NewGuid().ToString("N").Substring(0, 8);
            Room room = _app.CreateRoom(roomId);
            if (room == null)
            {
                _app.SendMessageToSession(session, new S2C_CreateRoomResult
                {
                    Success = false,
                    Reason = "服务器内部错误"
                });
                return;
            }

            room.Config.RoomName = string.IsNullOrWhiteSpace(msg.RoomName) ? $"房间_{roomId}" : msg.RoomName.Trim();
            room.Config.MaxMembers = msg.MaxMembers <= 0 ? 4 : msg.MaxMembers;
            room.Config.Password = msg.Password ?? string.Empty;

            bool buildSuccess = ServerRoomFactory.BuildComponents(room, uniqueComponentIds);
            if (!buildSuccess)
            {
                _app.DestroyRoom(roomId);
                NetLogger.LogError("ServerRoomModule", $"房间组件装配失败，已销毁残缺房间, RoomId:{roomId}", roomId, session.SessionId);
                _app.SendMessageToSession(session, new S2C_CreateRoomResult
                {
                    Success = false,
                    Reason = "房间组件装配失败，存在非法组件"
                });
                return;
            }

            room.SetComponentIds(uniqueComponentIds);
            session.AuthorizeRoom(roomId);

            _app.SendMessageToSession(session, new S2C_CreateRoomResult
            {
                Success = true,
                RoomId = roomId,
                ComponentIds = uniqueComponentIds,
                Reason = string.Empty
            });

            BroadcastRoomListToLobby();
        }

        [NetHandler]
        public void OnC2S_JoinRoom(Session session, C2S_JoinRoom msg)
        {
            if (_app == null)
            {
                NetLogger.LogError("ServerRoomModule", "加入房间失败: _app 为空");
                return;
            }

            if (session == null || msg == null)
            {
                NetLogger.LogError("ServerRoomModule",
                    $"加入房间失败: session 或 msg 为空, Session:{session?.SessionId ?? "null"}");
                return;
            }

            if (string.IsNullOrEmpty(msg.RoomId))
            {
                NetLogger.LogError("ServerRoomModule", "加入房间失败: RoomId 为空", "-", session.SessionId);
                _app.SendMessageToSession(session, new S2C_JoinRoomResult
                {
                    Success = false,
                    Reason = "房间 ID 不能为空"
                });
                return;
            }

            if (!string.IsNullOrEmpty(session.CurrentRoomId))
            {
                _app.SendMessageToSession(session, new S2C_JoinRoomResult
                {
                    Success = false,
                    Reason = "已在房间中"
                });
                return;
            }

            Room room = _app.GetRoom(msg.RoomId);
            if (room == null)
            {
                _app.SendMessageToSession(session, new S2C_JoinRoomResult
                {
                    Success = false,
                    Reason = "房间不存在"
                });
                return;
            }

            if (room.MemberCount >= room.Config.MaxMembers)
            {
                NetLogger.LogWarning("ServerRoomModule", "加入拦截: 房间人数已满", msg.RoomId, session.SessionId);
                _app.SendMessageToSession(session, new S2C_JoinRoomResult
                {
                    Success = false,
                    Reason = "房间人数已满"
                });
                return;
            }

            if (room.Config.IsPrivate && room.Config.Password != msg.Password)
            {
                NetLogger.LogWarning("ServerRoomModule", "加入拦截: 房间密码错误", msg.RoomId, session.SessionId);
                _app.SendMessageToSession(session, new S2C_JoinRoomResult
                {
                    Success = false,
                    Reason = "房间密码错误"
                });
                return;
            }

            session.AuthorizeRoom(room.RoomId);

            _app.SendMessageToSession(session, new S2C_JoinRoomResult
            {
                Success = true,
                RoomId = room.RoomId,
                ComponentIds = room.ComponentIds,
                Reason = string.Empty
            });
        }

        [NetHandler]
        public void OnC2S_RoomSetupReady(Session session, C2S_RoomSetupReady msg)
        {
            if (_app == null)
            {
                NetLogger.LogError("ServerRoomModule", "房间装配握手失败: _app 为空");
                return;
            }

            if (session == null || msg == null)
            {
                NetLogger.LogError("ServerRoomModule",
                    $"房间装配握手失败: session 或 msg 为空, Session:{session?.SessionId ?? "null"}");
                return;
            }

            if (string.IsNullOrEmpty(msg.RoomId))
            {
                NetLogger.LogError("ServerRoomModule", "握手阻断: msg.RoomId 为空", "-", session.SessionId);
                return;
            }

            if (string.IsNullOrEmpty(session.AuthorizedRoomId) || session.AuthorizedRoomId != msg.RoomId)
            {
                NetLogger.LogError(
                    "ServerRoomModule",
                    $"握手阻断: 授权房间不匹配, Target:{msg.RoomId}, Authorized:{session.AuthorizedRoomId}",
                    "-",
                    session.SessionId);
                return;
            }

            if (!string.IsNullOrEmpty(session.CurrentRoomId))
            {
                NetLogger.LogError(
                    "ServerRoomModule",
                    $"握手阻断: 玩家已在房间中, CurrentRoom:{session.CurrentRoomId}, Target:{msg.RoomId}",
                    session.CurrentRoomId,
                    session.SessionId);
                return;
            }

            Room room = _app.GetRoom(msg.RoomId);
            if (room == null)
            {
                NetLogger.LogError("ServerRoomModule", "握手阻断: 目标房间不存在", msg.RoomId, session.SessionId);
                return;
            }

            room.AddMember(session);
            session.ClearAuthorizedRoom();

            NetLogger.LogInfo("ServerRoomModule", "客户端首次装配就绪，正式加入房间", msg.RoomId, session.SessionId);

            BroadcastRoomListToLobby();
            ServerLobbyModule.BroadcastOnlinePlayerList(_app);
        }

        [NetHandler]
        public void OnC2S_LeaveRoom(Session session, C2S_LeaveRoom msg)
        {
            if (_app == null)
            {
                NetLogger.LogError("ServerRoomModule", "离开房间失败: _app 为空");
                return;
            }

            if (session == null)
            {
                NetLogger.LogError("ServerRoomModule", "离开房间失败: session 为空");
                return;
            }

            string roomId = session.CurrentRoomId;
            if (string.IsNullOrEmpty(roomId))
            {
                _app.SendMessageToSession(session, new S2C_LeaveRoomResult { Success = true });
                return;
            }

            Room room = _app.GetRoom(roomId);
            if (room != null)
            {
                room.RemoveMember(session);
                if (room.MemberCount == 0)
                {
                    _app.DestroyRoom(roomId);
                    NetLogger.LogInfo("ServerRoomModule", "房间已空，执行自动销毁", roomId, session.SessionId);
                }
            }

            _app.SendMessageToSession(session, new S2C_LeaveRoomResult { Success = true });

            BroadcastRoomListToLobby();
            ServerLobbyModule.BroadcastOnlinePlayerList(_app);
        }

        private int[] DeduplicateComponentIds(int[] rawIds)
        {
            if (rawIds == null || rawIds.Length == 0)
            {
                return Array.Empty<int>();
            }

            var set = new HashSet<int>();
            var list = new List<int>(rawIds.Length);

            for (int i = 0; i < rawIds.Length; i++)
            {
                if (set.Add(rawIds[i]))
                {
                    list.Add(rawIds[i]);
                }
            }

            return list.ToArray();
        }

        private void BroadcastRoomListToLobby()
        {
            if (_app == null)
            {
                return;
            }

            // 直接调用 ServerLobbyModule 的静态方法，复用组装逻辑
            S2C_RoomListResponse response = ServerLobbyModule.BuildRoomListResponse(_app);

            foreach (KeyValuePair<string, Session> kvp in _app.Sessions)
            {
                Session session = kvp.Value;
                if (session == null)
                {
                    continue;
                }

                if (session.IsOnline && string.IsNullOrEmpty(session.CurrentRoomId))
                {
                    _app.SendMessageToSession(session, response);
                }
            }
        }
    }
}