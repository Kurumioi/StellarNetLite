using System;
using System.Buffers;
using System.Collections.Generic;
using StellarNet.Lite.Server.Infrastructure;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Core
{
    public enum RoomState
    {
        Waiting,
        Playing,
        Finished
    }

    public sealed class Room
    {
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

        private readonly Dictionary<string, Session> _members = new Dictionary<string, Session>();
        public IReadOnlyDictionary<string, Session> Members => _members;

        private readonly List<RoomComponent> _components = new List<RoomComponent>();
        private readonly List<ITickableComponent> _tickableComponents = new List<ITickableComponent>();
        private readonly INetworkTransport _transport;
        private readonly INetSerializer _serializer;
        private readonly NetConfig _netConfig;
        private int _finishedTickCount;
        private int _recordStartTick;
        private bool _isDestroyed;

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

            if (State == RoomState.Finished)
            {
                NetLogger.LogWarning("Room", "拦截加入: 房间已结束", RoomId, session.SessionId);
                return;
            }

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

            State = RoomState.Playing;
            LastReplayId = string.Empty;
            StartRecord();

            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnGameStart();
            }
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

                var packet = new Packet(0, meta.Id, meta.Scope, RoomId, buffer, 0, length);

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

                var packet = new Packet(0, meta.Id, meta.Scope, RoomId, buffer, 0, length);

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

            CurrentTick++;

            for (int i = 0; i < _tickableComponents.Count; i++)
            {
                _tickableComponents[i].OnTick();
            }

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
            var sessionsToKick = new List<Session>(_members.Values);

            for (int i = 0; i < sessionsToKick.Count; i++)
            {
                Session session = sessionsToKick[i];
                if (session == null)
                {
                    continue;
                }

                var kickMsg = new StellarNet.Lite.Shared.Protocol.S2C_LeaveRoomResult { Success = true };
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
    }
}