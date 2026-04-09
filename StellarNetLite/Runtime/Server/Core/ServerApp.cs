using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Core
{
    public sealed class ServerApp
    {
        public GlobalDispatcher GlobalDispatcher { get; } = new GlobalDispatcher();
        public IReadOnlyDictionary<string, Room> Rooms => _rooms;
        public IReadOnlyDictionary<string, Session> Sessions => _sessions;
        public NetConfig Config { get; }

        private readonly INetworkTransport _transport;
        private readonly INetSerializer _serializer;

        #region 核心状态字典 (路由索引)

        private readonly Dictionary<string, Session> _sessions = new Dictionary<string, Session>();
        private readonly Dictionary<int, Session> _connectionToSession = new Dictionary<int, Session>();
        private readonly Dictionary<string, Session> _accountToSession = new Dictionary<string, Session>();
        private readonly Dictionary<string, Room> _rooms = new Dictionary<string, Room>();

        #endregion

        private readonly List<string> _gcRoomCache = new List<string>();
        private readonly List<string> _gcSessionCache = new List<string>();
        private bool _isDisposed;

        public ServerApp(INetworkTransport transport, INetSerializer serializer, NetConfig config)
        {
            _transport = transport;
            _serializer = serializer;
            Config = config;
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
            GlobalDispatcher.Clear();
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
            DateTime now = DateTime.UtcNow;

            foreach (var kvp in _rooms)
            {
                Room room = kvp.Value;
                if (room == null) continue;

                room.Tick();

                if ((now - room.CreateTime).TotalHours >= Config.MaxRoomLifetimeHours)
                {
                    _gcRoomCache.Add(room.RoomId);
                    continue;
                }

                if (room.MemberCount == 0 && (now - room.EmptySince).TotalMinutes >= Config.EmptyRoomTimeoutMinutes)
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
                session = new Session(Guid.NewGuid().ToString("N"), "UNAUTH", connectionId);
                RegisterSession(session);
                NetLogger.LogInfo("ServerApp", "接收到新连接，已分配匿名会话", "-", session.SessionId);
            }

            if (packet.Seq > 0 && !session.TryConsumeSeq(packet.Seq))
            {
                NetLogger.LogWarning("ServerApp", $"防重放拦截: MsgId:{packet.MsgId}, Seq:{packet.Seq}", "-", session.SessionId);
                return;
            }

            if (packet.Scope == NetScope.Global)
            {
                GlobalDispatcher.Dispatch(session, packet);
                return;
            }

            if (packet.Scope == NetScope.Room)
            {
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

                room.Dispatcher.Dispatch(session, packet);
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

            var room = new Room(roomId, _transport, _serializer, Config);
            _rooms.Add(roomId, room);
            return room;
        }

        public void DestroyRoom(string roomId)
        {
            if (_isDisposed || string.IsNullOrEmpty(roomId)) return;

            if (_rooms.TryGetValue(roomId, out Room room) && room != null)
            {
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
                oldSession.MarkOffline();
            }

            _connectionToSession.Remove(session.ConnectionId);
            session.UpdateConnection(connectionId);

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
            session.MarkOffline();

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