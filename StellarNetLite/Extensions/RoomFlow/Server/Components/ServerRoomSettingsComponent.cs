using System;
using System.Collections.Generic;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Server.Components
{
    /// <summary>
    /// 服务端房间基础设置组件。
    /// 负责成员快照、准备状态和房主迁移。
    /// </summary>
    [RoomComponent(1, "RoomSettings", "基础房间设置")]
    public sealed class ServerRoomSettingsComponent : ServerRoomComponent
    {
        /// <summary>
        /// 所属服务端逻辑宿主。
        /// </summary>
        private readonly ServerApp _app;

        /// <summary>
        /// 当前成员准备状态表。
        /// key 为 SessionId。
        /// </summary>
        private readonly Dictionary<string, bool> _readyStates = new Dictionary<string, bool>();

        /// <summary>
        /// 当前房主 SessionId。
        /// </summary>
        private string _ownerSessionId;

        /// <summary>
        /// 创建服务端房间设置组件。
        /// </summary>
        public ServerRoomSettingsComponent(ServerApp app)
        {
            _app = app;
        }

        /// <summary>
        /// 初始化房主与准备状态缓存。
        /// </summary>
        public override void OnInit()
        {
            _readyStates.Clear();
            _ownerSessionId = string.Empty;
        }

        /// <summary>
        /// 组装一个成员快照对象。
        /// </summary>
        private MemberInfo CreateMemberInfo(Session session, bool isReady, bool isOwner)
        {
            return new MemberInfo
            {
                SessionId = session.SessionId,
                AccountId = session.AccountId,
                DisplayName = string.IsNullOrEmpty(session.AccountId) ? "Unknown" : session.AccountId,
                IsReady = isReady,
                IsOwner = isOwner
            };
        }

        /// <summary>
        /// 成员加入后更新房主和成员快照。
        /// </summary>
        public override void OnMemberJoined(Session session)
        {
            if (string.IsNullOrEmpty(_ownerSessionId)) _ownerSessionId = session.SessionId;
            _readyStates[session.SessionId] = false;

            Room.BroadcastMessage(new S2C_MemberJoined { Member = CreateMemberInfo(session, false, session.SessionId == _ownerSessionId) });
            OnSendSnapshot(session);
        }

        /// <summary>
        /// 成员离开后刷新成员列表，必要时迁移房主。
        /// </summary>
        public override void OnMemberLeft(Session session)
        {
            _readyStates.Remove(session.SessionId);
            Room.BroadcastMessage(new S2C_MemberLeft { SessionId = session.SessionId });

            if (_ownerSessionId == session.SessionId) MigrateHost();
        }

        /// <summary>
        /// 房主离线时尝试迁移房主。
        /// </summary>
        public override void OnMemberOffline(Session session)
        {
            if (_ownerSessionId == session.SessionId) MigrateHost();
        }

        /// <summary>
        /// 在现有成员中重新选择房主。
        /// 优先在线成员，其次任意剩余成员。
        /// </summary>
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

        /// <summary>
        /// 向指定成员补发完整房间快照。
        /// </summary>
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

        /// <summary>
        /// 向全体成员广播完整房间快照。
        /// </summary>
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

        /// <summary>
        /// 处理成员准备状态切换请求。
        /// </summary>
        [NetHandler]
        public void OnC2S_SetReady(Session session, C2S_SetReady msg)
        {
            if (!_readyStates.ContainsKey(session.SessionId)) return;

            _readyStates[session.SessionId] = msg.IsReady;
            Room.BroadcastMessage(new S2C_MemberReadyChanged { SessionId = session.SessionId, IsReady = msg.IsReady });
        }

        /// <summary>
        /// 处理房主发起的开局请求。
        /// </summary>
        [NetHandler]
        public void OnC2S_StartGame(Session session, C2S_StartGame msg)
        {
            if (session.SessionId != _ownerSessionId || Room.State != RoomState.Waiting) return;

            foreach (var kvp in _readyStates)
            {
                if (kvp.Key != _ownerSessionId && !kvp.Value) return;
            }

            Room.StartGame();
            Room.BroadcastMessage(new S2C_GameStarted { StartUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        }

        /// <summary>
        /// 处理房主发起的强制结束请求。
        /// </summary>
        [NetHandler]
        public void OnC2S_EndGame(Session session, C2S_EndGame msg)
        {
            if (session.SessionId != _ownerSessionId || Room.State != RoomState.Playing) return;

            Room.EndGame();
            Room.BroadcastMessage(new S2C_GameEnded { WinnerSessionId = "房主强制中止", ReplayId = Room.LastReplayId ?? string.Empty });
        }
    }
}
