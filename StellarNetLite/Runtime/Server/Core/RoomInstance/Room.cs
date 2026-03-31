using System;
using System.Buffers;
using System.Collections.Generic;
using StellarNet.Lite.Server.Components;
using StellarNet.Lite.Server.Infrastructure;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Replay;

namespace StellarNet.Lite.Server.Core
{
    public enum RoomState
    {
        Waiting,
        Playing,
        Finished
    }

    /// <summary>
    /// 服务端单个房间实例。
    /// 负责成员、组件、状态机、录制和房间级广播。
    /// </summary>
    public sealed class Room
    {
        // 对象关键帧采样间隔。
        private const int ReplaySnapshotIntervalTicks = 150;

        public string RoomId { get; }
        public RoomConfigModel Config { get; } = new RoomConfigModel();
        public string RoomName => Config.RoomName;
        public RoomDispatcher Dispatcher { get; }
        public bool IsRecording { get; private set; }
        public int CurrentTick { get; private set; }
        public DateTime CreateTime { get; }
        public DateTime EmptySince { get; private set; }
        public int[] ComponentIds { get; private set; }
        public int MemberCount => _members.Count;
        public RoomState State { get; private set; } = RoomState.Waiting;
        public string LastReplayId { get; private set; }

        // 房间内在线成员。
        private readonly Dictionary<string, Session> _members = new Dictionary<string, Session>();
        public IReadOnlyDictionary<string, Session> Members => _members;

        // 横向扩展组件和可 Tick 组件。
        private readonly List<RoomComponent> _components = new List<RoomComponent>();
        private readonly List<ITickableComponent> _tickableComponents = new List<ITickableComponent>();
        private readonly INetworkTransport _transport;
        private readonly INetSerializer _serializer;
        private readonly NetConfig _netConfig;

        private int _finishedTickCount;
        private int _recordStartTick;
        private bool _isDestroyed;
        private bool _hasWrittenInitialReplaySnapshot;

        public Room(string roomId, INetworkTransport transport, INetSerializer serializer, NetConfig config)
        {
            RoomId = roomId;
            Dispatcher = new RoomDispatcher(roomId);
            _transport = transport;
            _serializer = serializer;
            _netConfig = config ?? new NetConfig();
            CreateTime = DateTime.UtcNow;
            EmptySince = DateTime.UtcNow;
            CurrentTick = 0;
            State = RoomState.Waiting;
        }

        public void SetComponentIds(int[] ids)
        {
            if (_isDestroyed)
            {
                NetLogger.LogError("Room", $"设置组件清单失败: 房间已销毁, RoomId:{RoomId}");
                return;
            }

            if (ids == null)
            {
                NetLogger.LogWarning("Room", "设置组件清单失败: ids 为空", RoomId);
                return;
            }

            ComponentIds = ids;
        }

        public void AddComponent(RoomComponent component)
        {
            if (_isDestroyed)
            {
                NetLogger.LogError("Room", $"添加组件失败: 房间已销毁, RoomId:{RoomId}, Component:{component?.GetType().FullName ?? "null"}");
                return;
            }

            if (component == null)
            {
                NetLogger.LogError("Room", "添加组件失败: component 为空", RoomId);
                return;
            }

            // 组件加入房间时会自动挂上 Room 上下文。
            component.Room = this;
            _components.Add(component);

            if (component is ITickableComponent tickable)
            {
                _tickableComponents.Add(tickable);
            }
        }

        public T GetComponent<T>() where T : RoomComponent
        {
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] is T target)
                {
                    return target;
                }
            }

            return null;
        }

        public void InitializeComponents()
        {
            if (_isDestroyed)
            {
                NetLogger.LogError("Room", $"初始化组件失败: 房间已销毁, RoomId:{RoomId}");
                return;
            }

            // 装配完成后统一调用 OnInit，保证组件看到的是完整房间。
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] == null)
                {
                    NetLogger.LogError("Room", $"初始化组件失败: 第 {i} 个组件为空", RoomId);
                    continue;
                }

                _components[i].OnInit();
            }
        }

        public void AddMember(Session session)
        {
            if (_isDestroyed)
            {
                NetLogger.LogError("Room", $"加入房间失败: 房间已销毁, RoomId:{RoomId}, SessionId:{session?.SessionId ?? "null"}");
                return;
            }

            if (session == null)
            {
                NetLogger.LogError("Room", "加入房间失败: session 为空", RoomId);
                return;
            }

            if (_members.ContainsKey(session.SessionId))
            {
                NetLogger.LogWarning("Room", $"加入房间跳过: 会话已存在, SessionId:{session.SessionId}", RoomId, session.SessionId);
                return;
            }

            // Finished 房间不再接受新成员。
            if (State == RoomState.Finished)
            {
                NetLogger.LogWarning("Room", "拦截加入: 房间已结束", RoomId, session.SessionId);
                return;
            }

            // 成员正式加入后交给所有组件感知。
            _members.Add(session.SessionId, session);
            session.BindRoom(RoomId);
            EmptySince = DateTime.MaxValue;

            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnMemberJoined(session);
            }
        }

        public void RemoveMember(Session session)
        {
            if (_isDestroyed)
            {
                return;
            }

            if (session == null)
            {
                NetLogger.LogError("Room", "移出房间失败: session 为空", RoomId);
                return;
            }

            if (!_members.ContainsKey(session.SessionId))
            {
                return;
            }

            // 离房顺序是：组件通知 -> 成员字典移除 -> Session 解绑。
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnMemberLeft(session);
            }

            _members.Remove(session.SessionId);
            session.UnbindRoom();

            if (_members.Count == 0)
            {
                EmptySince = DateTime.UtcNow;
            }
        }

        public Session GetMember(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return null;
            }

            _members.TryGetValue(sessionId, out Session session);
            return session;
        }

        public void NotifyMemberOffline(Session session)
        {
            if (_isDestroyed)
            {
                return;
            }

            if (session == null || !_members.ContainsKey(session.SessionId))
            {
                return;
            }

            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnMemberOffline(session);
            }
        }

        public void NotifyMemberOnline(Session session)
        {
            if (_isDestroyed)
            {
                return;
            }

            if (session == null || !_members.ContainsKey(session.SessionId))
            {
                return;
            }

            if (State == RoomState.Finished)
            {
                NetLogger.LogWarning("Room", "拦截重连: 房间已结束，强制移出成员", RoomId, session.SessionId);
                RemoveMember(session);
                return;
            }

            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnMemberOnline(session);
            }
        }

        public void TriggerReconnectSnapshot(Session session)
        {
            if (_isDestroyed)
            {
                return;
            }

            if (session == null || !_members.ContainsKey(session.SessionId))
            {
                return;
            }

            // 重连恢复由每个组件自己决定如何给该成员补快照。
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnSendSnapshot(session);
            }
        }

        public void StartGame()
        {
            if (_isDestroyed)
            {
                NetLogger.LogError("Room", $"开始游戏失败: 房间已销毁, RoomId:{RoomId}");
                return;
            }

            if (State != RoomState.Waiting)
            {
                NetLogger.LogWarning("Room", $"开始游戏失败: 当前状态非法, State:{State}", RoomId);
                return;
            }

            // 进入 Playing 后立刻开启录制并通知组件。
            State = RoomState.Playing;
            LastReplayId = string.Empty;
            _hasWrittenInitialReplaySnapshot = false;

            StartRecord();

            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnGameStart();
            }

            TryRecordInitialReplaySnapshot();
        }

        public void EndGame(string replayDisplayName = "")
        {
            if (_isDestroyed)
            {
                return;
            }

            if (State != RoomState.Playing)
            {
                NetLogger.LogWarning("Room", $"结束游戏失败: 当前状态非法, State:{State}", RoomId);
                return;
            }

            // 结束对局前先补一次终局关键帧，保证 Seek 到结尾可恢复。
            TryRecordFinalReplaySnapshot();

            State = RoomState.Finished;
            _finishedTickCount = 0;

            if (IsRecording)
            {
                string finalName = string.IsNullOrEmpty(replayDisplayName) ? Config.RoomName : replayDisplayName;
                LastReplayId = StopRecordAndSave(finalName, CurrentTick - _recordStartTick);
            }

            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnGameEnd();
            }
        }

        public void BroadcastMessage<T>(T msg, bool recordToReplay = true) where T : class
        {
            if (_isDestroyed)
            {
                NetLogger.LogError("Room", $"广播失败: 房间已销毁, RoomId:{RoomId}, Type:{typeof(T).FullName}");
                return;
            }

            if (msg == null)
            {
                NetLogger.LogError("Room", $"广播失败: msg 为空, Type:{typeof(T).FullName}", RoomId);
                return;
            }

            if (_serializer == null)
            {
                NetLogger.LogError("Room", $"广播失败: _serializer 为空, Type:{typeof(T).FullName}", RoomId);
                return;
            }

            if (_transport == null)
            {
                NetLogger.LogError("Room", $"广播失败: _transport 为空, Type:{typeof(T).FullName}", RoomId);
                return;
            }

            // 房间广播是服务端推给所有在线且已 ready 的成员。
            if (!NetMessageMapper.TryGetMeta(typeof(T), out NetMessageMeta meta))
            {
                NetLogger.LogError("Room", $"广播失败: 未找到静态网络元数据, Type:{typeof(T).FullName}", RoomId);
                return;
            }

            if (meta.Dir != NetDir.S2C)
            {
                NetLogger.LogError("Room", $"广播阻断: 协议方向非法, MsgId:{meta.Id}, Dir:{meta.Dir}", RoomId);
                return;
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(131072);
            try
            {
                int length = _serializer.Serialize(msg, buffer);
                if (length <= 0)
                {
                    NetLogger.LogError("Room", $"广播失败: 序列化结果长度非法, MsgId:{meta.Id}, Type:{typeof(T).FullName}, Length:{length}", RoomId);
                    return;
                }

                Packet packet = new Packet(0, meta.Id, meta.Scope, RoomId, buffer, 0, length);

                // 回放只记录房间内真实广播过的消息。
                if (recordToReplay && IsRecording)
                {
                    int relativeTick = CurrentTick - _recordStartTick;
                    ServerReplayStorage.RecordFrame(RoomId, relativeTick, meta.Id, buffer, length);
                }

                foreach (KeyValuePair<string, Session> kvp in _members)
                {
                    Session session = kvp.Value;
                    if (session == null)
                    {
                        continue;
                    }

                    // 只有在线且已完成房间装配的成员才能收到房间包。
                    if (!session.IsOnline || !session.IsRoomReady)
                    {
                        continue;
                    }

                    _transport.SendToClient(session.ConnectionId, packet);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public void SendMessageTo<T>(Session session, T msg, bool recordToReplay = false) where T : class
        {
            if (_isDestroyed)
            {
                NetLogger.LogError("Room", $"发送失败: 房间已销毁, RoomId:{RoomId}, Type:{typeof(T).FullName}, SessionId:{session?.SessionId ?? "null"}");
                return;
            }

            if (session == null)
            {
                NetLogger.LogError("Room", $"发送失败: session 为空, Type:{typeof(T).FullName}", RoomId);
                return;
            }

            if (!session.IsOnline)
            {
                NetLogger.LogWarning("Room", $"发送跳过: session 离线, Type:{typeof(T).FullName}", RoomId, session.SessionId);
                return;
            }

            if (msg == null)
            {
                NetLogger.LogError("Room", $"发送失败: msg 为空, Type:{typeof(T).FullName}", RoomId, session.SessionId);
                return;
            }

            if (_serializer == null)
            {
                NetLogger.LogError("Room", $"发送失败: _serializer 为空, Type:{typeof(T).FullName}", RoomId, session.SessionId);
                return;
            }

            if (_transport == null)
            {
                NetLogger.LogError("Room", $"发送失败: _transport 为空, Type:{typeof(T).FullName}", RoomId, session.SessionId);
                return;
            }

            if (!NetMessageMapper.TryGetMeta(typeof(T), out NetMessageMeta meta))
            {
                NetLogger.LogError("Room", $"发送失败: 未找到静态网络元数据, Type:{typeof(T).FullName}", RoomId, session.SessionId);
                return;
            }

            if (meta.Dir != NetDir.S2C)
            {
                NetLogger.LogError("Room", $"发送阻断: 协议方向非法, MsgId:{meta.Id}, Dir:{meta.Dir}", RoomId, session.SessionId);
                return;
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(131072);
            try
            {
                int length = _serializer.Serialize(msg, buffer);
                if (length <= 0)
                {
                    NetLogger.LogError("Room", $"发送失败: 序列化结果长度非法, MsgId:{meta.Id}, Type:{typeof(T).FullName}, Length:{length}", RoomId, session.SessionId);
                    return;
                }

                Packet packet = new Packet(0, meta.Id, meta.Scope, RoomId, buffer, 0, length);

                if (recordToReplay && IsRecording)
                {
                    int relativeTick = CurrentTick - _recordStartTick;
                    ServerReplayStorage.RecordFrame(RoomId, relativeTick, meta.Id, buffer, length);
                }

                _transport.SendToClient(session.ConnectionId, packet);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public void StartRecord()
        {
            if (_isDestroyed)
            {
                NetLogger.LogError("Room", $"启动录制失败: 房间已销毁, RoomId:{RoomId}");
                return;
            }

            IsRecording = true;
            _recordStartTick = CurrentTick;
            _hasWrittenInitialReplaySnapshot = false;
            ServerReplayStorage.StartRecord(RoomId);
        }

        public string StopRecordAndSave(string displayName, int totalTicks)
        {
            IsRecording = false;
            string replayId = Guid.NewGuid().ToString("N");
            ServerReplayStorage.StopRecordAndSave(RoomId, replayId, displayName, ComponentIds, _netConfig, totalTicks);
            return replayId;
        }

        public void Tick()
        {
            if (_isDestroyed)
            {
                return;
            }

            // 每帧先推进逻辑时钟，再采样关键帧，再驱动组件 Tick。
            CurrentTick++;

            TryRecordPeriodicReplaySnapshot();

            for (int i = 0; i < _tickableComponents.Count; i++)
            {
                _tickableComponents[i].OnTick();
            }

            // Finished 房间会在较长延迟后清理残留成员，防止僵尸房间。
            if (State != RoomState.Finished)
            {
                return;
            }

            _finishedTickCount++;
            if (_finishedTickCount <= 18000 || _members.Count <= 0)
            {
                return;
            }

            NetLogger.LogWarning("Room", $"僵尸清理: 结算超时，强制清空残留玩家数:{_members.Count}", RoomId);
            List<Session> sessionsToKick = new List<Session>(_members.Values);
            for (int i = 0; i < sessionsToKick.Count; i++)
            {
                Session session = sessionsToKick[i];
                if (session == null)
                {
                    continue;
                }

                StellarNet.Lite.Shared.Protocol.S2C_LeaveRoomResult kickMsg = new StellarNet.Lite.Shared.Protocol.S2C_LeaveRoomResult { Success = true };
                SendMessageTo(session, kickMsg);
                RemoveMember(session);
            }
        }

        public void Destroy()
        {
            if (_isDestroyed)
            {
                return;
            }

            _isDestroyed = true;

            // Playing 状态销毁时会先走一遍正常 EndGame 收尾。
            if (State == RoomState.Playing)
            {
                EndGame();
            }

            if (IsRecording)
            {
                ServerReplayStorage.AbortRecord(RoomId);
                IsRecording = false;
            }

            for (int i = 0; i < _components.Count; i++)
            {
                RoomComponent component = _components[i];
                if (component == null)
                {
                    continue;
                }

                component.OnDestroy();
            }

            _components.Clear();
            _tickableComponents.Clear();

            // 最后统一解除成员和分发器引用。
            foreach (KeyValuePair<string, Session> kvp in _members)
            {
                Session session = kvp.Value;
                if (session == null)
                {
                    continue;
                }

                session.UnbindRoom();
            }

            _members.Clear();
            Dispatcher.Clear();
        }

        #region ================= 回放关键帧录制 =================

        /// <summary>
        /// 开局关键帧。
        /// 我只在当前房间存在对象同步组件时才写对象关键帧，
        /// 是为了明确“对象关键帧是增强能力而不是基础能力”，避免聊天室这类房间被误判成录制异常。
        /// </summary>
        private void TryRecordInitialReplaySnapshot()
        {
            if (!IsRecording)
            {
                return;
            }

            if (_hasWrittenInitialReplaySnapshot)
            {
                return;
            }

            if (!HasReplaySnapshotSupport())
            {
                return;
            }

            bool recorded = RecordReplaySnapshotAtCurrentTick();
            if (!recorded)
            {
                NetLogger.LogError("Room", $"记录开局关键帧失败, RoomId:{RoomId}, CurrentTick:{CurrentTick}, IsRecording:{IsRecording}");
                return;
            }

            _hasWrittenInitialReplaySnapshot = true;
        }

        /// <summary>
        /// 周期关键帧。
        /// 我先做组件能力判定再决定是否采样，是为了让房间层只对“支持对象世界恢复”的房间启用关键帧策略。
        /// </summary>
        private void TryRecordPeriodicReplaySnapshot()
        {
            if (!IsRecording)
            {
                return;
            }

            if (State != RoomState.Playing)
            {
                return;
            }

            if (!HasReplaySnapshotSupport())
            {
                return;
            }

            if (ReplaySnapshotIntervalTicks <= 0)
            {
                NetLogger.LogError("Room", $"记录周期关键帧失败: ReplaySnapshotIntervalTicks 非法, RoomId:{RoomId}, Interval:{ReplaySnapshotIntervalTicks}");
                return;
            }

            int relativeTick = CurrentTick - _recordStartTick;
            if (relativeTick <= 0)
            {
                return;
            }

            if (relativeTick % ReplaySnapshotIntervalTicks != 0)
            {
                return;
            }

            bool recorded = RecordReplaySnapshotAtCurrentTick();
            if (!recorded)
            {
                NetLogger.LogError("Room", $"记录周期关键帧失败, RoomId:{RoomId}, CurrentTick:{CurrentTick}, RelativeTick:{relativeTick}");
            }
        }

        /// <summary>
        /// 终局关键帧。
        /// 我只对支持对象世界的房间补终局恢复点，避免无对象房间在结算阶段产生无意义错误日志。
        /// </summary>
        private void TryRecordFinalReplaySnapshot()
        {
            if (!IsRecording)
            {
                return;
            }

            if (!HasReplaySnapshotSupport())
            {
                return;
            }

            bool recorded = RecordReplaySnapshotAtCurrentTick();
            if (!recorded)
            {
                NetLogger.LogError("Room", $"记录终局关键帧失败, RoomId:{RoomId}, CurrentTick:{CurrentTick}, IsRecording:{IsRecording}");
            }
        }

        /// <summary>
        /// 判断当前房间是否具备对象关键帧录制能力。
        /// 我把这层判定抽出来，是为了把“缺少 ObjectSync 组件”明确表达成合法能力缺省，而不是错误状态。
        /// </summary>
        private bool HasReplaySnapshotSupport()
        {
            ServerObjectSyncComponent objectSyncComponent = GetComponent<ServerObjectSyncComponent>();
            return objectSyncComponent != null;
        }

        /// <summary>
        /// 记录当前 Tick 的对象关键帧。
        /// 我在这里保留真正的异常校验，但对“房间不支持对象快照”直接静默跳过，避免把可选能力缺失误报成故障。
        /// </summary>
        private bool RecordReplaySnapshotAtCurrentTick()
        {
            if (!IsRecording)
            {
                return false;
            }

            ServerObjectSyncComponent objectSyncComponent = GetComponent<ServerObjectSyncComponent>();
            if (objectSyncComponent == null)
            {
                return false;
            }

            int relativeTick = CurrentTick - _recordStartTick;
            if (relativeTick < 0)
            {
                NetLogger.LogError("Room", $"记录对象关键帧失败: relativeTick 非法, RoomId:{RoomId}, CurrentTick:{CurrentTick}, RecordStartTick:{_recordStartTick}");
                return false;
            }

            ReplayObjectSnapshotFrame snapshotFrame = new ReplayObjectSnapshotFrame
            {
                Tick = relativeTick,
                States = objectSyncComponent.ExportSpawnStates()
            };

            ServerReplayStorage.RecordObjectSnapshotFrame(RoomId, snapshotFrame);
            return true;
        }

        #endregion
    }
}
