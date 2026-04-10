using System;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Server.Modules
{
    /// <summary>
    /// 用户鉴权与登录模块。
    /// </summary>
    [ServerModule("ServerUserModule", "用户鉴权与登录模块")]
    public sealed class ServerUserModule
    {
        // 当前服务端应用实例。
        private readonly ServerApp _app;

        /// <summary>
        /// 创建用户鉴权模块。
        /// </summary>
        public ServerUserModule(ServerApp app)
        {
            _app = app;
        }

        /// <summary>
        /// 处理登录请求。
        /// </summary>
        [NetHandler]
        public void OnC2S_Login(Session session, C2S_Login msg)
        {
            string accountId = string.IsNullOrWhiteSpace(msg.AccountId) ? string.Empty : msg.AccountId.Trim();
            NetLogger.LogInfo(
                "ServerUserModule",
                "登录请求",
                sessionId: session.SessionId,
                extraContext: $"AccountId:{accountId}, Version:{msg.ClientVersion}");

            if (string.IsNullOrWhiteSpace(msg.AccountId))
            {
                NetLogger.LogWarning("ServerUserModule", "登录拒绝: AccountId 为空", sessionId: session.SessionId);
                return;
            }

            if (string.IsNullOrWhiteSpace(msg.ClientVersion))
            {
                _app.SendMessageToSession(session, new S2C_LoginResult { Success = false, Reason = "客户端版本过旧，请更新游戏" });
                NetLogger.LogWarning(
                    "ServerUserModule",
                    "登录拒绝: ClientVersion 为空",
                    sessionId: session.SessionId,
                    extraContext: $"AccountId:{accountId}");
                return;
            }

            string minClientVersion = _app.Config != null ? _app.Config.MinClientVersion : "0.0.1";
            if (Version.TryParse(msg.ClientVersion, out Version clientVer) && Version.TryParse(minClientVersion, out Version minVer))
            {
                if (clientVer < minVer)
                {
                    _app.SendMessageToSession(
                        session,
                        new S2C_LoginResult { Success = false, Reason = $"客户端版本过旧，请更新至 {minClientVersion} 或以上" });
                    NetLogger.LogWarning(
                        "ServerUserModule",
                        $"登录拒绝: 版本过旧, Client:{msg.ClientVersion}, Min:{minClientVersion}",
                        sessionId: session.SessionId,
                        extraContext: $"AccountId:{accountId}");
                    return;
                }
            }
            else if (!string.Equals(msg.ClientVersion, minClientVersion, StringComparison.Ordinal))
            {
                _app.SendMessageToSession(
                    session,
                    new S2C_LoginResult { Success = false, Reason = $"客户端版本不匹配，请更新至 {minClientVersion}" });
                NetLogger.LogWarning(
                    "ServerUserModule",
                    $"登录拒绝: 版本不匹配, Client:{msg.ClientVersion}, Min:{minClientVersion}",
                    sessionId: session.SessionId,
                    extraContext: $"AccountId:{accountId}");
                return;
            }

            Session oldSession = _app.GetSessionByAccountId(accountId);
            if (oldSession != null)
            {
                if (oldSession == session)
                {
                    bool hasRoom = !string.IsNullOrEmpty(oldSession.CurrentRoomId) && _app.GetRoom(oldSession.CurrentRoomId) != null;
                    _app.SendMessageToSession(
                        oldSession,
                        new S2C_LoginResult { Success = true, SessionId = oldSession.SessionId, HasReconnectRoom = hasRoom });
                    NetLogger.LogInfo(
                        "ServerUserModule",
                        $"登录复用完成: HasReconnectRoom:{hasRoom}",
                        oldSession.CurrentRoomId,
                        oldSession.SessionId,
                        $"AccountId:{accountId}");
                    return;
                }

                if (oldSession.IsOnline)
                {
                    NetLogger.LogWarning(
                        "ServerUserModule",
                        "挤下线旧会话: 账号在其他连接登录",
                        oldSession.CurrentRoomId,
                        oldSession.SessionId,
                        $"AccountId:{accountId}");
                    _app.SendMessageToSession(oldSession, new S2C_KickOut { Reason = "账号在其他设备登录" });
                    _app.UnbindConnection(oldSession);
                }

                // 先移除当前临时会话，再把物理连接切回旧会话。
                _app.RemoveSession(session.SessionId);
                _app.BindConnection(oldSession, session.ConnectionId);
                oldSession.ResetSeq(session.LastReceivedSeq);

                bool hasReconnectRoom = !string.IsNullOrEmpty(oldSession.CurrentRoomId) && _app.GetRoom(oldSession.CurrentRoomId) != null;
                _app.SendMessageToSession(
                    oldSession,
                    new S2C_LoginResult { Success = true, SessionId = oldSession.SessionId, HasReconnectRoom = hasReconnectRoom });

                NetLogger.LogInfo(
                    "ServerUserModule",
                    $"重登完成: HasReconnectRoom:{hasReconnectRoom}",
                    oldSession.CurrentRoomId,
                    oldSession.SessionId,
                    $"AccountId:{accountId}, ConnectionId:{session.ConnectionId}");
                ServerLobbyModule.BroadcastOnlinePlayerList(_app);
                return;
            }

            _app.RemoveSession(session.SessionId);

            var authSession = new Session(session.SessionId, accountId, session.ConnectionId);
            authSession.ResetSeq(session.LastReceivedSeq);
            _app.RegisterSession(authSession);

            _app.SendMessageToSession(
                authSession,
                new S2C_LoginResult { Success = true, SessionId = authSession.SessionId, HasReconnectRoom = false });

            NetLogger.LogInfo(
                "ServerUserModule",
                "首次登录完成",
                sessionId: authSession.SessionId,
                extraContext: $"AccountId:{accountId}");
            ServerLobbyModule.BroadcastOnlinePlayerList(_app);
        }

        /// <summary>
        /// 处理重连确认请求。
        /// </summary>
        [NetHandler]
        public void OnC2S_ConfirmReconnect(Session session, C2S_ConfirmReconnect msg)
        {
            string roomId = session.CurrentRoomId;
            Room room = string.IsNullOrEmpty(roomId) ? null : _app.GetRoom(roomId);
            NetLogger.LogInfo(
                "ServerUserModule",
                $"重连确认请求: Accept:{msg.Accept}",
                roomId,
                session.SessionId);

            if (!msg.Accept)
            {
                if (room != null)
                {
                    room.RemoveMember(session);
                }

                session.UnbindRoom();
                _app.SendMessageToSession(session, new S2C_ReconnectResult { Success = false, Reason = "已放弃重连" });
                NetLogger.LogInfo("ServerUserModule", "重连放弃完成", roomId, session.SessionId);
                return;
            }

            if (room == null)
            {
                session.UnbindRoom();
                _app.SendMessageToSession(session, new S2C_ReconnectResult { Success = false, Reason = "房间已解散" });
                NetLogger.LogWarning("ServerUserModule", "重连拒绝: 房间不存在", roomId, session.SessionId);
                return;
            }

            _app.SendMessageToSession(
                session,
                new S2C_ReconnectResult { Success = true, RoomId = room.RoomId, ComponentIds = room.ComponentIds });
            NetLogger.LogInfo(
                "ServerUserModule",
                $"重连确认完成: ComponentCount:{room.ComponentIds.Length}",
                room.RoomId,
                session.SessionId);
        }

        /// <summary>
        /// 处理重连就绪请求。
        /// </summary>
        [NetHandler]
        public void OnC2S_ReconnectReady(Session session, C2S_ReconnectReady msg)
        {
            string roomId = session.CurrentRoomId;
            NetLogger.LogInfo("ServerUserModule", "重连就绪请求", roomId, session.SessionId);
            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogWarning("ServerUserModule", "重连就绪跳过: 当前未绑定房间", sessionId: session.SessionId);
                return;
            }

            Room room = _app.GetRoom(roomId);
            if (room == null)
            {
                NetLogger.LogWarning("ServerUserModule", "重连就绪跳过: 房间不存在", roomId, session.SessionId);
                return;
            }

            session.SetRoomReady(true);
            room.TriggerReconnectSnapshot(session);
            NetLogger.LogInfo("ServerUserModule", "重连快照触发完成", roomId, session.SessionId);
        }
    }
}
