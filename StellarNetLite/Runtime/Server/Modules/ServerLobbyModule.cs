using System;
using System.Collections.Generic;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;

namespace StellarNet.Lite.Server.Modules
{
    /// <summary>
    /// 服务端大厅模块。
    /// 职责：处理大厅层级的全局请求，如查询房间列表。
    /// </summary>
    public sealed class ServerLobbyModule
    {
        private readonly ServerApp _app;
        private readonly Action<int, Packet> _networkSender;
        private readonly Func<object, byte[]> _serializeFunc;

        public ServerLobbyModule(ServerApp app, Action<int, Packet> networkSender, Func<object, byte[]> serializeFunc)
        {
            _app = app;
            _networkSender = networkSender;
            _serializeFunc = serializeFunc;
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

                // 领域模型反向映射：将服务端内部的 Config 映射为扁平的 DTO 供客户端展示
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