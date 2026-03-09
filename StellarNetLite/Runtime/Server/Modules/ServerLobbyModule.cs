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

            // 核心修复：移除反射，直接使用 ServerApp 提供的合法只读接口
            var roomsDict = _app.Rooms;
            var roomList = new List<RoomBriefInfo>();

            foreach (var kvp in roomsDict)
            {
                var room = kvp.Value;

                // 过滤掉已经结束的僵尸房间
                if (room.State == RoomState.Finished) continue;

                string displayName = string.IsNullOrEmpty(room.RoomName)
                    ? $"房间_{room.RoomId.Substring(0, Math.Min(6, room.RoomId.Length))}"
                    : room.RoomName;

                roomList.Add(new RoomBriefInfo
                {
                    RoomId = room.RoomId,
                    RoomName = displayName,
                    MemberCount = room.MemberCount,
                    State = (int)room.State
                });
            }

            var response = new S2C_RoomListResponse { Rooms = roomList.ToArray() };

            // 核心修复：改用强类型统一发送器，消除硬编码发包
            _app.SendMessageToSession(session, response);
        }
    }
}