using System;
using System.Collections.Generic;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Server.Modules
{
    /// <summary>
    /// 房间生命周期模块。
    /// </summary>
    [ServerModule("ServerRoomModule", "房间生命周期模块")]
    public sealed class ServerRoomModule
    {
        // 当前服务端应用实例。
        private readonly ServerApp _app;

        /// <summary>
        /// 创建房间生命周期模块。
        /// </summary>
        public ServerRoomModule(ServerApp app)
        {
            _app = app;
        }

        /// <summary>
        /// 处理建房请求。
        /// </summary>
        [NetHandler]
        public void OnC2S_CreateRoom(Session session, C2S_CreateRoom msg)
        {
            int componentCount = msg.RoomConfig != null && msg.RoomConfig.ComponentIds != null
                ? msg.RoomConfig.ComponentIds.Length
                : 0;
            NetLogger.LogInfo(
                "ServerRoomModule",
                $"建房请求: ComponentCount:{componentCount}",
                sessionId: session.SessionId);

            if (!string.IsNullOrEmpty(session.CurrentRoomId))
            {
                _app.SendMessageToSession(session, new S2C_CreateRoomResult { Success = false, Reason = "已在房间中" });
                NetLogger.LogWarning("ServerRoomModule", "建房拒绝: 当前已在房间中", session.CurrentRoomId, session.SessionId);
                return;
            }

            if (msg.RoomConfig == null)
            {
                _app.SendMessageToSession(session, new S2C_CreateRoomResult { Success = false, Reason = "缺少建房配置" });
                NetLogger.LogWarning("ServerRoomModule", "建房拒绝: 缺少房间配置", sessionId: session.SessionId);
                return;
            }

            int[] uniqueComponentIds = DeduplicateComponentIds(msg.RoomConfig.ComponentIds);
            if (uniqueComponentIds.Length == 0)
            {
                _app.SendMessageToSession(session, new S2C_CreateRoomResult { Success = false, Reason = "房间组件清单不能为空" });
                NetLogger.LogWarning("ServerRoomModule", "建房拒绝: 组件清单为空", sessionId: session.SessionId);
                return;
            }

            string roomId = Guid.NewGuid().ToString("N").Substring(0, 8);
            Room room = _app.CreateRoom(roomId);
            if (room == null)
            {
                _app.SendMessageToSession(session, new S2C_CreateRoomResult { Success = false, Reason = "服务器内部错误" });
                NetLogger.LogError("ServerRoomModule", "建房失败: Room 实例创建失败", sessionId: session.SessionId);
                return;
            }

            room.Config.RoomName = string.IsNullOrWhiteSpace(msg.RoomConfig.RoomName) ? $"房间_{roomId}" : msg.RoomConfig.RoomName.Trim();
            room.Config.MaxMembers = msg.RoomConfig.MaxMembers <= 0 ? 4 : msg.RoomConfig.MaxMembers;
            room.Config.Password = msg.RoomConfig.Password ?? string.Empty;

            if (msg.RoomConfig.CustomProperties != null)
            {
                // 复制一份自定义属性，避免直接引用请求对象。
                room.Config.CustomProperties = new Dictionary<string, string>(msg.RoomConfig.CustomProperties);
            }

            if (!ServerRoomFactory.BuildComponents(room, uniqueComponentIds))
            {
                _app.DestroyRoom(roomId);
                _app.SendMessageToSession(session, new S2C_CreateRoomResult { Success = false, Reason = "房间组件装配失败" });
                NetLogger.LogError("ServerRoomModule", "建房失败: 房间组件装配失败", roomId, session.SessionId);
                return;
            }

            room.SetComponentIds(uniqueComponentIds);
            session.AuthorizeRoom(roomId);

            _app.SendMessageToSession(
                session,
                new S2C_CreateRoomResult { Success = true, RoomId = roomId, ComponentIds = uniqueComponentIds });
            NetLogger.LogInfo(
                "ServerRoomModule",
                $"建房完成: ComponentCount:{uniqueComponentIds.Length}",
                roomId,
                session.SessionId);
            BroadcastRoomListToLobby();
        }

        /// <summary>
        /// 处理加入或创建房间请求。
        /// </summary>
        [NetHandler]
        public void OnC2S_JoinOrCreateRoom(Session session, C2S_JoinOrCreateRoom msg)
        {
            NetLogger.LogInfo("ServerRoomModule", "加房或建房请求", msg.RoomId, session.SessionId);
            if (!string.IsNullOrEmpty(session.CurrentRoomId))
            {
                _app.SendMessageToSession(session, new S2C_JoinRoomResult { Success = false, Reason = "已在房间中" });
                NetLogger.LogWarning("ServerRoomModule", "加房或建房拒绝: 当前已在房间中", session.CurrentRoomId, session.SessionId);
                return;
            }

            if (string.IsNullOrEmpty(msg.RoomId))
            {
                _app.SendMessageToSession(session, new S2C_JoinRoomResult { Success = false, Reason = "必须指定有效的 RoomId" });
                NetLogger.LogWarning("ServerRoomModule", "加房或建房拒绝: RoomId 为空", sessionId: session.SessionId);
                return;
            }

            Room room = _app.GetRoom(msg.RoomId);
            if (room != null)
            {
                if (room.MemberCount >= room.Config.MaxMembers)
                {
                    _app.SendMessageToSession(session, new S2C_JoinRoomResult { Success = false, Reason = "房间人数已满" });
                    NetLogger.LogWarning("ServerRoomModule", "加房拒绝: 房间已满", room.RoomId, session.SessionId);
                    return;
                }

                string password = msg.RoomConfig != null ? msg.RoomConfig.Password ?? string.Empty : string.Empty;
                if (room.Config.IsPrivate && room.Config.Password != password)
                {
                    _app.SendMessageToSession(session, new S2C_JoinRoomResult { Success = false, Reason = "房间密码错误" });
                    NetLogger.LogWarning("ServerRoomModule", "加房拒绝: 房间密码错误", room.RoomId, session.SessionId);
                    return;
                }

                session.AuthorizeRoom(room.RoomId);
                _app.SendMessageToSession(
                    session,
                    new S2C_JoinRoomResult { Success = true, RoomId = room.RoomId, ComponentIds = room.ComponentIds });
                NetLogger.LogInfo(
                    "ServerRoomModule",
                    $"加房授权完成: ComponentCount:{room.ComponentIds.Length}",
                    room.RoomId,
                    session.SessionId);
                return;
            }

            if (msg.RoomConfig == null)
            {
                _app.SendMessageToSession(session, new S2C_CreateRoomResult { Success = false, Reason = "房间不存在且未提供降级建房配置" });
                NetLogger.LogWarning("ServerRoomModule", "降级建房拒绝: 缺少房间配置", msg.RoomId, session.SessionId);
                return;
            }

            int[] uniqueComponentIds = DeduplicateComponentIds(msg.RoomConfig.ComponentIds);
            if (uniqueComponentIds.Length == 0)
            {
                _app.SendMessageToSession(session, new S2C_CreateRoomResult { Success = false, Reason = "房间组件清单不能为空" });
                NetLogger.LogWarning("ServerRoomModule", "降级建房拒绝: 组件清单为空", msg.RoomId, session.SessionId);
                return;
            }

            Room newRoom = _app.CreateRoom(msg.RoomId);
            if (newRoom == null)
            {
                _app.SendMessageToSession(session, new S2C_CreateRoomResult { Success = false, Reason = "服务器内部错误" });
                NetLogger.LogError("ServerRoomModule", "降级建房失败: Room 实例创建失败", msg.RoomId, session.SessionId);
                return;
            }

            newRoom.Config.RoomName = string.IsNullOrWhiteSpace(msg.RoomConfig.RoomName) ? $"房间_{msg.RoomId}" : msg.RoomConfig.RoomName.Trim();
            newRoom.Config.MaxMembers = msg.RoomConfig.MaxMembers <= 0 ? 4 : msg.RoomConfig.MaxMembers;
            newRoom.Config.Password = msg.RoomConfig.Password ?? string.Empty;

            if (msg.RoomConfig.CustomProperties != null)
            {
                // 复制一份自定义属性，避免直接引用请求对象。
                newRoom.Config.CustomProperties = new Dictionary<string, string>(msg.RoomConfig.CustomProperties);
            }

            if (!ServerRoomFactory.BuildComponents(newRoom, uniqueComponentIds))
            {
                _app.DestroyRoom(msg.RoomId);
                _app.SendMessageToSession(session, new S2C_CreateRoomResult { Success = false, Reason = "房间组件装配失败" });
                NetLogger.LogError("ServerRoomModule", "降级建房失败: 房间组件装配失败", msg.RoomId, session.SessionId);
                return;
            }

            newRoom.SetComponentIds(uniqueComponentIds);
            session.AuthorizeRoom(msg.RoomId);

            _app.SendMessageToSession(
                session,
                new S2C_CreateRoomResult { Success = true, RoomId = msg.RoomId, ComponentIds = uniqueComponentIds });
            NetLogger.LogInfo(
                "ServerRoomModule",
                $"降级建房完成: ComponentCount:{uniqueComponentIds.Length}",
                msg.RoomId,
                session.SessionId);
            BroadcastRoomListToLobby();
        }

        /// <summary>
        /// 处理加入房间请求。
        /// </summary>
        [NetHandler]
        public void OnC2S_JoinRoom(Session session, C2S_JoinRoom msg)
        {
            NetLogger.LogInfo("ServerRoomModule", "加房请求", msg.RoomId, session.SessionId);
            if (string.IsNullOrEmpty(msg.RoomId))
            {
                NetLogger.LogWarning("ServerRoomModule", "加房拒绝: RoomId 为空", sessionId: session.SessionId);
                return;
            }

            if (!string.IsNullOrEmpty(session.CurrentRoomId))
            {
                _app.SendMessageToSession(session, new S2C_JoinRoomResult { Success = false, Reason = "已在房间中" });
                NetLogger.LogWarning("ServerRoomModule", "加房拒绝: 当前已在房间中", session.CurrentRoomId, session.SessionId);
                return;
            }

            Room room = _app.GetRoom(msg.RoomId);
            if (room == null)
            {
                _app.SendMessageToSession(session, new S2C_JoinRoomResult { Success = false, Reason = "房间不存在" });
                NetLogger.LogWarning("ServerRoomModule", "加房拒绝: 房间不存在", msg.RoomId, session.SessionId);
                return;
            }

            if (room.MemberCount >= room.Config.MaxMembers)
            {
                _app.SendMessageToSession(session, new S2C_JoinRoomResult { Success = false, Reason = "房间人数已满" });
                NetLogger.LogWarning("ServerRoomModule", "加房拒绝: 房间已满", room.RoomId, session.SessionId);
                return;
            }

            if (room.Config.IsPrivate && room.Config.Password != msg.Password)
            {
                _app.SendMessageToSession(session, new S2C_JoinRoomResult { Success = false, Reason = "房间密码错误" });
                NetLogger.LogWarning("ServerRoomModule", "加房拒绝: 房间密码错误", room.RoomId, session.SessionId);
                return;
            }

            session.AuthorizeRoom(room.RoomId);
            _app.SendMessageToSession(
                session,
                new S2C_JoinRoomResult { Success = true, RoomId = room.RoomId, ComponentIds = room.ComponentIds });
            NetLogger.LogInfo(
                "ServerRoomModule",
                $"加房授权完成: ComponentCount:{room.ComponentIds.Length}",
                room.RoomId,
                session.SessionId);
        }

        /// <summary>
        /// 处理房间初始化完成请求。
        /// </summary>
        [NetHandler]
        public void OnC2S_RoomSetupReady(Session session, C2S_RoomSetupReady msg)
        {
            NetLogger.LogInfo("ServerRoomModule", "房间初始化就绪请求", msg.RoomId, session.SessionId);
            if (string.IsNullOrEmpty(msg.RoomId))
            {
                NetLogger.LogWarning("ServerRoomModule", "进房拒绝: RoomId 为空", sessionId: session.SessionId);
                return;
            }

            if (session.AuthorizedRoomId != msg.RoomId)
            {
                NetLogger.LogWarning(
                    "ServerRoomModule",
                    $"进房拒绝: 授权房间不匹配, Authorized:{session.AuthorizedRoomId}",
                    msg.RoomId,
                    session.SessionId);
                return;
            }

            if (!string.IsNullOrEmpty(session.CurrentRoomId))
            {
                NetLogger.LogWarning("ServerRoomModule", "进房拒绝: 当前已在房间中", session.CurrentRoomId, session.SessionId);
                return;
            }

            Room room = _app.GetRoom(msg.RoomId);
            if (room == null)
            {
                NetLogger.LogWarning("ServerRoomModule", "进房拒绝: 房间不存在", msg.RoomId, session.SessionId);
                return;
            }

            room.AddMember(session);
            session.ClearAuthorizedRoom();

            BroadcastRoomListToLobby();
            ServerLobbyModule.BroadcastOnlinePlayerList(_app);
            NetLogger.LogInfo("ServerRoomModule", "进房完成", room.RoomId, session.SessionId);
        }

        /// <summary>
        /// 处理离开房间请求。
        /// </summary>
        [NetHandler]
        public void OnC2S_LeaveRoom(Session session, C2S_LeaveRoom msg)
        {
            string roomId = session.CurrentRoomId;
            NetLogger.LogInfo("ServerRoomModule", "离房请求", roomId, session.SessionId);
            if (!string.IsNullOrEmpty(roomId))
            {
                Room room = _app.GetRoom(roomId);
                if (room != null)
                {
                    room.RemoveMember(session);
                    if (room.MemberCount == 0)
                    {
                        _app.DestroyRoom(roomId);
                        NetLogger.LogInfo("ServerRoomModule", "空房销毁完成", roomId, session.SessionId);
                    }
                }
            }

            _app.SendMessageToSession(session, new S2C_LeaveRoomResult { Success = true });
            BroadcastRoomListToLobby();
            ServerLobbyModule.BroadcastOnlinePlayerList(_app);
            NetLogger.LogInfo("ServerRoomModule", "离房完成", roomId, session.SessionId);
        }

        /// <summary>
        /// 去重房间组件 Id 列表。
        /// </summary>
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

        /// <summary>
        /// 向大厅在线玩家广播房间列表。
        /// </summary>
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
