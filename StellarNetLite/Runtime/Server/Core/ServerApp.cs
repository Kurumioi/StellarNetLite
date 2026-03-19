using System;
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

        private readonly Dictionary<string, Session> _sessions = new Dictionary<string, Session>();
        private readonly Dictionary<int, Session> _connectionToSession = new Dictionary<int, Session>();
        private readonly Dictionary<string, Room> _rooms = new Dictionary<string, Room>();

        private readonly List<string> _gcRoomCache = new List<string>();
        private readonly List<string> _gcSessionCache = new List<string>();

        private bool _isDisposed = false;

        public ServerApp(INetworkTransport transport, INetSerializer serializer, NetConfig config)
        {
            _transport = transport;
            _serializer = serializer;
            Config = config;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            NetLogger.LogWarning("ServerApp", "执行 ServerApp 深度销毁与资源回收");

            foreach (var kvp in _rooms)
            {
                kvp.Value.Destroy();
            }

            _rooms.Clear();
            _sessions.Clear();
            _connectionToSession.Clear();
            GlobalDispatcher.Clear();
        }

        public void Tick()
        {
            if (_isDisposed || Config == null) return;

            _gcRoomCache.Clear();
            _gcSessionCache.Clear();
            DateTime now = DateTime.UtcNow;

            var activeRooms = _rooms.Values.ToList();
            for (int i = 0; i < activeRooms.Count; i++)
            {
                var room = activeRooms[i];
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
                NetLogger.LogWarning("ServerApp", $"触发房间 GC: 销毁房间 {_gcRoomCache[i]}");
                DestroyRoom(_gcRoomCache[i]);
            }

            var activeSessions = _sessions.Values.ToList();
            for (int i = 0; i < activeSessions.Count; i++)
            {
                var session = activeSessions[i];
                if (!session.IsOnline)
                {
                    double offlineMinutes = (now - session.LastOfflineTime).TotalMinutes;
                    bool inRoom = !string.IsNullOrEmpty(session.CurrentRoomId);

                    if (inRoom && offlineMinutes >= Config.OfflineTimeoutRoomMinutes)
                    {
                        _gcSessionCache.Add(session.SessionId);
                    }
                    else if (!inRoom && offlineMinutes >= Config.OfflineTimeoutLobbyMinutes)
                    {
                        _gcSessionCache.Add(session.SessionId);
                    }
                }
            }

            for (int i = 0; i < _gcSessionCache.Count; i++)
            {
                string sessionId = _gcSessionCache[i];
                if (_sessions.TryGetValue(sessionId, out var session))
                {
                    if (!string.IsNullOrEmpty(session.CurrentRoomId))
                    {
                        var room = GetRoom(session.CurrentRoomId);
                        if (room != null) room.RemoveMember(session);
                    }

                    RemoveSession(sessionId);
                    NetLogger.LogWarning("ServerApp", $"触发 Session GC: 回收离线会话 {sessionId}");
                }
            }
        }

        public void OnReceivePacket(int connectionId, Packet packet)
        {
            if (_isDisposed) return;

            Session session = TryGetSessionByConnectionId(connectionId);
            if (session == null)
            {
                session = new Session(Guid.NewGuid().ToString("N"), "UNAUTH", connectionId);
                RegisterSession(session);
                NetLogger.LogInfo("ServerApp", $"接收到新连接，已分配匿名会话", "-", session.SessionId);
            }

            if (packet.Seq > 0 && !session.TryConsumeSeq(packet.Seq))
            {
                NetLogger.LogWarning("ServerApp", $"防重放拦截: 丢弃重复包 MsgId {packet.MsgId}, Seq {packet.Seq}", "-", session.SessionId);
                return;
            }

            if (packet.Scope == NetScope.Global)
            {
                GlobalDispatcher.Dispatch(session, packet);
            }
            else if (packet.Scope == NetScope.Room)
            {
                if (string.IsNullOrEmpty(packet.RoomId) || packet.RoomId != session.CurrentRoomId)
                {
                    NetLogger.LogError("ServerApp", $"路由阻断: 房间上下文不匹配。Packet: {packet.RoomId}, Session: {session.CurrentRoomId}", "-", session.SessionId);
                    return;
                }

                if (!_rooms.TryGetValue(packet.RoomId, out var room))
                {
                    NetLogger.LogError("ServerApp", $"路由阻断: 目标房间不存在", packet.RoomId, session.SessionId);
                    return;
                }

                room.Dispatcher.Dispatch(session, packet);
            }
        }

        public void SendMessageToSession<T>(Session session, T msg) where T : class
        {
            if (_isDisposed) return;
            if (session == null || !session.IsOnline || msg == null)
            {
                NetLogger.LogError("ServerApp", "发送失败: 参数存在空值或离线", "-", session?.SessionId);
                return;
            }

            if (!NetMessageMapper.TryGetMeta(typeof(T), out var meta))
            {
                NetLogger.LogError("ServerApp", $"发送失败: 未找到类型 {typeof(T).Name} 的网络元数据", "-", session.SessionId);
                return;
            }

            if (meta.Dir != NetDir.S2C)
            {
                NetLogger.LogError("ServerApp", $"发送阻断: 协议 {meta.Id} 的方向为 {meta.Dir}，服务端只能发送 S2C", "-", session.SessionId);
                return;
            }

            // 核心修复：将 Buffer 提升至 128KB，完美容纳 64KB 的录像分块数据
            byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(131072);
            try
            {
                int length = _serializer.Serialize(msg, buffer);
                string roomId = meta.Scope == NetScope.Room ? session.CurrentRoomId : string.Empty;
                var packet = new Packet(0, meta.Id, meta.Scope, roomId, buffer, length);
                _transport?.SendToClient(session.ConnectionId, packet);
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public Room CreateRoom(string roomId)
        {
            if (_isDisposed || string.IsNullOrEmpty(roomId)) return null;
            if (_rooms.ContainsKey(roomId)) return null;

            var room = new Room(roomId, _transport, _serializer, Config);
            _rooms[roomId] = room;
            return room;
        }

        public void DestroyRoom(string roomId)
        {
            if (_isDisposed || string.IsNullOrEmpty(roomId)) return;
            if (_rooms.TryGetValue(roomId, out var room))
            {
                room.Destroy();
                _rooms.Remove(roomId);
            }
        }

        public Room GetRoom(string roomId)
        {
            if (_isDisposed || string.IsNullOrEmpty(roomId)) return null;
            _rooms.TryGetValue(roomId, out var room);
            return room;
        }

        public void RegisterSession(Session session)
        {
            if (_isDisposed || session == null) return;
            _sessions[session.SessionId] = session;
            if (session.IsOnline)
            {
                _connectionToSession[session.ConnectionId] = session;
            }
        }

        public void RemoveSession(string sessionId)
        {
            if (_isDisposed || string.IsNullOrEmpty(sessionId)) return;
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                _sessions.Remove(sessionId);
                _connectionToSession.Remove(session.ConnectionId);
            }
        }

        public void BindConnection(Session session, int connectionId)
        {
            if (_isDisposed || session == null) return;

            if (_connectionToSession.TryGetValue(connectionId, out var oldSession) && oldSession != session)
            {
                NetLogger.LogWarning("ServerApp", $"物理连接顶号: ConnId {connectionId} 原属 {oldSession.SessionId}，现被抢占", "-", session.SessionId);
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
            if (_isDisposed || session == null) return;
            if (session.IsOnline)
            {
                _connectionToSession.Remove(session.ConnectionId);
                session.MarkOffline();

                if (!string.IsNullOrEmpty(session.CurrentRoomId))
                {
                    GetRoom(session.CurrentRoomId)?.NotifyMemberOffline(session);
                }
            }
        }

        internal Session TryGetSessionByConnectionId(int connectionId)
        {
            if (_isDisposed) return null;
            _connectionToSession.TryGetValue(connectionId, out var session);
            return session;
        }
    }
}