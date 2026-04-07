using System;
using System.Collections.Generic;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Server.Modules
{
    [ServerModule("ServerUserModule", "用户鉴权与登录模块")]
    public sealed class ServerUserModule
    {
        private readonly ServerApp _app;
        private readonly Dictionary<string, Session> _accountToSession = new Dictionary<string, Session>();

        public ServerUserModule(ServerApp app)
        {
            _app = app;
        }

        [NetHandler]
        public void OnC2S_Login(Session session, C2S_Login msg)
        {
            if (string.IsNullOrWhiteSpace(msg.AccountId)) return;

            if (string.IsNullOrWhiteSpace(msg.ClientVersion))
            {
                _app.SendMessageToSession(session, new S2C_LoginResult { Success = false, Reason = "客户端版本过旧，请更新游戏" });
                return;
            }

            CleanupInvalidAccountMappings();
            string accountId = msg.AccountId.Trim();

            if (Version.TryParse(msg.ClientVersion, out Version clientVer) && Version.TryParse(_app.Config.MinClientVersion, out Version minVer))
            {
                if (clientVer < minVer)
                {
                    _app.SendMessageToSession(session, new S2C_LoginResult { Success = false, Reason = $"客户端版本过旧，请更新至 {_app.Config.MinClientVersion} 或以上" });
                    return;
                }
            }
            else if (!string.Equals(msg.ClientVersion, _app.Config.MinClientVersion, StringComparison.Ordinal))
            {
                _app.SendMessageToSession(session, new S2C_LoginResult { Success = false, Reason = $"客户端版本不匹配，请更新至 {_app.Config.MinClientVersion}" });
                return;
            }

            if (_accountToSession.TryGetValue(accountId, out Session oldSession) && oldSession != null)
            {
                if (oldSession == session)
                {
                    bool hasRoom = !string.IsNullOrEmpty(oldSession.CurrentRoomId) && _app.GetRoom(oldSession.CurrentRoomId) != null;
                    _app.SendMessageToSession(oldSession, new S2C_LoginResult { Success = true, SessionId = oldSession.SessionId, HasReconnectRoom = hasRoom });
                    return;
                }

                if (oldSession.IsOnline)
                {
                    _app.SendMessageToSession(oldSession, new S2C_KickOut { Reason = "账号在其他设备登录" });
                    _app.UnbindConnection(oldSession);
                }

                _app.RemoveSession(session.SessionId);
                _app.BindConnection(oldSession, session.ConnectionId);
                oldSession.ResetSeq(session.LastReceivedSeq);

                bool hasReconnectRoom = !string.IsNullOrEmpty(oldSession.CurrentRoomId) && _app.GetRoom(oldSession.CurrentRoomId) != null;
                _app.SendMessageToSession(oldSession, new S2C_LoginResult { Success = true, SessionId = oldSession.SessionId, HasReconnectRoom = hasReconnectRoom });
                ServerLobbyModule.BroadcastOnlinePlayerList(_app);
                return;
            }

            _app.RemoveSession(session.SessionId);
            var authSession = new Session(session.SessionId, accountId, session.ConnectionId);
            authSession.ResetSeq(session.LastReceivedSeq);
            _accountToSession[accountId] = authSession;
            _app.RegisterSession(authSession);

            _app.SendMessageToSession(authSession, new S2C_LoginResult { Success = true, SessionId = authSession.SessionId, HasReconnectRoom = false });
            ServerLobbyModule.BroadcastOnlinePlayerList(_app);
        }

        [NetHandler]
        public void OnC2S_ConfirmReconnect(Session session, C2S_ConfirmReconnect msg)
        {
            string roomId = session.CurrentRoomId;
            Room room = string.IsNullOrEmpty(roomId) ? null : _app.GetRoom(roomId);

            if (!msg.Accept)
            {
                if (room != null) room.RemoveMember(session);
                session.UnbindRoom();
                _app.SendMessageToSession(session, new S2C_ReconnectResult { Success = false, Reason = "已放弃重连" });
                return;
            }

            if (room == null)
            {
                session.UnbindRoom();
                _app.SendMessageToSession(session, new S2C_ReconnectResult { Success = false, Reason = "房间已解散" });
                return;
            }

            _app.SendMessageToSession(session, new S2C_ReconnectResult { Success = true, RoomId = room.RoomId, ComponentIds = room.ComponentIds });
        }

        [NetHandler]
        public void OnC2S_ReconnectReady(Session session, C2S_ReconnectReady msg)
        {
            string roomId = session.CurrentRoomId;
            if (string.IsNullOrEmpty(roomId)) return;

            Room room = _app.GetRoom(roomId);
            if (room == null) return;

            session.SetRoomReady(true);
            room.TriggerReconnectSnapshot(session);
        }

        private void CleanupInvalidAccountMappings()
        {
            var invalidAccounts = new List<string>();
            foreach (var kvp in _accountToSession)
            {
                if (kvp.Value == null || !_app.Sessions.ContainsKey(kvp.Value.SessionId))
                {
                    invalidAccounts.Add(kvp.Key);
                }
            }
            for (int i = 0; i < invalidAccounts.Count; i++) _accountToSession.Remove(invalidAccounts[i]);
        }
    }
}
