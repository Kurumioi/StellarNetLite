using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using StellarNet.Lite.Server.Infrastructure;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
namespace StellarNet.Lite.Server.Core
{
    /// <summary>
    /// 服务端主状态机。
    /// </summary>
    public sealed class ServerApp
    {
        public GlobalDispatcher GlobalDispatcher { get; } = new GlobalDispatcher();
        public ServerPersistenceRuntime PersistenceRuntime { get; } = new ServerPersistenceRuntime();
        public IReadOnlyDictionary<string, Room> Rooms => _rooms;
        public IReadOnlyDictionary<string, Session> Sessions => _sessions;
        public NetConfig Config { get; }
        public object SyncRoot { get; } = new object();
        public float CurrentRealtimeSinceStartup { get; private set; }
        public DateTime CurrentUtcNow { get; private set; }
        public ServerRoomScheduler RoomScheduler { get; private set; }
        public IRoomRecordingService RoomRecordingService { get; set; }
        public IRoomMembershipNotifier RoomMembershipNotifier { get; set; }

        private readonly INetworkTransport _transport;
        private readonly INetSerializer _serializer;
        private readonly ServerOutboundDispatcher _outboundDispatcher = new ServerOutboundDispatcher();

        #region 核心状态字典 (路由索引)
        private readonly Dictionary<string, Session> _sessions = new Dictionary<string, Session>();
        private readonly Dictionary<int, Session> _connectionToSession = new Dictionary<int, Session>();
        private readonly Dictionary<string, Session> _accountToSession = new Dictionary<string, Session>();
        private readonly Dictionary<string, Room> _rooms = new Dictionary<string, Room>();
        private readonly Dictionary<Type, object> _globalModules = new Dictionary<Type, object>();
        private readonly HashSet<int> _unauthenticatedGlobalMsgIds = new HashSet<int>();
        #endregion

        private readonly List<string> _gcRoomCache = new List<string>();
        private readonly List<string> _gcSessionCache = new List<string>();
        private bool _isDisposed;

        public ServerApp(INetworkTransport transport, INetSerializer serializer, NetConfig config)
        {
            _transport = transport;
            _serializer = serializer;
            Config = config;
            CurrentRealtimeSinceStartup = 0f;
            CurrentUtcNow = DateTime.UtcNow;
        }

        public void UpdateRuntimeContext(float realtimeSinceStartup, DateTime utcNow)
        {
            CurrentRealtimeSinceStartup = realtimeSinceStartup;
            CurrentUtcNow = utcNow;
        }

        public void AttachRoomScheduler(ServerRoomScheduler scheduler)
        {
            RoomScheduler = scheduler;
        }

        public void FlushOutboundPackets()
        {
            _outboundDispatcher.Drain(_transport);
        }

        public RoomRuntimeSnapshot[] CaptureRoomRuntimeSnapshots()
        {
            var snapshots = new RoomRuntimeSnapshot[_rooms.Count];
            int index = 0;
            foreach (KeyValuePair<string, Room> kvp in _rooms)
            {
                Room room = kvp.Value;
                if (room == null)
                {
                    continue;
                }

                snapshots[index++] = room.GetRuntimeSnapshot();
            }

            if (index != snapshots.Length)
            {
                Array.Resize(ref snapshots, index);
            }

            return snapshots;
        }

        public RoomDetailedSnapshot CaptureRoomDetailedSnapshot(string roomId)
        {
            Room room = GetRoom(roomId);
            return room != null ? room.CaptureDetailedSnapshot() : null;
        }

        public (int WorkerId, int Rooms, int Pending, float AvgTickMs, float LoadScore)[] CaptureRoomWorkerStats()
        {
            return RoomScheduler != null
                ? RoomScheduler.CaptureWorkerStats()
                : Array.Empty<(int WorkerId, int Rooms, int Pending, float AvgTickMs, float LoadScore)>();
        }

        #region 生命周期与 GC

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            NetLogger.LogWarning("ServerApp", "执行 ServerApp 深度销毁与资源回收");
            foreach (KeyValuePair<string, Room> kvp in _rooms)
            {
                kvp.Value.Destroy();
            }
            _rooms.Clear();
            _sessions.Clear();
            _connectionToSession.Clear();
            _accountToSession.Clear();
            _globalModules.Clear();
            GlobalDispatcher.Clear();
            RoomScheduler = null;
        }

        public void Tick()
        {
            if (_isDisposed) return;
            if (Config == null)
            {
                NetLogger.LogError("ServerApp", "Tick 失败: Config 为空");
                return;
            }

            _gcRoomCache.Clear();
            _gcSessionCache.Clear();
            DateTime now = CurrentUtcNow == default ? DateTime.UtcNow : CurrentUtcNow;

            foreach (var kvp in _rooms)
            {
                Room room = kvp.Value;
                if (room == null) continue;
                RoomRuntimeSnapshot snapshot = room.GetRuntimeSnapshot();
                if (snapshot == null)
                {
                    continue;
                }

                if ((now - snapshot.CreateTimeUtc).TotalHours >= Config.MaxRoomLifetimeHours)
                {
                    _gcRoomCache.Add(room.RoomId);
                    continue;
                }

                if (snapshot.MemberCount == 0 && snapshot.EmptySinceUtc != DateTime.MaxValue &&
                    (now - snapshot.EmptySinceUtc).TotalMinutes >= Config.EmptyRoomTimeoutMinutes)
                {
                    _gcRoomCache.Add(room.RoomId);
                }
            }

            for (int i = 0; i < _gcRoomCache.Count; i++)
            {
                NetLogger.LogWarning("ServerApp", $"触发房间 GC: RoomId:{_gcRoomCache[i]}");
                DestroyRoom(_gcRoomCache[i]);
            }

            foreach (var kvp in _sessions)
            {
                Session session = kvp.Value;
                if (session == null || session.IsOnline) continue;

                double offlineMinutes = (now - session.LastOfflineTime).TotalMinutes;
                bool inRoom = !string.IsNullOrEmpty(session.CurrentRoomId);
                float timeoutThreshold = inRoom ? Config.OfflineTimeoutRoomMinutes : Config.OfflineTimeoutLobbyMinutes;

                if (offlineMinutes >= timeoutThreshold)
                {
                    _gcSessionCache.Add(session.SessionId);
                }
            }

            for (int i = 0; i < _gcSessionCache.Count; i++)
            {
                string sessionId = _gcSessionCache[i];
                if (!_sessions.TryGetValue(sessionId, out Session session) || session == null) continue;

                if (!string.IsNullOrEmpty(session.CurrentRoomId))
                {
                    Room room = GetRoom(session.CurrentRoomId);
                    room?.RemoveMember(session);
                }
                RemoveSession(sessionId);
                NetLogger.LogWarning("ServerApp", $"触发 Session GC: SessionId:{sessionId}");
            }
        }

        #endregion

        #region 网络收发与路由分发

        public void OnReceivePacket(int connectionId, Packet packet)
        {
            if (_isDisposed) return;

            Session session = TryGetSessionByConnectionId(connectionId);
            if (session == null)
            {
                session = new Session(Guid.NewGuid().ToString("N"), "UNAUTH", connectionId, CurrentRealtimeSinceStartup);
                RegisterSession(session);
                NetLogger.LogInfo("ServerApp", "接收到新连接，已分配匿名会话", "-", session.SessionId);
            }

            session.MarkActive(CurrentRealtimeSinceStartup);

            if (packet.Seq > 0 && !session.TryConsumeSeq(packet.Seq))
            {
                NetLogger.LogWarning("ServerApp", $"防重放拦截: MsgId:{packet.MsgId}, Seq:{packet.Seq}", "-", session.SessionId);
                return;
            }

            if (!session.IsAuthenticated)
            {
                if (packet.Scope != NetScope.Global)
                {
                    NetLogger.LogWarning(
                        "ServerApp",
                        $"未鉴权会话禁止访问房间域协议, MsgId:{packet.MsgId}, Scope:{packet.Scope}",
                        packet.RoomId,
                        session.SessionId,
                        $"ConnId:{connectionId}");
                    return;
                }

                if (!_unauthenticatedGlobalMsgIds.Contains(packet.MsgId))
                {
                    NetLogger.LogWarning(
                        "ServerApp",
                        $"未鉴权会话禁止访问全局协议, MsgId:{packet.MsgId}",
                        sessionId: session.SessionId,
                        extraContext: $"ConnId:{connectionId}");
                    return;
                }
            }

            if (packet.Scope == NetScope.Global)
            {
                GlobalDispatcher.Dispatch(session, packet);
                return;
            }

            if (packet.Scope == NetScope.Room)
            {
                // 只有收到房间业务包时才刷新房间活跃时间。
                session.MarkRoomActive(CurrentRealtimeSinceStartup);

                if (string.IsNullOrEmpty(packet.RoomId) || packet.RoomId != session.CurrentRoomId)
                {
                    NetLogger.LogError("ServerApp",
                        $"路由阻断: 房间上下文不匹配, PacketRoom:{packet.RoomId}, SessionRoom:{session.CurrentRoomId}, MsgId:{packet.MsgId}", "-",
                        session.SessionId);
                    return;
                }

                if (!_rooms.TryGetValue(packet.RoomId, out Room room) || room == null)
                {
                    NetLogger.LogError("ServerApp", $"路由阻断: 目标房间不存在, MsgId:{packet.MsgId}", packet.RoomId, session.SessionId);
                    return;
                }

                if (RoomScheduler != null)
                {
                    RoomScheduler.DispatchPacketToRoom(room, session, packet);
                }
                else
                {
                    room.DispatchPacket(session, packet);
                }
            }
        }

        public void SendMessageToSession<T>(Session session, T msg) where T : class
        {
            if (_isDisposed) return;
            if (session == null || msg == null)
            {
                NetLogger.LogError("ServerApp", $"发送失败: session 或 msg 为空, Type:{typeof(T).FullName}", "-", session?.SessionId);
                return;
            }

            if (!session.IsOnline)
            {
                NetLogger.LogError("ServerApp", $"发送失败: 会话离线, Type:{typeof(T).FullName}", "-", session.SessionId);
                return;
            }

            if (!NetMessageMapper.TryGetMeta(typeof(T), out NetMessageMeta meta))
            {
                NetLogger.LogError("ServerApp", $"发送失败: 未找到静态网络元数据, Type:{typeof(T).FullName}", "-", session.SessionId);
                return;
            }

            if (meta.Dir != NetDir.S2C)
            {
                NetLogger.LogError("ServerApp", $"发送阻断: 协议方向非法, MsgId:{meta.Id}, Dir:{meta.Dir}", "-", session.SessionId);
                return;
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(131072);
            try
            {
                int length = _serializer.Serialize(msg, buffer);
                if (length <= 0)
                {
                    NetLogger.LogError("ServerApp", $"发送失败: 序列化结果长度非法, MsgId:{meta.Id}", "-", session.SessionId);
                    return;
                }

                string roomId = meta.Scope == NetScope.Room ? session.CurrentRoomId : string.Empty;
                var packet = new Packet(0, meta.Id, meta.Scope, roomId, buffer, 0, length);
                _transport.SendToClient(session.ConnectionId, packet);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        #endregion

        #region 房间管理

        public Room CreateRoom(string roomId)
        {
            if (_isDisposed || string.IsNullOrEmpty(roomId)) return null;

            if (_rooms.ContainsKey(roomId))
            {
                NetLogger.LogError("ServerApp", $"创建房间失败: RoomId 已存在, RoomId:{roomId}");
                return null;
            }

            var room = new Room(roomId, _outboundDispatcher, _serializer, Config, RoomRecordingService, RoomMembershipNotifier);
            _rooms.Add(roomId, room);
            return room;
        }

        public void RegisterRoomToScheduler(Room room)
        {
            if (_isDisposed || room == null || RoomScheduler == null)
            {
                return;
            }

            RoomScheduler.RegisterRoom(room);
        }

        public void DestroyRoom(string roomId)
        {
            if (_isDisposed || string.IsNullOrEmpty(roomId)) return;

            if (_rooms.TryGetValue(roomId, out Room room) && room != null)
            {
                RoomScheduler?.UnregisterRoom(roomId);
                room.Destroy();
                _rooms.Remove(roomId);
            }
        }

        public Room GetRoom(string roomId)
        {
            if (_isDisposed || string.IsNullOrEmpty(roomId)) return null;
            _rooms.TryGetValue(roomId, out Room room);
            return room;
        }

        public void RegisterUnauthenticatedGlobalProtocol(int msgId)
        {
            if (_isDisposed) return;
            if (msgId <= 0)
            {
                NetLogger.LogError("ServerApp", $"注册未鉴权协议失败: MsgId 非法, MsgId:{msgId}");
                return;
            }

            _unauthenticatedGlobalMsgIds.Add(msgId);
        }

        public void RegisterGlobalModule(object module)
        {
            if (_isDisposed || module == null) return;

            Type moduleType = module.GetType();
            if (_globalModules.ContainsKey(moduleType))
            {
                NetLogger.LogError("ServerApp", $"注册全局模块失败: 模块类型重复, Type:{moduleType.FullName}");
                return;
            }

            _globalModules.Add(moduleType, module);
        }

        public T GetGlobalModule<T>() where T : class
        {
            if (TryGetGlobalModule(out T module))
            {
                return module;
            }

            NetLogger.LogWarning("ServerApp", $"获取全局模块失败: 未找到模块, Type:{typeof(T).FullName}");
            return null;
        }

        public bool TryGetGlobalModule<T>(out T module) where T : class
        {
            module = null;
            if (_isDisposed) return false;

            Type targetType = typeof(T);
            if (_globalModules.TryGetValue(targetType, out object exactModule))
            {
                module = exactModule as T;
                return module != null;
            }

            T matchedModule = null;
            Type matchedType = null;
            foreach (KeyValuePair<Type, object> kvp in _globalModules)
            {
                if (!targetType.IsAssignableFrom(kvp.Key)) continue;
                if (!(kvp.Value is T typedModule)) continue;

                if (matchedModule != null)
                {
                    NetLogger.LogError(
                        "ServerApp",
                        $"获取全局模块失败: 匹配到多个模块, Target:{targetType.FullName}, TypeA:{matchedType.FullName}, TypeB:{kvp.Key.FullName}");
                    module = null;
                    return false;
                }

                matchedModule = typedModule;
                matchedType = kvp.Key;
            }

            module = matchedModule;
            return module != null;
        }

        #endregion

        #region 会话与路由管理 (核心双索引机制)

        public Session GetSessionByAccountId(string accountId)
        {
            if (_isDisposed || string.IsNullOrEmpty(accountId)) return null;
            _accountToSession.TryGetValue(accountId, out Session session);
            return session;
        }

        public void RegisterSession(Session session)
        {
            if (_isDisposed || session == null) return;

            _sessions[session.SessionId] = session;
            if (session.IsOnline)
            {
                _connectionToSession[session.ConnectionId] = session;
            }

            if (!string.IsNullOrEmpty(session.AccountId) && session.AccountId != "UNAUTH")
            {
                _accountToSession[session.AccountId] = session;
            }
        }

        public void RemoveSession(string sessionId)
        {
            if (_isDisposed || string.IsNullOrEmpty(sessionId)) return;

            if (!_sessions.TryGetValue(sessionId, out Session session) || session == null) return;

            _sessions.Remove(sessionId);
            _connectionToSession.Remove(session.ConnectionId);

            if (!string.IsNullOrEmpty(session.AccountId) && session.AccountId != "UNAUTH")
            {
                if (_accountToSession.TryGetValue(session.AccountId, out Session mappedSession) && mappedSession == session)
                {
                    _accountToSession.Remove(session.AccountId);
                }
            }
        }

        public void BindConnection(Session session, int connectionId)
        {
            if (_isDisposed || session == null) return;

            if (_connectionToSession.TryGetValue(connectionId, out Session oldSession) && oldSession != null && oldSession != session)
            {
                NetLogger.LogWarning("ServerApp", $"物理连接顶号: ConnId:{connectionId}, OldSession:{oldSession.SessionId}", "-", session.SessionId);
                oldSession.MarkOffline(CurrentUtcNow);
            }

            _connectionToSession.Remove(session.ConnectionId);
            session.UpdateConnection(connectionId, CurrentRealtimeSinceStartup);

            if (session.IsOnline)
            {
                _connectionToSession[connectionId] = session;
            }

            if (!string.IsNullOrEmpty(session.CurrentRoomId))
            {
                GetRoom(session.CurrentRoomId)?.NotifyMemberOnline(session);
            }
        }

        public void UnbindConnection(Session session)
        {
            if (_isDisposed || session == null || !session.IsOnline) return;

            _connectionToSession.Remove(session.ConnectionId);
            session.MarkOffline(CurrentUtcNow);

            if (!string.IsNullOrEmpty(session.CurrentRoomId))
            {
                GetRoom(session.CurrentRoomId)?.NotifyMemberOffline(session);
            }
        }

        internal Session TryGetSessionByConnectionId(int connectionId)
        {
            if (_isDisposed) return null;
            _connectionToSession.TryGetValue(connectionId, out Session session);
            return session;
        }

        #endregion
    }
}
