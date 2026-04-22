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
    /// <summary>
    /// 服务端房间状态。
    /// </summary>
    public enum RoomState
    {
        Waiting,
        Playing,
        Finished
    }

    /// <summary>
    /// 服务端房间实例。
    /// 负责成员管理、房间组件驱动、广播和录像录制。
    /// </summary>
    public sealed class Room
    {
        private const int ReplaySnapshotIntervalTicks = 150;
        private readonly object _gate = new object();

        public string RoomId { get; }
        public RoomConfigModel Config { get; } = new RoomConfigModel();
        public string RoomName => Config.RoomName;
        public RoomDispatcher Dispatcher { get; }
        public object SyncRoot => _gate;

        public bool IsRecording { get; private set; }
        public int CurrentTick { get; private set; }
        public DateTime CreateTime { get; }
        public DateTime EmptySince { get; private set; }
        public int[] ComponentIds { get; private set; }
        public int MemberCount => _members.Count;
        public RoomState State { get; private set; } = RoomState.Waiting;
        public string LastReplayId { get; private set; }
        public float CurrentRealtimeSinceStartup { get; private set; }
        public DateTime CurrentUtcNow { get; private set; }

        private readonly Dictionary<string, Session> _members = new Dictionary<string, Session>();
        public IReadOnlyDictionary<string, Session> Members => _members;
        private readonly HashSet<string> _suspendedMemberIds = new HashSet<string>();

        private readonly List<ServerRoomComponent> _components = new List<ServerRoomComponent>();
        private readonly List<ITickableComponent> _tickableComponents = new List<ITickableComponent>();

        private readonly ServerOutboundDispatcher _outboundDispatcher;
        private readonly INetSerializer _serializer;
        private readonly NetConfig _netConfig;

        private int _finishedTickCount;
        private int _recordStartTick;
        private bool _isDestroyed;
        private bool _hasWrittenInitialReplaySnapshot;
        private int _assignedWorkerId = -1;
        private float _workerAverageTickMs;
        private RoomRuntimeSnapshot _runtimeSnapshot;

        public Room(string roomId, ServerOutboundDispatcher outboundDispatcher, INetSerializer serializer, NetConfig config)
        {
            RoomId = roomId;
            Dispatcher = new RoomDispatcher(roomId);
            _outboundDispatcher = outboundDispatcher;
            _serializer = serializer;
            _netConfig = config ?? new NetConfig();
            CreateTime = DateTime.UtcNow;
            EmptySince = DateTime.UtcNow;
            CurrentTick = 0;
            State = RoomState.Waiting;
            ComponentIds = Array.Empty<int>();
            LastReplayId = string.Empty;
            _runtimeSnapshot = BuildRuntimeSnapshotUnsafe();
        }

        public void UpdateRuntimeContext(float realtimeSinceStartup, DateTime utcNow)
        {
            lock (_gate)
            {
                CurrentRealtimeSinceStartup = realtimeSinceStartup;
                CurrentUtcNow = utcNow;
                RefreshRuntimeSnapshotUnsafe();
            }
        }

        public void SetAssignedWorker(int workerId)
        {
            lock (_gate)
            {
                _assignedWorkerId = workerId;
                RefreshRuntimeSnapshotUnsafe();
            }
        }

        public void UpdateWorkerMetrics(int workerId, float averageTickMs)
        {
            lock (_gate)
            {
                _assignedWorkerId = workerId;
                _workerAverageTickMs = averageTickMs;
                RefreshRuntimeSnapshotUnsafe();
            }
        }

        public RoomRuntimeSnapshot GetRuntimeSnapshot()
        {
            return _runtimeSnapshot;
        }

        public RoomDetailedSnapshot CaptureDetailedSnapshot()
        {
            lock (_gate)
            {
                var members = new RoomMemberSnapshot[_members.Count];
                int index = 0;
                foreach (KeyValuePair<string, Session> kvp in _members)
                {
                    Session session = kvp.Value;
                    if (session == null)
                    {
                        continue;
                    }

                    members[index++] = new RoomMemberSnapshot
                    {
                        SessionId = session.SessionId,
                        AccountId = session.AccountId ?? string.Empty,
                        ConnectionId = session.ConnectionId,
                        IsOnline = session.IsOnline,
                        IsRoomReady = session.IsRoomReady,
                        LastActiveRealtime = session.LastActiveRealtime,
                        LastRoomActiveRealtime = session.LastRoomActiveRealtime
                    };
                }

                if (index != members.Length)
                {
                    Array.Resize(ref members, index);
                }

                KeyValuePair<string, string>[] customProperties = Array.Empty<KeyValuePair<string, string>>();
                if (Config.CustomProperties != null && Config.CustomProperties.Count > 0)
                {
                    customProperties = new KeyValuePair<string, string>[Config.CustomProperties.Count];
                    int propIndex = 0;
                    foreach (KeyValuePair<string, string> kvp in Config.CustomProperties)
                    {
                        customProperties[propIndex++] = kvp;
                    }
                }

                return new RoomDetailedSnapshot
                {
                    Runtime = BuildRuntimeSnapshotUnsafe(),
                    CustomPropertyCount = Config.CustomProperties != null ? Config.CustomProperties.Count : 0,
                    CustomProperties = customProperties,
                    Members = members
                };
            }
        }

        public void SetComponentIds(int[] ids)
        {
            lock (_gate)
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
                RefreshRuntimeSnapshotUnsafe();
            }
        }

        public void AddComponent(ServerRoomComponent component)
        {
            lock (_gate)
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

                component.Room = this;
                _components.Add(component);
                if (component is ITickableComponent tickable)
                {
                    _tickableComponents.Add(tickable);
                }
            }
        }

        public T GetComponent<T>() where T : ServerRoomComponent
        {
            lock (_gate)
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
        }

        public void InitializeComponents()
        {
            lock (_gate)
            {
                if (_isDestroyed)
                {
                    NetLogger.LogError("Room", $"初始化组件失败: 房间已销毁, RoomId:{RoomId}");
                    return;
                }

                for (int i = 0; i < _components.Count; i++)
                {
                    if (_components[i] == null)
                    {
                        NetLogger.LogError("Room", $"初始化组件失败: 第 {i} 个组件为空", RoomId);
                        continue;
                    }

                    _components[i].OnInit();
                }

                RefreshRuntimeSnapshotUnsafe();
            }
        }

        public bool AddMember(Session session)
        {
            return TryAddMember(session, true, out _);
        }

        public bool TryAddMember(Session session, bool notifyComponents, out string reason)
        {
            lock (_gate)
            {
                reason = string.Empty;
                if (_isDestroyed)
                {
                    NetLogger.LogError("Room", $"加入房间失败: 房间已销毁, RoomId:{RoomId}, SessionId:{session?.SessionId ?? "null"}");
                    reason = "房间已销毁";
                    return false;
                }

                if (session == null)
                {
                    NetLogger.LogError("Room", "加入房间失败: session 为空", RoomId);
                    reason = "会话为空";
                    return false;
                }

                if (_members.ContainsKey(session.SessionId))
                {
                    NetLogger.LogWarning("Room", $"加入房间跳过: 会话已存在, SessionId:{session.SessionId}", RoomId, session.SessionId);
                    reason = "会话已在房间中";
                    return false;
                }

                if (State == RoomState.Finished)
                {
                    NetLogger.LogWarning("Room", "拦截加入: 房间已结束", RoomId, session.SessionId);
                    reason = "房间已结束";
                    return false;
                }

                if (_members.Count >= Config.MaxMembers)
                {
                    NetLogger.LogWarning("Room", $"拦截加入: 房间人数已满, Count:{_members.Count}, Max:{Config.MaxMembers}", RoomId, session.SessionId);
                    reason = "房间人数已满";
                    return false;
                }

                _members.Add(session.SessionId, session);
                _suspendedMemberIds.Remove(session.SessionId);
                session.BindRoom(RoomId);
                session.SetRoomReady(notifyComponents);
                EmptySince = DateTime.MaxValue;
                RefreshRuntimeSnapshotUnsafe();
                if (notifyComponents)
                {
                    NotifyMemberJoined(session);
                }

                return true;
            }
        }

        public void NotifyMemberJoined(Session session)
        {
            lock (_gate)
            {
                if (_isDestroyed || session == null || !_members.ContainsKey(session.SessionId))
                {
                    return;
                }

                for (int i = 0; i < _components.Count; i++)
                {
                    _components[i].OnMemberJoined(session);
                }

                RefreshRuntimeSnapshotUnsafe();
            }
        }

        public void RemoveMember(Session session)
        {
            lock (_gate)
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

                for (int i = 0; i < _components.Count; i++)
                {
                    _components[i].OnMemberLeft(session);
                }

                _members.Remove(session.SessionId);
                _suspendedMemberIds.Remove(session.SessionId);
                if (session.CurrentRoomId == RoomId)
                {
                    session.UnbindRoom();
                }
                if (_members.Count == 0)
                {
                    EmptySince = DateTime.UtcNow;
                }

                RefreshRuntimeSnapshotUnsafe();
            }
        }

        public Session GetMember(string sessionId)
        {
            lock (_gate)
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    return null;
                }

                _members.TryGetValue(sessionId, out Session session);
                return session;
            }
        }

        public void NotifyMemberOffline(Session session)
        {
            lock (_gate)
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

                RefreshRuntimeSnapshotUnsafe();
            }
        }

        public bool SuspendMember(Session session, out string reason)
        {
            lock (_gate)
            {
                reason = string.Empty;
                if (_isDestroyed)
                {
                    reason = "房间已销毁";
                    return false;
                }

                if (session == null)
                {
                    reason = "会话为空";
                    return false;
                }

                if (!_members.ContainsKey(session.SessionId))
                {
                    reason = "当前不在房间中";
                    return false;
                }

                _suspendedMemberIds.Add(session.SessionId);
                session.SetRecoverableRoom(RoomId);
                session.UnbindRoom();

                for (int i = 0; i < _components.Count; i++)
                {
                    _components[i].OnMemberOffline(session);
                }

                RefreshRuntimeSnapshotUnsafe();
                return true;
            }
        }

        public bool ResumeMember(Session session, out string reason)
        {
            lock (_gate)
            {
                reason = string.Empty;
                if (_isDestroyed)
                {
                    reason = "房间已销毁";
                    return false;
                }

                if (session == null)
                {
                    reason = "会话为空";
                    return false;
                }

                if (!_members.ContainsKey(session.SessionId))
                {
                    reason = "房间恢复上下文已失效";
                    return false;
                }

                if (!_suspendedMemberIds.Contains(session.SessionId))
                {
                    reason = "当前不是挂起成员";
                    return false;
                }

                _suspendedMemberIds.Remove(session.SessionId);
                session.BindRoom(RoomId);
                session.SetRoomReady(false);

                for (int i = 0; i < _components.Count; i++)
                {
                    _components[i].OnMemberOnline(session);
                }

                RefreshRuntimeSnapshotUnsafe();
                return true;
            }
        }

        public void NotifyMemberOnline(Session session)
        {
            lock (_gate)
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

                RefreshRuntimeSnapshotUnsafe();
            }
        }

        public void TriggerReconnectSnapshot(Session session)
        {
            lock (_gate)
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
                    _components[i].OnSendSnapshot(session);
                }
            }
        }

        public void StartGame()
        {
            lock (_gate)
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

                State = RoomState.Playing;
                LastReplayId = string.Empty;
                _hasWrittenInitialReplaySnapshot = false;
                StartRecord();
                for (int i = 0; i < _components.Count; i++)
                {
                    _components[i].OnGameStart();
                }

                TryRecordInitialReplaySnapshot();
                RefreshRuntimeSnapshotUnsafe();
            }
        }

        public void EndGame(string replayDisplayName = "")
        {
            lock (_gate)
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

                RefreshRuntimeSnapshotUnsafe();
            }
        }

        public void BroadcastMessage<T>(T msg, bool recordToReplay = true) where T : class
        {
            lock (_gate)
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

                if (_outboundDispatcher == null)
                {
                    NetLogger.LogError("Room", $"广播失败: _outboundDispatcher 为空, Type:{typeof(T).FullName}", RoomId);
                    return;
                }

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
                int length = _serializer.Serialize(msg, buffer);
                if (length <= 0)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    NetLogger.LogError("Room", $"广播失败: 序列化结果长度非法, MsgId:{meta.Id}, Type:{typeof(T).FullName}, Length:{length}", RoomId);
                    return;
                }

                if (recordToReplay && IsRecording)
                {
                    int relativeTick = CurrentTick - _recordStartTick;
                    ServerReplayStorage.RecordFrame(RoomId, relativeTick, meta.Id, buffer, length);
                }

                int[] targets = new int[_members.Count];
                int targetCount = 0;
                foreach (KeyValuePair<string, Session> kvp in _members)
                {
                    Session session = kvp.Value;
                    if (session == null || !session.IsOnline || !session.IsRoomReady)
                    {
                        continue;
                    }

                    targets[targetCount++] = session.ConnectionId;
                }

                if (targetCount <= 0)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    return;
                }

                Packet packet = new Packet(0, meta.Id, meta.Scope, RoomId, buffer, 0, length);
                _outboundDispatcher.EnqueueMany(targets, targetCount, packet, true);
            }
        }

        public void SendMessageTo<T>(Session session, T msg, bool recordToReplay = false) where T : class
        {
            lock (_gate)
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

                if (_outboundDispatcher == null)
                {
                    NetLogger.LogError("Room", $"发送失败: _outboundDispatcher 为空, Type:{typeof(T).FullName}", RoomId, session.SessionId);
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
                int length = _serializer.Serialize(msg, buffer);
                if (length <= 0)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    NetLogger.LogError("Room", $"发送失败: 序列化结果长度非法, MsgId:{meta.Id}, Type:{typeof(T).FullName}, Length:{length}", RoomId,
                        session.SessionId);
                    return;
                }

                Packet packet = new Packet(0, meta.Id, meta.Scope, RoomId, buffer, 0, length);
                if (recordToReplay && IsRecording)
                {
                    int relativeTick = CurrentTick - _recordStartTick;
                    ServerReplayStorage.RecordFrame(RoomId, relativeTick, meta.Id, buffer, length);
                }

                _outboundDispatcher.EnqueueSingle(session.ConnectionId, packet, true);
            }
        }

        public void RecordMessageToReplay<T>(T msg) where T : class
        {
            lock (_gate)
            {
                if (_isDestroyed || !IsRecording || msg == null || _serializer == null)
                {
                    return;
                }

                if (!NetMessageMapper.TryGetMeta(typeof(T), out NetMessageMeta meta))
                {
                    NetLogger.LogError("Room", $"回放记录失败: 未找到静态网络元数据, Type:{typeof(T).FullName}", RoomId);
                    return;
                }

                byte[] buffer = ArrayPool<byte>.Shared.Rent(131072);
                try
                {
                    int length = _serializer.Serialize(msg, buffer);
                    if (length <= 0)
                    {
                        return;
                    }

                    int relativeTick = CurrentTick - _recordStartTick;
                    ServerReplayStorage.RecordFrame(RoomId, relativeTick, meta.Id, buffer, length);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }

        public void StartRecord()
        {
            lock (_gate)
            {
                if (_isDestroyed)
                {
                    NetLogger.LogError("Room", $"启动录制失败: 房间已销毁, RoomId:{RoomId}");
                    return;
                }

                if (_netConfig != null && !_netConfig.EnableReplayRecording)
                {
                    IsRecording = false;
                    _hasWrittenInitialReplaySnapshot = false;
                    RefreshRuntimeSnapshotUnsafe();
                    return;
                }

                if (!Config.EnableReplayRecording)
                {
                    IsRecording = false;
                    _hasWrittenInitialReplaySnapshot = false;
                    RefreshRuntimeSnapshotUnsafe();
                    return;
                }

                IsRecording = true;
                _recordStartTick = CurrentTick;
                _hasWrittenInitialReplaySnapshot = false;
                ServerReplayStorage.StartRecord(RoomId);
                RefreshRuntimeSnapshotUnsafe();
            }
        }

        public string StopRecordAndSave(string displayName, int totalTicks)
        {
            lock (_gate)
            {
                IsRecording = false;
                string replayId = Guid.NewGuid().ToString("N");
                ServerReplayStorage.StopRecordAndSave(RoomId, replayId, displayName, ComponentIds, _netConfig, totalTicks);
                RefreshRuntimeSnapshotUnsafe();
                return replayId;
            }
        }

        public void DispatchPacket(Session session, Packet packet)
        {
            lock (_gate)
            {
                if (_isDestroyed)
                {
                    return;
                }

                Dispatcher.Dispatch(session, packet);
            }
        }

        public void Tick()
        {
            lock (_gate)
            {
                if (_isDestroyed)
                {
                    return;
                }

                CurrentTick++;
                TryRecordPeriodicReplaySnapshot();
                for (int i = 0; i < _tickableComponents.Count; i++)
                {
                    _tickableComponents[i].OnTick();
                }

                if (State == RoomState.Finished)
                {
                    _finishedTickCount++;
                    if (_finishedTickCount > 18000 && _members.Count > 0)
                    {
                        NetLogger.LogWarning("Room", $"僵尸清理: 结算超时，强制清空残留玩家数:{_members.Count}", RoomId);
                        List<Session> sessionsToKick = new List<Session>(_members.Values);
                        for (int i = 0; i < sessionsToKick.Count; i++)
                        {
                            Session session = sessionsToKick[i];
                            if (session == null)
                            {
                                continue;
                            }

                            StellarNet.Lite.Shared.Protocol.S2C_LeaveRoomResult kickMsg =
                                new StellarNet.Lite.Shared.Protocol.S2C_LeaveRoomResult { Success = true };
                            SendMessageTo(session, kickMsg);
                            RemoveMember(session);
                        }
                    }
                }

                RefreshRuntimeSnapshotUnsafe();
            }
        }

        public void Destroy()
        {
            lock (_gate)
            {
                if (_isDestroyed)
                {
                    return;
                }

                if (State == RoomState.Playing)
                {
                    EndGame();
                }

                _isDestroyed = true;

                if (IsRecording)
                {
                    ServerReplayStorage.AbortRecord(RoomId);
                    IsRecording = false;
                }

                for (int i = 0; i < _components.Count; i++)
                {
                    ServerRoomComponent component = _components[i];
                    if (component == null)
                    {
                        continue;
                    }

                    component.OnDestroy();
                }

                _components.Clear();
                _tickableComponents.Clear();
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
                RefreshRuntimeSnapshotUnsafe();
            }
        }

        private void RefreshRuntimeSnapshotUnsafe()
        {
            _runtimeSnapshot = BuildRuntimeSnapshotUnsafe();
        }

        private RoomRuntimeSnapshot BuildRuntimeSnapshotUnsafe()
        {
            int onlineMemberCount = 0;
            foreach (KeyValuePair<string, Session> kvp in _members)
            {
                Session session = kvp.Value;
                if (session != null && session.IsOnline)
                {
                    onlineMemberCount++;
                }
            }

            int[] componentIds = ComponentIds ?? Array.Empty<int>();
            int[] componentCopy = componentIds.Length > 0 ? (int[])componentIds.Clone() : Array.Empty<int>();
            return new RoomRuntimeSnapshot
            {
                RoomId = RoomId,
                RoomName = Config.RoomName ?? string.Empty,
                State = State,
                MemberCount = _members.Count,
                OnlineMemberCount = onlineMemberCount,
                MaxMembers = Config.MaxMembers,
                IsPrivate = Config.IsPrivate,
                CurrentTick = CurrentTick,
                IsRecording = IsRecording,
                LastReplayId = LastReplayId ?? string.Empty,
                ComponentIds = componentCopy,
                CreateTimeUtc = CreateTime,
                EmptySinceUtc = EmptySince,
                AssignedWorkerId = _assignedWorkerId,
                WorkerAverageTickMs = _workerAverageTickMs
            };
        }

        #region ================= 回放关键帧录制 (核心解耦) =================

        private void TryRecordInitialReplaySnapshot()
        {
            if (!IsRecording || _hasWrittenInitialReplaySnapshot || !HasReplaySnapshotSupport()) return;
            bool recorded = RecordReplaySnapshotAtCurrentTick();
            if (!recorded)
            {
                NetLogger.LogError("Room", $"记录开局关键帧失败, RoomId:{RoomId}, CurrentTick:{CurrentTick}, IsRecording:{IsRecording}");
                return;
            }

            _hasWrittenInitialReplaySnapshot = true;
        }

        private void TryRecordPeriodicReplaySnapshot()
        {
            if (!IsRecording || State != RoomState.Playing || !HasReplaySnapshotSupport()) return;

            int relativeTick = CurrentTick - _recordStartTick;
            if (relativeTick <= 0 || relativeTick % ReplaySnapshotIntervalTicks != 0) return;

            bool recorded = RecordReplaySnapshotAtCurrentTick();
            if (!recorded)
            {
                NetLogger.LogError("Room", $"记录周期关键帧失败, RoomId:{RoomId}, CurrentTick:{CurrentTick}, RelativeTick:{relativeTick}");
            }
        }

        private void TryRecordFinalReplaySnapshot()
        {
            if (!IsRecording || !HasReplaySnapshotSupport()) return;
            bool recorded = RecordReplaySnapshotAtCurrentTick();
            if (!recorded)
            {
                NetLogger.LogError("Room", $"记录终局关键帧失败, RoomId:{RoomId}, CurrentTick:{CurrentTick}, IsRecording:{IsRecording}");
            }
        }

        private bool HasReplaySnapshotSupport()
        {
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] is IReplaySnapshotProvider) return true;
            }

            return false;
        }

        private bool RecordReplaySnapshotAtCurrentTick()
        {
            if (!IsRecording) return false;

            int relativeTick = CurrentTick - _recordStartTick;
            if (relativeTick < 0) return false;

            var snapshots = new List<ComponentSnapshotData>();
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] is IReplaySnapshotProvider provider)
                {
                    byte[] payload = provider.ExportSnapshot();
                    if (payload != null)
                    {
                        snapshots.Add(new ComponentSnapshotData
                        {
                            ComponentId = provider.SnapshotComponentId,
                            Payload = payload
                        });
                    }
                }
            }

            if (snapshots.Count == 0) return false;

            ReplaySnapshotFrame snapshotFrame = new ReplaySnapshotFrame
            {
                Tick = relativeTick,
                ComponentSnapshots = snapshots.ToArray()
            };

            ServerReplayStorage.RecordSnapshotFrame(RoomId, snapshotFrame);
            return true;
        }

        #endregion
    }
}
