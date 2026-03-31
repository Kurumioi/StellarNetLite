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
        // AccountId -> 正式 Session，用于顶号和断线恢复。
        private readonly Dictionary<string, Session> _accountToSession = new Dictionary<string, Session>();

        public ServerUserModule(ServerApp app)
        {
            _app = app;
        }

        [NetHandler]
        public void OnC2S_Login(Session session, C2S_Login msg)
        {
            if (_app == null)
            {
                NetLogger.LogError("ServerUserModule", "登录失败: _app 为空");
                return;
            }

            if (session == null || msg == null)
            {
                NetLogger.LogError("ServerUserModule",
                    $"登录失败: session 或 msg 为空, Session:{session?.SessionId ?? "null"}");
                return;
            }

            if (string.IsNullOrWhiteSpace(msg.AccountId))
            {
                NetLogger.LogError("ServerUserModule", "登录失败: AccountId 为空", "-", session.SessionId);
                return;
            }

            if (string.IsNullOrWhiteSpace(msg.ClientVersion))
            {
                NetLogger.LogWarning("ServerUserModule", "登录拦截: ClientVersion 为空", "-", session.SessionId);
                _app.SendMessageToSession(session, new S2C_LoginResult
                {
                    Success = false,
                    Reason = "客户端版本过旧，请更新游戏"
                });
                return;
            }

            // 登录前先做账号映射表清理，剔除已经失效的脏引用。
            CleanupInvalidAccountMappings();

            string accountId = msg.AccountId.Trim();

            // 版本校验失败直接拦截，不进入后续会话恢复流程。
            if (Version.TryParse(msg.ClientVersion, out Version clientVer) &&
                Version.TryParse(_app.Config.MinClientVersion, out Version minVer))
            {
                if (clientVer < minVer)
                {
                    NetLogger.LogWarning(
                        "ServerUserModule",
                        $"登录拦截: 客户端版本过低, Client:{msg.ClientVersion}, Min:{_app.Config.MinClientVersion}",
                        "-",
                        session.SessionId);

                    _app.SendMessageToSession(session, new S2C_LoginResult
                    {
                        Success = false,
                        Reason = $"客户端版本过旧，请更新至 {_app.Config.MinClientVersion} 或以上版本"
                    });
                    return;
                }
            }
            else if (!string.Equals(msg.ClientVersion, _app.Config.MinClientVersion, StringComparison.Ordinal))
            {
                NetLogger.LogWarning(
                    "ServerUserModule",
                    $"登录拦截: 客户端版本不匹配, Client:{msg.ClientVersion}, Min:{_app.Config.MinClientVersion}",
                    "-",
                    session.SessionId);

                _app.SendMessageToSession(session, new S2C_LoginResult
                {
                    Success = false,
                    Reason = $"客户端版本不匹配，请更新至 {_app.Config.MinClientVersion}"
                });
                return;
            }

            // 同账号重复登录时，优先复用旧正式 Session。
            if (_accountToSession.TryGetValue(accountId, out Session oldSession) && oldSession != null)
            {
                if (oldSession == session)
                {
                    NetLogger.LogInfo("ServerUserModule", "忽略重复登录请求，返回当前会话结果", oldSession.CurrentRoomId,
                        oldSession.SessionId);

                    bool hasReconnectRoom = !string.IsNullOrEmpty(oldSession.CurrentRoomId) &&
                                            _app.GetRoom(oldSession.CurrentRoomId) != null;
                    _app.SendMessageToSession(oldSession, new S2C_LoginResult
                    {
                        Success = true,
                        SessionId = oldSession.SessionId,
                        HasReconnectRoom = hasReconnectRoom,
                        Reason = string.Empty
                    });
                    ServerLobbyModule.BroadcastOnlinePlayerList(_app);
                    return;
                }

                if (oldSession.IsOnline)
                {
                    NetLogger.LogWarning("ServerUserModule", "账号在其他设备登录，踢出旧连接", oldSession.CurrentRoomId,
                        oldSession.SessionId);
                    _app.SendMessageToSession(oldSession, new S2C_KickOut { Reason = "账号在其他设备登录" });
                    _app.UnbindConnection(oldSession);
                }

                // 顶号重连：新匿名 Session 被移除，旧正式 Session 接管新连接。
                _app.RemoveSession(session.SessionId);
                _app.BindConnection(oldSession, session.ConnectionId);
                oldSession.ResetSeq(session.LastReceivedSeq);

                bool hasRoom = !string.IsNullOrEmpty(oldSession.CurrentRoomId) &&
                               _app.GetRoom(oldSession.CurrentRoomId) != null;
                _app.SendMessageToSession(oldSession, new S2C_LoginResult
                {
                    Success = true,
                    SessionId = oldSession.SessionId,
                    HasReconnectRoom = hasRoom,
                    Reason = string.Empty
                });

                NetLogger.LogInfo("ServerUserModule", "玩家断线重连(顶号)成功", oldSession.CurrentRoomId, oldSession.SessionId);
                ServerLobbyModule.BroadcastOnlinePlayerList(_app);
                return;
            }

            // 首次登录：把匿名 Session 升级成正式账号 Session。
            _app.RemoveSession(session.SessionId);

            var authSession = new Session(session.SessionId, accountId, session.ConnectionId);
            authSession.ResetSeq(session.LastReceivedSeq);

            _accountToSession[accountId] = authSession;
            _app.RegisterSession(authSession);

            _app.SendMessageToSession(authSession, new S2C_LoginResult
            {
                Success = true,
                SessionId = authSession.SessionId,
                HasReconnectRoom = false,
                Reason = string.Empty
            });

            NetLogger.LogInfo("ServerUserModule", "玩家全新登录成功", "-", authSession.SessionId);
            ServerLobbyModule.BroadcastOnlinePlayerList(_app);
        }

        [NetHandler]
        public void OnC2S_ConfirmReconnect(Session session, C2S_ConfirmReconnect msg)
        {
            if (_app == null)
            {
                NetLogger.LogError("ServerUserModule", "确认重连失败: _app 为空");
                return;
            }

            if (session == null || msg == null)
            {
                NetLogger.LogError("ServerUserModule",
                    $"确认重连失败: session 或 msg 为空, Session:{session?.SessionId ?? "null"}");
                return;
            }

            string roomId = session.CurrentRoomId;
            Room room = string.IsNullOrEmpty(roomId) ? null : _app.GetRoom(roomId);

            // 玩家可以选择接受重连或主动放弃恢复。
            if (!msg.Accept)
            {
                if (room != null)
                {
                    room.RemoveMember(session);
                }

                session.UnbindRoom();
                _app.SendMessageToSession(session, new S2C_ReconnectResult
                {
                    Success = false,
                    Reason = "已放弃重连"
                });
                return;
            }

            if (room == null)
            {
                session.UnbindRoom();
                _app.SendMessageToSession(session, new S2C_ReconnectResult
                {
                    Success = false,
                    Reason = "房间已解散"
                });
                return;
            }

            _app.SendMessageToSession(session, new S2C_ReconnectResult
            {
                Success = true,
                RoomId = room.RoomId,
                ComponentIds = room.ComponentIds,
                Reason = string.Empty
            });
        }

        [NetHandler]
        public void OnC2S_ReconnectReady(Session session, C2S_ReconnectReady msg)
        {
            if (_app == null)
            {
                NetLogger.LogError("ServerUserModule", "重连就绪失败: _app 为空");
                return;
            }

            if (session == null || msg == null)
            {
                NetLogger.LogError("ServerUserModule",
                    $"重连就绪失败: session 或 msg 为空, Session:{session?.SessionId ?? "null"}");
                return;
            }

            string roomId = session.CurrentRoomId;
            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("ServerUserModule", "握手阻断: 尚未绑定房间", "-", session.SessionId);
                return;
            }

            Room room = _app.GetRoom(roomId);
            if (room == null)
            {
                NetLogger.LogError("ServerUserModule", "握手阻断: 房间不存在", roomId, session.SessionId);
                return;
            }

            // 客户端本地房间装配完成后，服务端才下发重连快照。
            session.SetRoomReady(true);
            room.TriggerReconnectSnapshot(session);
            NetLogger.LogInfo("ServerUserModule", "客户端重连装配就绪，已下发快照", roomId, session.SessionId);
        }

        private void CleanupInvalidAccountMappings()
        {
            var invalidAccounts = new List<string>();

            foreach (var kvp in _accountToSession)
            {
                string accountId = kvp.Key;
                Session mappedSession = kvp.Value;

                if (mappedSession == null)
                {
                    invalidAccounts.Add(accountId);
                    continue;
                }

                if (!_app.Sessions.ContainsKey(mappedSession.SessionId))
                {
                    invalidAccounts.Add(accountId);
                }
            }

            for (int i = 0; i < invalidAccounts.Count; i++)
            {
                _accountToSession.Remove(invalidAccounts[i]);
            }
        }
    }
}
