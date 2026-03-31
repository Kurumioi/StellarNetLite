using System;
using System.Collections.Generic;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Server.Components
{
    // 服务端房间基础设置组件。
    [RoomComponent(1, "RoomSettings", "基础房间设置")]
    public sealed class ServerRoomSettingsComponent : RoomComponent
    {
        private readonly ServerApp _app;
        // 成员准备状态表，Key 为 SessionId。
        private readonly Dictionary<string, bool> _readyStates = new Dictionary<string, bool>();
        // 当前房主 SessionId。
        private string _ownerSessionId;

        public ServerRoomSettingsComponent(ServerApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            // 每次房间初始化都重置房主和准备态。
            _readyStates.Clear();
            _ownerSessionId = string.Empty;
        }

        // 统一组装下发给客户端的成员展示信息。
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
            if (session == null)
            {
                NetLogger.LogError("ServerRoomSettingsComponent", $"成员加入失败: session 为空, RoomId:{Room?.RoomId ?? "-"}");
                return;
            }

            if (Room == null)
            {
                NetLogger.LogError("ServerRoomSettingsComponent", $"成员加入失败: Room 为空, SessionId:{session.SessionId}");
                return;
            }

            if (string.IsNullOrEmpty(_ownerSessionId))
            {
                // 首个进房玩家默认成为房主。
                _ownerSessionId = session.SessionId;
            }

            _readyStates[session.SessionId] = false;
            // 先广播增量，再单独给新成员补一份完整快照。
            Room.BroadcastMessage(new S2C_MemberJoined
            {
                Member = CreateMemberInfo(session, false, session.SessionId == _ownerSessionId)
            });

            OnSendSnapshot(session);
        }

        public override void OnMemberLeft(Session session)
        {
            if (session == null)
            {
                NetLogger.LogError("ServerRoomSettingsComponent", $"成员离开失败: session 为空, RoomId:{Room?.RoomId ?? "-"}");
                return;
            }

            if (Room == null)
            {
                NetLogger.LogError("ServerRoomSettingsComponent", $"成员离开失败: Room 为空, SessionId:{session.SessionId}");
                return;
            }

            _readyStates.Remove(session.SessionId);
            Room.BroadcastMessage(new S2C_MemberLeft { SessionId = session.SessionId });

            if (_ownerSessionId == session.SessionId)
            {
                // 房主离开后尝试移交房主。
                MigrateHost();
            }
        }

        public override void OnMemberOffline(Session session)
        {
            if (session == null)
            {
                NetLogger.LogError("ServerRoomSettingsComponent", $"成员离线处理失败: session 为空, RoomId:{Room?.RoomId ?? "-"}");
                return;
            }

            if (_ownerSessionId == session.SessionId)
            {
                NetLogger.LogWarning("ServerRoomSettingsComponent", "房主异常离线，触发房主移交", Room?.RoomId ?? "-", session.SessionId);
                MigrateHost();
            }
        }

        private void MigrateHost()
        {
            if (Room == null)
            {
                NetLogger.LogError("ServerRoomSettingsComponent", "迁移房主失败: Room 为空");
                return;
            }

            _ownerSessionId = string.Empty;
            string fallbackSessionId = string.Empty;

            // 优先移交给在线玩家，没有在线玩家时退化为任意仍在房间的成员。
            foreach (var kvp in _readyStates)
            {
                Session memberSession = Room.GetMember(kvp.Key);
                if (memberSession == null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(fallbackSessionId))
                {
                    fallbackSessionId = kvp.Key;
                }

                if (memberSession.IsOnline)
                {
                    _ownerSessionId = kvp.Key;
                    break;
                }
            }

            if (string.IsNullOrEmpty(_ownerSessionId))
            {
                _ownerSessionId = fallbackSessionId;
            }

            if (!string.IsNullOrEmpty(_ownerSessionId))
            {
                // 房主变更后用全量快照覆盖客户端本地状态。
                BroadcastSnapshotToAll();
            }
        }

        public override void OnSendSnapshot(Session session)
        {
            if (session == null)
            {
                NetLogger.LogError("ServerRoomSettingsComponent", $"发送快照失败: session 为空, RoomId:{Room?.RoomId ?? "-"}");
                return;
            }

            if (Room == null)
            {
                NetLogger.LogError("ServerRoomSettingsComponent", $"发送快照失败: Room 为空, SessionId:{session.SessionId}");
                return;
            }

            // 快照包含房间配置和当前全部成员态。
            var members = new List<MemberInfo>();
            foreach (var kvp in _readyStates)
            {
                Session memberSession = Room.GetMember(kvp.Key);
                if (memberSession == null)
                {
                    continue;
                }

                members.Add(CreateMemberInfo(memberSession, kvp.Value, kvp.Key == _ownerSessionId));
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
                // 重连中的玩家进入游戏中房间时，额外补发游戏开始事件。
                Room.SendMessageTo(session, new S2C_GameStarted
                {
                    StartUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
            }
        }

        private void BroadcastSnapshotToAll()
        {
            if (Room == null)
            {
                NetLogger.LogError("ServerRoomSettingsComponent", "广播快照失败: Room 为空");
                return;
            }

            // 广播版快照构造逻辑与单发版保持一致。
            var members = new List<MemberInfo>();
            foreach (var kvp in _readyStates)
            {
                Session memberSession = Room.GetMember(kvp.Key);
                if (memberSession == null)
                {
                    continue;
                }

                members.Add(CreateMemberInfo(memberSession, kvp.Value, kvp.Key == _ownerSessionId));
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
            if (session == null || msg == null)
            {
                NetLogger.LogError("ServerRoomSettingsComponent", $"设置准备状态失败: session 或 msg 为空, Session:{session?.SessionId ?? "null"}");
                return;
            }

            if (Room == null)
            {
                NetLogger.LogError("ServerRoomSettingsComponent", $"设置准备状态失败: Room 为空, SessionId:{session.SessionId}");
                return;
            }

            if (!_readyStates.ContainsKey(session.SessionId))
            {
                NetLogger.LogError("ServerRoomSettingsComponent", $"设置准备状态失败: 成员不存在, SessionId:{session.SessionId}, RoomId:{Room.RoomId}");
                return;
            }

            _readyStates[session.SessionId] = msg.IsReady;
            // 准备状态变更后同步给全房间。
            Room.BroadcastMessage(new S2C_MemberReadyChanged
            {
                SessionId = session.SessionId,
                IsReady = msg.IsReady
            });
        }

        [NetHandler]
        public void OnC2S_StartGame(Session session, C2S_StartGame msg)
        {
            if (session == null)
            {
                NetLogger.LogError("ServerRoomSettingsComponent", "开始游戏失败: session 为空");
                return;
            }

            if (Room == null)
            {
                NetLogger.LogError("ServerRoomSettingsComponent", $"开始游戏失败: Room 为空, SessionId:{session.SessionId}");
                return;
            }

            if (session.SessionId != _ownerSessionId)
            {
                NetLogger.LogWarning("ServerRoomSettingsComponent", "开始游戏被拦截: 当前玩家不是房主", Room.RoomId, session.SessionId);
                return;
            }

            if (Room.State != RoomState.Waiting)
            {
                NetLogger.LogWarning("ServerRoomSettingsComponent", $"开始游戏被拦截: 房间状态非法, State:{Room.State}", Room.RoomId, session.SessionId);
                return;
            }

            foreach (var kvp in _readyStates)
            {
                // 房主以外所有人都准备后才允许开局。
                if (kvp.Key != _ownerSessionId && !kvp.Value)
                {
                    NetLogger.LogWarning("ServerRoomSettingsComponent", $"开始游戏被拦截: 存在未准备成员, MemberSessionId:{kvp.Key}", Room.RoomId, session.SessionId);
                    return;
                }
            }

            Room.StartGame();
            // 开始游戏成功后向所有成员广播开局时间。
            Room.BroadcastMessage(new S2C_GameStarted
            {
                StartUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        [NetHandler]
        public void OnC2S_EndGame(Session session, C2S_EndGame msg)
        {
            if (session == null || msg == null)
            {
                NetLogger.LogError("ServerRoomSettingsComponent", $"结束游戏失败: session 或 msg 为空, Session:{session?.SessionId ?? "null"}");
                return;
            }

            if (Room == null)
            {
                NetLogger.LogError("ServerRoomSettingsComponent", $"结束游戏失败: Room 为空, SessionId:{session.SessionId}");
                return;
            }

            if (session.SessionId != _ownerSessionId)
            {
                NetLogger.LogWarning("ServerRoomSettingsComponent", "结束游戏被拦截: 当前玩家不是房主", Room.RoomId, session.SessionId);
                return;
            }

            if (Room.State != RoomState.Playing)
            {
                NetLogger.LogWarning("ServerRoomSettingsComponent", $"结束游戏被拦截: 房间状态非法, State:{Room.State}", Room.RoomId, session.SessionId);
                return;
            }

            Room.EndGame();

            string replayId = Room.LastReplayId ?? string.Empty;
            // 当前实现里，房主主动结束会把原因直接写进 WinnerSessionId。
            Room.BroadcastMessage(new S2C_GameEnded
            {
                WinnerSessionId = "房主强制中止",
                ReplayId = replayId
            });
        }
    }
}
