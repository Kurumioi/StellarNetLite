using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using UnityEngine;

namespace StellarNet.Lite.Server.Modules
{
    public sealed class ServerUserModule
    {
        private readonly ServerApp _app;
        private readonly Action<int, Packet> _networkSender;
        private readonly Func<object, byte[]> _serializeFunc;

        private readonly Dictionary<string, Session> _accountToSession = new Dictionary<string, Session>();

        public ServerUserModule(ServerApp app, Action<int, Packet> networkSender, Func<object, byte[]> serializeFunc)
        {
            _app = app;
            _networkSender = networkSender;
            _serializeFunc = serializeFunc;
        }

        [NetHandler]
        public void OnC2S_Login(Session session, C2S_Login msg)
        {
            if (session == null || msg == null || string.IsNullOrEmpty(msg.AccountId))
            {
                Debug.LogError($"[ServerUserModule] 登录失败: 参数非法, ConnectionId: {session?.ConnectionId}");
                return;
            }

            if (_accountToSession.TryGetValue(msg.AccountId, out var oldSession))
            {
                if (oldSession.IsOnline)
                {
                    var kickMsg = new S2C_KickOut { Reason = "账号在其他设备登录" };
                    _app.SendMessageToSession(oldSession, kickMsg);
                    _app.UnbindConnection(oldSession);
                }

                _app.RemoveSession(session.SessionId);
                _app.BindConnection(oldSession, session.ConnectionId);

                bool hasRoom = !string.IsNullOrEmpty(oldSession.CurrentRoomId) &&
                               _app.GetRoom(oldSession.CurrentRoomId) != null;
                var reconnectRes = new S2C_LoginResult
                {
                    Success = true,
                    SessionId = oldSession.SessionId,
                    HasReconnectRoom = hasRoom,
                    Reason = string.Empty
                };

                _app.SendMessageToSession(oldSession, reconnectRes);
                return;
            }

            _app.RemoveSession(session.SessionId);
            var authSession = new Session(session.SessionId, msg.AccountId, session.ConnectionId);
            _accountToSession[msg.AccountId] = authSession;
            _app.RegisterSession(authSession);

            var res = new S2C_LoginResult
            {
                Success = true,
                SessionId = authSession.SessionId,
                HasReconnectRoom = false,
                Reason = string.Empty
            };

            _app.SendMessageToSession(authSession, res);
        }

        [NetHandler]
        public void OnC2S_ConfirmReconnect(Session session, C2S_ConfirmReconnect msg)
        {
            if (session == null || msg == null) return;

            string roomId = session.CurrentRoomId;
            Room room = string.IsNullOrEmpty(roomId) ? null : _app.GetRoom(roomId);

            if (!msg.Accept)
            {
                if (room != null)
                {
                    room.RemoveMember(session);
                }

                session.UnbindRoom();
                var rejectRes = new S2C_ReconnectResult { Success = false, Reason = "已放弃重连" };
                _app.SendMessageToSession(session, rejectRes);
                return;
            }

            if (room == null)
            {
                session.UnbindRoom();
                var failRes = new S2C_ReconnectResult { Success = false, Reason = "房间已解散" };
                _app.SendMessageToSession(session, failRes);
                return;
            }

            var successRes = new S2C_ReconnectResult
            {
                Success = true,
                RoomId = room.RoomId,
                ComponentIds = room.ComponentIds,
                Reason = string.Empty
            };

            _app.SendMessageToSession(session, successRes);
        }

        [NetHandler]
        public void OnC2S_ReconnectReady(Session session, C2S_ReconnectReady msg)
        {
            if (session == null || msg == null) return;

            string roomId = session.CurrentRoomId;
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError($"[ServerUserModule] 握手阻断: Session {session.SessionId} 尚未绑定房间，无法下发快照");
                return;
            }

            Room room = _app.GetRoom(roomId);
            if (room == null)
            {
                Debug.LogError($"[ServerUserModule] 握手阻断: 房间 {roomId} 不存在");
                return;
            }

            room.TriggerReconnectSnapshot(session);
            Debug.Log($"[ServerUserModule] 客户端 {session.SessionId} 装配就绪，已下发房间 {roomId} 全量快照");
        }
    }
}