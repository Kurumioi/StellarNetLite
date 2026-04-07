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
            if (!string.IsNullOrEmpty(session.CurrentRoomId))
            {
                _app.SendMessageToSession(session, new S2C_CreateRoomResult { Success = false, Reason = "已在房间中" });
                return;
            }

            int[] uniqueComponentIds = DeduplicateComponentIds(msg.ComponentIds);
            if (uniqueComponentIds.Length == 0)
            {
                _app.SendMessageToSession(session, new S2C_CreateRoomResult { Success = false, Reason = "房间组件清单不能为空" });
                return;
            }

            string roomId = Guid.NewGuid().ToString("N").Substring(0, 8);
            Room room = _app.CreateRoom(roomId);
            if (room == null)
            {
                _app.SendMessageToSession(session, new S2C_CreateRoomResult { Success = false, Reason = "服务器内部错误" });
                return;
            }

            room.Config.RoomName = string.IsNullOrWhiteSpace(msg.RoomName) ? $"房间_{roomId}" : msg.RoomName.Trim();
            room.Config.MaxMembers = msg.MaxMembers <= 0 ? 4 : msg.MaxMembers;
            room.Config.Password = msg.Password ?? string.Empty;

            if (!ServerRoomFactory.BuildComponents(room, uniqueComponentIds))
            {
                _app.DestroyRoom(roomId);
                _app.SendMessageToSession(session, new S2C_CreateRoomResult { Success = false, Reason = "房间组件装配失败" });
                return;
            }

            room.SetComponentIds(uniqueComponentIds);
            session.AuthorizeRoom(roomId);
            
            _app.SendMessageToSession(session, new S2C_CreateRoomResult { Success = true, RoomId = roomId, ComponentIds = uniqueComponentIds });
            BroadcastRoomListToLobby();
        }

        [NetHandler]
        public void OnC2S_JoinRoom(Session session, C2S_JoinRoom msg)
        {
            if (string.IsNullOrEmpty(msg.RoomId)) return;

            if (!string.IsNullOrEmpty(session.CurrentRoomId))
            {
                _app.SendMessageToSession(session, new S2C_JoinRoomResult { Success = false, Reason = "已在房间中" });
                return;
            }

            Room room = _app.GetRoom(msg.RoomId);
            if (room == null)
            {
                _app.SendMessageToSession(session, new S2C_JoinRoomResult { Success = false, Reason = "房间不存在" });
                return;
            }

            if (room.MemberCount >= room.Config.MaxMembers)
            {
                _app.SendMessageToSession(session, new S2C_JoinRoomResult { Success = false, Reason = "房间人数已满" });
                return;
            }

            if (room.Config.IsPrivate && room.Config.Password != msg.Password)
            {
                _app.SendMessageToSession(session, new S2C_JoinRoomResult { Success = false, Reason = "房间密码错误" });
                return;
            }

            session.AuthorizeRoom(room.RoomId);
            _app.SendMessageToSession(session, new S2C_JoinRoomResult { Success = true, RoomId = room.RoomId, ComponentIds = room.ComponentIds });
        }

        [NetHandler]
        public void OnC2S_RoomSetupReady(Session session, C2S_RoomSetupReady msg)
        {
            if (string.IsNullOrEmpty(msg.RoomId) || session.AuthorizedRoomId != msg.RoomId || !string.IsNullOrEmpty(session.CurrentRoomId)) return;

            Room room = _app.GetRoom(msg.RoomId);
            if (room == null) return;

            room.AddMember(session);
            session.ClearAuthorizedRoom();
            
            BroadcastRoomListToLobby();
            ServerLobbyModule.BroadcastOnlinePlayerList(_app);
        }

        [NetHandler]
        public void OnC2S_LeaveRoom(Session session, C2S_LeaveRoom msg)
        {
            string roomId = session.CurrentRoomId;
            if (!string.IsNullOrEmpty(roomId))
            {
                Room room = _app.GetRoom(roomId);
                if (room != null)
                {
                    room.RemoveMember(session);
                    if (room.MemberCount == 0) _app.DestroyRoom(roomId);
                }
            }

            _app.SendMessageToSession(session, new S2C_LeaveRoomResult { Success = true });
            BroadcastRoomListToLobby();
            ServerLobbyModule.BroadcastOnlinePlayerList(_app);
        }

        private int[] DeduplicateComponentIds(int[] rawIds)
        {
            if (rawIds == null || rawIds.Length == 0) return Array.Empty<int>();
            var set = new HashSet<int>();
            var list = new List<int>(rawIds.Length);
            for (int i = 0; i < rawIds.Length; i++)
            {
                if (set.Add(rawIds[i])) list.Add(rawIds[i]);
            }
            return list.ToArray();
        }

        private void BroadcastRoomListToLobby()
        {
            S2C_RoomListResponse response = ServerLobbyModule.BuildRoomListResponse(_app);
            foreach (var kvp in _app.Sessions)
            {
                Session session = kvp.Value;
                if (session != null && session.IsOnline && string.IsNullOrEmpty(session.CurrentRoomId))
                {
                    _app.SendMessageToSession(session, response);
                }
            }
        }
    }
}
