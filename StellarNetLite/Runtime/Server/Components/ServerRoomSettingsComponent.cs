using System;
using System.Collections.Generic;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Server.Components
{
    [RoomComponent(1, "RoomSettings", "基础房间设置")]
    public sealed class ServerRoomSettingsComponent : RoomComponent
    {
        private readonly ServerApp _app;
        private readonly Dictionary<string, bool> _readyStates = new Dictionary<string, bool>();
        private string _ownerSessionId;

        public ServerRoomSettingsComponent(ServerApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            _readyStates.Clear();
            _ownerSessionId = string.Empty;
        }

        private MemberInfo CreateMemberInfo(Session session, bool isReady, bool isOwner)
        {
            return new MemberInfo
            {
                SessionId = session.SessionId,
                Uid = session.Uid,
                DisplayName = string.IsNullOrEmpty(session.Uid) ? "Unknown" : session.Uid,
                IsReady = isReady,
                IsOwner = isOwner
            };
        }

        public override void OnMemberJoined(Session session)
        {
            if (string.IsNullOrEmpty(_ownerSessionId)) _ownerSessionId = session.SessionId;
            _readyStates[session.SessionId] = false;

            Room.BroadcastMessage(new S2C_MemberJoined { Member = CreateMemberInfo(session, false, session.SessionId == _ownerSessionId) });
            OnSendSnapshot(session);
        }

        public override void OnMemberLeft(Session session)
        {
            _readyStates.Remove(session.SessionId);
            Room.BroadcastMessage(new S2C_MemberLeft { SessionId = session.SessionId });
            if (_ownerSessionId == session.SessionId) MigrateHost();
        }

        public override void OnMemberOffline(Session session)
        {
            if (_ownerSessionId == session.SessionId) MigrateHost();
        }

        private void MigrateHost()
        {
            _ownerSessionId = string.Empty;
            string fallbackSessionId = string.Empty;

            foreach (var kvp in _readyStates)
            {
                Session memberSession = Room.GetMember(kvp.Key);
                if (memberSession == null) continue;

                if (string.IsNullOrEmpty(fallbackSessionId)) fallbackSessionId = kvp.Key;
                if (memberSession.IsOnline)
                {
                    _ownerSessionId = kvp.Key;
                    break;
                }
            }

            if (string.IsNullOrEmpty(_ownerSessionId)) _ownerSessionId = fallbackSessionId;
            if (!string.IsNullOrEmpty(_ownerSessionId)) BroadcastSnapshotToAll();
        }

        public override void OnSendSnapshot(Session session)
        {
            var members = new List<MemberInfo>();
            foreach (var kvp in _readyStates)
            {
                Session memberSession = Room.GetMember(kvp.Key);
                if (memberSession != null) members.Add(CreateMemberInfo(memberSession, kvp.Value, kvp.Key == _ownerSessionId));
            }

            Room.SendMessageTo(session, new S2C_RoomSnapshot
            {
                RoomName = Room.Config.RoomName,
                MaxMembers = Room.Config.MaxMembers,
                IsPrivate = Room.Config.IsPrivate,
                Members = members.ToArray()
            });

            if (Room.State == RoomState.Playing)
            {
                Room.SendMessageTo(session, new S2C_GameStarted { StartUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            }
        }

        private void BroadcastSnapshotToAll()
        {
            var members = new List<MemberInfo>();
            foreach (var kvp in _readyStates)
            {
                Session memberSession = Room.GetMember(kvp.Key);
                if (memberSession != null) members.Add(CreateMemberInfo(memberSession, kvp.Value, kvp.Key == _ownerSessionId));
            }

            Room.BroadcastMessage(new S2C_RoomSnapshot
            {
                RoomName = Room.Config.RoomName,
                MaxMembers = Room.Config.MaxMembers,
                IsPrivate = Room.Config.IsPrivate,
                Members = members.ToArray()
            });
        }

        [NetHandler]
        public void OnC2S_SetReady(Session session, C2S_SetReady msg)
        {
            if (!_readyStates.ContainsKey(session.SessionId)) return;
            _readyStates[session.SessionId] = msg.IsReady;
            Room.BroadcastMessage(new S2C_MemberReadyChanged { SessionId = session.SessionId, IsReady = msg.IsReady });
        }

        [NetHandler]
        public void OnC2S_StartGame(Session session, C2S_StartGame msg)
        {
            if (session.SessionId != _ownerSessionId || Room.State != RoomState.Waiting) return;

            foreach (var kvp in _readyStates)
            {
                if (kvp.Key != _ownerSessionId && !kvp.Value) return; // 有人未准备
            }

            Room.StartGame();
            Room.BroadcastMessage(new S2C_GameStarted { StartUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        }

        [NetHandler]
        public void OnC2S_EndGame(Session session, C2S_EndGame msg)
        {
            if (session.SessionId != _ownerSessionId || Room.State != RoomState.Playing) return;

            Room.EndGame();
            Room.BroadcastMessage(new S2C_GameEnded { WinnerSessionId = "房主强制中止", ReplayId = Room.LastReplayId ?? string.Empty });
        }
    }
}