using System;
using System.Collections.Generic;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;

namespace StellarNet.Lite.Server.Modules
{
    [GlobalModule("ServerLobbyModule", "大厅信息模块")]
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
            if (session == null) return;

            var roomsDict = _app.Rooms;
            var roomList = new List<RoomBriefInfo>();

            foreach (var kvp in roomsDict)
            {
                var room = kvp.Value;
                if (room.State == RoomState.Finished) continue;

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

            var response = new S2C_RoomListResponse { Rooms = roomList.ToArray() };
            _app.SendMessageToSession(session, response);
        }
    }
}