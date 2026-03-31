using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Core
{
    /// <summary>
    /// 服务端全局权威内核。
    /// 管理 Session、Room、GlobalDispatcher 和生命周期治理。
    /// </summary>
    public sealed class ServerApp
    {
        // 全局域协议分发器。
        public GlobalDispatcher GlobalDispatcher { get; } = new GlobalDispatcher();
        public IReadOnlyDictionary<string, Room> Rooms => _rooms;
        public IReadOnlyDictionary<string, Session> Sessions => _sessions;
        public NetConfig Config { get; }

        // 传输层和序列化器都通过接口注入，避免绑死底层库。
        private readonly INetworkTransport _transport;
        private readonly INetSerializer _serializer;

        // 会话和连接索引。
        private readonly Dictionary<string, Session> _sessions = new Dictionary<string, Session>();
        private readonly Dictionary<int, Session> _connectionToSession = new Dictionary<int, Session>();

        // 房间索引和 GC 缓存。
        private readonly Dictionary<string, Room> _rooms = new Dictionary<string, Room>();
        private readonly List<string> _gcRoomCache = new List<string>();
        private readonly List<string> _gcSessionCache = new List<string>();
        private bool _isDisposed;

        public ServerApp(INetworkTransport transport, INetSerializer serializer, NetConfig config)
        {
            _transport = transport;
            _serializer = serializer;
            Config = config;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            NetLogger.LogWarning("ServerApp", "执行 ServerApp 深度销毁与资源回收");

            foreach (KeyValuePair<string, Room> kvp in _rooms)
            {
                kvp.Value.Destroy();
            }

            _rooms.Clear();
            _sessions.Clear();
            _connectionToSession.Clear();
            GlobalDispatcher.Clear();
        }

        /// <summary>
        /// 驱动服务端逻辑帧
        /// 优化：采用零分配遍历模式，消除 ToList() 产生的 GC 压力
        /// </summary>
        public void Tick()
        {
            if (_isDisposed) return;
            if (Config == null)
            {
                NetLogger.LogError("ServerApp", "Tick 失败: Config 为空");
                return;
            }

            // 先复用缓存列表，避免 Tick 期间产生额外 GC。
            _gcRoomCache.Clear();
            _gcSessionCache.Clear();

            DateTime now = DateTime.UtcNow;

            // 1. 推进所有房间，并检查寿命/空房回收条件。
            foreach (var kvp in _rooms)
            {
                Room room = kvp.Value;
                if (room == null) continue;

                room.Tick();

                // 超过最大寿命直接回收。
                if ((now - room.CreateTime).TotalHours >= Config.MaxRoomLifetimeHours)
                {
                    _gcRoomCache.Add(room.RoomId);
                    continue;
                }

                // 空房超时也会被自动清理。
                if (room.MemberCount == 0 && (now - room.EmptySince).TotalMinutes >= Config.EmptyRoomTimeoutMinutes)
                {
                    _gcRoomCache.Add(room.RoomId);
                }
            }

            // 统一执行房间清理，避免遍历时改字典。
            for (int i = 0; i < _gcRoomCache.Count; i++)
            {
                NetLogger.LogWarning("ServerApp", $"触发房间 GC: RoomId:{_gcRoomCache[i]}");
                DestroyRoom(_gcRoomCache[i]);
            }

            // 2. 检查离线 Session 是否超时。
            foreach (var kvp in _sessions)
            {
                Session session = kvp.Value;
                if (session == null || session.IsOnline) continue;

                double offlineMinutes = (now - session.LastOfflineTime).TotalMinutes;
                bool inRoom = !string.IsNullOrEmpty(session.CurrentRoomId);

                // 在房间内和大厅使用不同的离线超时阈值。
                float timeoutThreshold = inRoom ? Config.OfflineTimeoutRoomMinutes : Config.OfflineTimeoutLobbyMinutes;
                if (offlineMinutes >= timeoutThreshold)
                {
                    _gcSessionCache.Add(session.SessionId);
                }
            }

            // 统一执行 Session 清理。
            for (int i = 0; i < _gcSessionCache.Count; i++)
            {
                string sessionId = _gcSessionCache[i];
                if (!_sessions.TryGetValue(sessionId, out Session session) || session == null) continue;

                // 删除 Session 前先安全退出房间上下文。
                if (!string.IsNullOrEmpty(session.CurrentRoomId))
                {
                    Room room = GetRoom(session.CurrentRoomId);
                    room?.RemoveMember(session);
                }

                RemoveSession(sessionId);
                NetLogger.LogWarning("ServerApp", $"触发 Session GC: SessionId:{sessionId}");
            }
        }

        public void OnReceivePacket(int connectionId, Packet packet)
        {
            if (_isDisposed)
            {
                return;
            }

            // 未鉴权连接先分配匿名 Session，后续登录时再升级成正式会话。
            Session session = TryGetSessionByConnectionId(connectionId);
            if (session == null)
            {
                session = new Session(Guid.NewGuid().ToString("N"), "UNAUTH", connectionId);
                RegisterSession(session);
                NetLogger.LogInfo("ServerApp", "接收到新连接，已分配匿名会话", "-", session.SessionId);
            }

            // 所有客户端包都先经过 Seq 防重放。
            if (packet.Seq > 0 && !session.TryConsumeSeq(packet.Seq))
            {
                NetLogger.LogWarning("ServerApp", $"防重放拦截: MsgId:{packet.MsgId}, Seq:{packet.Seq}", "-", session.SessionId);
                return;
            }

            // Global 和 Room 两个作用域走不同分发链。
            if (packet.Scope == NetScope.Global)
            {
                GlobalDispatcher.Dispatch(session, packet);
                return;
            }

            if (packet.Scope == NetScope.Room)
            {
                // 房间包必须和 Session 当前房间严格匹配。
                if (string.IsNullOrEmpty(packet.RoomId) || packet.RoomId != session.CurrentRoomId)
                {
                    NetLogger.LogError(
                        "ServerApp",
                        $"路由阻断: 房间上下文不匹配, PacketRoom:{packet.RoomId}, SessionRoom:{session.CurrentRoomId}, MsgId:{packet.MsgId}",
                        "-",
                        session.SessionId);
                    return;
                }

                if (!_rooms.TryGetValue(packet.RoomId, out Room room) || room == null)
                {
                    NetLogger.LogError("ServerApp", $"路由阻断: 目标房间不存在, MsgId:{packet.MsgId}", packet.RoomId, session.SessionId);
                    return;
                }

                room.Dispatcher.Dispatch(session, packet);
            }
        }

        public void SendMessageToSession<T>(Session session, T msg) where T : class
        {
            if (_isDisposed)
            {
                return;
            }

            if (session == null || msg == null)
            {
                NetLogger.LogError("ServerApp", $"发送失败: session 或 msg 为空, Type:{typeof(T).FullName}", "-", session?.SessionId);
                return;
            }

            if (_serializer == null)
            {
                NetLogger.LogError("ServerApp", $"发送失败: _serializer 为空, Type:{typeof(T).FullName}", "-", session.SessionId);
                return;
            }

            if (_transport == null)
            {
                NetLogger.LogError("ServerApp", $"发送失败: _transport 为空, Type:{typeof(T).FullName}", "-", session.SessionId);
                return;
            }

            if (!session.IsOnline)
            {
                NetLogger.LogError("ServerApp", $"发送失败: 会话离线, Type:{typeof(T).FullName}", "-", session.SessionId);
                return;
            }

            // 发送前强校验：类型必须存在静态元数据，且方向必须是 S2C。
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
                // 发送时只借共享 buffer，不保留长期引用。
                int length = _serializer.Serialize(msg, buffer);
                if (length <= 0)
                {
                    NetLogger.LogError("ServerApp", $"发送失败: 序列化结果长度非法, MsgId:{meta.Id}, Type:{typeof(T).FullName}, Length:{length}", "-", session.SessionId);
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

        public Room CreateRoom(string roomId)
        {
            if (_isDisposed)
            {
                return null;
            }

            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("ServerApp", "创建房间失败: roomId 为空");
                return null;
            }

            if (_rooms.ContainsKey(roomId))
            {
                NetLogger.LogError("ServerApp", $"创建房间失败: RoomId 已存在, RoomId:{roomId}");
                return null;
            }

            var room = new Room(roomId, _transport, _serializer, Config);
            _rooms.Add(roomId, room);
            return room;
        }

        public void DestroyRoom(string roomId)
        {
            if (_isDisposed)
            {
                return;
            }

            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("ServerApp", "销毁房间失败: roomId 为空");
                return;
            }

            if (!_rooms.TryGetValue(roomId, out Room room) || room == null)
            {
                return;
            }

            room.Destroy();
            _rooms.Remove(roomId);
        }

        public Room GetRoom(string roomId)
        {
            if (_isDisposed)
            {
                return null;
            }

            if (string.IsNullOrEmpty(roomId))
            {
                return null;
            }

            _rooms.TryGetValue(roomId, out Room room);
            return room;
        }

        public void RegisterSession(Session session)
        {
            if (_isDisposed)
            {
                return;
            }

            if (session == null)
            {
                NetLogger.LogError("ServerApp", "注册会话失败: session 为空");
                return;
            }

            _sessions[session.SessionId] = session;
            if (session.IsOnline)
            {
                _connectionToSession[session.ConnectionId] = session;
            }
        }

        public void RemoveSession(string sessionId)
        {
            if (_isDisposed)
            {
                return;
            }

            if (string.IsNullOrEmpty(sessionId))
            {
                NetLogger.LogError("ServerApp", "移除会话失败: sessionId 为空");
                return;
            }

            if (!_sessions.TryGetValue(sessionId, out Session session) || session == null)
            {
                return;
            }

            _sessions.Remove(sessionId);
            _connectionToSession.Remove(session.ConnectionId);
        }

        public void BindConnection(Session session, int connectionId)
        {
            if (_isDisposed)
            {
                return;
            }

            if (session == null)
            {
                NetLogger.LogError("ServerApp", $"绑定连接失败: session 为空, ConnId:{connectionId}");
                return;
            }

            // 同一物理连接被新会话占用时，旧会话会被标记离线。
            if (_connectionToSession.TryGetValue(connectionId, out Session oldSession) && oldSession != null && oldSession != session)
            {
                NetLogger.LogWarning("ServerApp", $"物理连接顶号: ConnId:{connectionId}, OldSession:{oldSession.SessionId}", "-", session.SessionId);
                oldSession.MarkOffline();
            }

            _connectionToSession.Remove(session.ConnectionId);
            session.UpdateConnection(connectionId);

            if (session.IsOnline)
            {
                _connectionToSession[connectionId] = session;
            }

            // 重连成功后，如果还在房间内，要通知房间做在线恢复。
            if (!string.IsNullOrEmpty(session.CurrentRoomId))
            {
                GetRoom(session.CurrentRoomId)?.NotifyMemberOnline(session);
            }
        }

        public void UnbindConnection(Session session)
        {
            if (_isDisposed)
            {
                return;
            }

            if (session == null)
            {
                NetLogger.LogError("ServerApp", "解绑连接失败: session 为空");
                return;
            }

            if (!session.IsOnline)
            {
                return;
            }

            _connectionToSession.Remove(session.ConnectionId);
            session.MarkOffline();

            if (!string.IsNullOrEmpty(session.CurrentRoomId))
            {
                GetRoom(session.CurrentRoomId)?.NotifyMemberOffline(session);
            }
        }

        internal Session TryGetSessionByConnectionId(int connectionId)
        {
            if (_isDisposed)
            {
                return null;
            }

            _connectionToSession.TryGetValue(connectionId, out Session session);
            return session;
        }
    }
}
