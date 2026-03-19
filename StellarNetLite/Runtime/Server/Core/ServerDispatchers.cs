using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Core
{
    public sealed class GlobalDispatcher
    {
        private readonly Dictionary<int, Action<Session, Packet>> _handlers =
            new Dictionary<int, Action<Session, Packet>>();

        public void Register(int msgId, Action<Session, Packet> handler)
        {
            if (handler == null)
            {
                NetLogger.LogError("GlobalDispatcher", $"注册失败: handler 为空, MsgId:{msgId}");
                return;
            }

            if (_handlers.ContainsKey(msgId))
            {
                NetLogger.LogError("GlobalDispatcher", $"注册失败: MsgId 重复注册会导致重复分发, MsgId:{msgId}");
                return;
            }

            _handlers.Add(msgId, handler);
        }

        public void Dispatch(Session session, Packet packet)
        {
            if (session == null)
            {
                NetLogger.LogError("GlobalDispatcher", $"分发失败: session 为空, MsgId:{packet.MsgId}");
                return;
            }

            if (_handlers.TryGetValue(packet.MsgId, out Action<Session, Packet> handler))
            {
                handler.Invoke(session, packet);
                return;
            }

            NetLogger.LogWarning("GlobalDispatcher", $"未找到 MsgId 对应处理函数，消息已忽略。MsgId:{packet.MsgId}, SessionId:{session.SessionId}");
        }

        public void Clear()
        {
            _handlers.Clear();
        }
    }

    public sealed class RoomDispatcher
    {
        private readonly Dictionary<int, Action<Session, Packet>> _handlers =
            new Dictionary<int, Action<Session, Packet>>();

        private readonly string _roomId;

        public RoomDispatcher(string roomId)
        {
            _roomId = roomId;
        }

        public void Register(int msgId, Action<Session, Packet> handler)
        {
            if (handler == null)
            {
                NetLogger.LogError("RoomDispatcher", $"注册失败: handler 为空, RoomId:{_roomId}, MsgId:{msgId}");
                return;
            }

            if (_handlers.ContainsKey(msgId))
            {
                NetLogger.LogError("RoomDispatcher", $"注册失败: MsgId 重复注册会导致重复分发, RoomId:{_roomId}, MsgId:{msgId}");
                return;
            }

            _handlers.Add(msgId, handler);
        }

        public void Dispatch(Session session, Packet packet)
        {
            if (session == null)
            {
                NetLogger.LogError("RoomDispatcher", $"分发失败: session 为空, RoomId:{_roomId}, MsgId:{packet.MsgId}");
                return;
            }

            if (_handlers.TryGetValue(packet.MsgId, out Action<Session, Packet> handler))
            {
                handler.Invoke(session, packet);
                return;
            }

            NetLogger.LogWarning("RoomDispatcher", $"未找到 MsgId 对应处理函数，消息已忽略。RoomId:{_roomId}, MsgId:{packet.MsgId}, SessionId:{session.SessionId}");
        }

        public void Clear()
        {
            _handlers.Clear();
        }
    }
}