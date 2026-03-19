using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Server.Infrastructure;

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

        private int _finishedTickCount = 0;
        private int _recordStartTick = 0;

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
            if (ids == null) return;
            ComponentIds = ids;
        }

        public void AddComponent(RoomComponent component)
        {
            if (component == null) return;
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
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnInit();
            }
        }

        public void AddMember(Session session)
        {
            if (session == null || _members.ContainsKey(session.SessionId)) return;

            if (State == RoomState.Finished)
            {
                NetLogger.LogWarning("Room", "拦截加入: 房间已结束，拒绝加入", RoomId, session.SessionId);
                return;
            }

            _members[session.SessionId] = session;
            session.BindRoom(RoomId);
            EmptySince = DateTime.MaxValue;

            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnMemberJoined(session);
            }
        }

        public void RemoveMember(Session session)
        {
            if (session == null || !_members.ContainsKey(session.SessionId)) return;

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
            if (string.IsNullOrEmpty(sessionId)) return null;
            _members.TryGetValue(sessionId, out var session);
            return session;
        }

        public void NotifyMemberOffline(Session session)
        {
            if (session == null || !_members.ContainsKey(session.SessionId)) return;
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnMemberOffline(session);
            }
        }

        public void NotifyMemberOnline(Session session)
        {
            if (session == null || !_members.ContainsKey(session.SessionId)) return;

            if (State == RoomState.Finished)
            {
                NetLogger.LogWarning("Room", "拦截重连: 房间已结束，强制将重连玩家移出房间", RoomId, session.SessionId);
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
            if (session == null || !_members.ContainsKey(session.SessionId)) return;
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnSendSnapshot(session);
            }
        }

        public void StartGame()
        {
            if (State != RoomState.Waiting) return;

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
            if (State != RoomState.Playing) return;

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
            if (!NetMessageMapper.TryGetMeta(typeof(T), out var meta))
            {
                NetLogger.LogError("Room", $"广播失败: 未找到类型 {typeof(T).Name} 的网络元数据", RoomId);
                return;
            }

            if (meta.Dir != NetDir.S2C)
            {
                NetLogger.LogError("Room", $"广播阻断: 协议 {meta.Id} 方向为 {meta.Dir}，服务端只能发送 S2C", RoomId);
                return;
            }

            // 核心修复：将 Buffer 提升至 128KB
            byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(131072);
            try
            {
                int length = _serializer.Serialize(msg, buffer);
                var packet = new Packet(0, meta.Id, meta.Scope, RoomId, buffer, length);

                if (recordToReplay && IsRecording)
                {
                    int relativeTick = CurrentTick - _recordStartTick;
                    ServerReplayStorage.RecordFrame(RoomId, relativeTick, meta.Id, buffer, length);
                }

                foreach (var kvp in _members)
                {
                    var session = kvp.Value;
                    if (session.IsOnline && session.IsRoomReady)
                    {
                        _transport?.SendToClient(session.ConnectionId, packet);
                    }
                }
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public void SendMessageTo<T>(Session session, T msg, bool recordToReplay = false) where T : class
        {
            if (session == null || !session.IsOnline) return;

            if (!NetMessageMapper.TryGetMeta(typeof(T), out var meta))
            {
                NetLogger.LogError("Room", $"发送失败: 未找到类型 {typeof(T).Name} 的网络元数据", RoomId, session.SessionId);
                return;
            }

            if (meta.Dir != NetDir.S2C)
            {
                NetLogger.LogError("Room", $"发送阻断: 协议 {meta.Id} 方向为 {meta.Dir}，服务端只能发送 S2C", RoomId, session.SessionId);
                return;
            }

            // 核心修复：将 Buffer 提升至 128KB
            byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(131072);
            try
            {
                int length = _serializer.Serialize(msg, buffer);
                var packet = new Packet(0, meta.Id, meta.Scope, RoomId, buffer, length);

                if (recordToReplay && IsRecording)
                {
                    int relativeTick = CurrentTick - _recordStartTick;
                    ServerReplayStorage.RecordFrame(RoomId, relativeTick, meta.Id, buffer, length);
                }

                _transport?.SendToClient(session.ConnectionId, packet);
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public void StartRecord()
        {
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
            CurrentTick++;

            for (int i = 0; i < _tickableComponents.Count; i++)
            {
                _tickableComponents[i].OnTick();
            }

            if (State == RoomState.Finished)
            {
                _finishedTickCount++;
                if (_finishedTickCount > 18000 && _members.Count > 0)
                {
                    NetLogger.LogWarning("Room", $"僵尸清理: 结算已超时(5分钟)，强制清空残留的 {_members.Count} 名玩家", RoomId);
                    var sessionsToKick = new System.Collections.Generic.List<Session>(_members.Values);
                    foreach (var s in sessionsToKick)
                    {
                        var kickMsg = new StellarNet.Lite.Shared.Protocol.S2C_LeaveRoomResult { Success = true };
                        SendMessageTo(s, kickMsg);
                        RemoveMember(s);
                    }
                }
            }
        }

        public void Destroy()
        {
            if (State == RoomState.Playing)
            {
                EndGame();
            }

            if (IsRecording)
            {
                ServerReplayStorage.AbortRecord(RoomId);
            }

            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnDestroy();
            }

            _components.Clear();
            _tickableComponents.Clear();

            foreach (var kvp in _members)
            {
                kvp.Value.UnbindRoom();
            }

            _members.Clear();
            Dispatcher.Clear();
        }
    }
}