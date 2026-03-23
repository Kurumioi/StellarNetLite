using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Modules
{
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

            // 复用静态构建逻辑，保证主动拉取与被动推送的数据结构绝对一致
            S2C_RoomListResponse response = BuildRoomListResponse(_app);
            _app.SendMessageToSession(session, response);
        }

        /// <summary>
        /// 提取公共聚合逻辑，供其他模块（如 ServerRoomModule）复用。
        /// 将大厅列表的组装职责收敛于此，避免后续扩展字段时产生多处散落的修改点。
        /// </summary>
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
    }
}
