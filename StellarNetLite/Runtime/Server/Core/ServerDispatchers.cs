using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Core
{
    /// <summary>
    /// 服务端全局域消息分发器。
    /// 用于登录、大厅、建房等不依赖房间上下文的协议。
    /// </summary>
    public sealed class GlobalDispatcher
    {
        // MsgId -> Handler。
        private readonly Dictionary<int, Action<Session, Packet>> _handlers =
            new Dictionary<int, Action<Session, Packet>>();

        public void Register(int msgId, Action<Session, Packet> handler)
        {
            if (handler == null)
            {
                NetLogger.LogError("GlobalDispatcher", $"注册失败: handler 为空, MsgId:{msgId}");
                return;
            }

            // 每个 MsgId 只允许注册一个处理器，避免重复触发。
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

            // 分发时同时携带 Session 和原始 Packet。
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

    /// <summary>
    /// 服务端房间域消息分发器。
    /// 仅处理已经通过房间上下文校验的协议。
    /// </summary>
    public sealed class RoomDispatcher
    {
        // MsgId -> Handler。
        private readonly Dictionary<int, Action<Session, Packet>> _handlers =
            new Dictionary<int, Action<Session, Packet>>();

        // 当前分发器归属的房间 Id，仅用于日志定位。
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

            // 每个 MsgId 只允许注册一个处理器，避免重复触发。
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

            // 房间分发器只关心当前房间内的消息处理。
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
