using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Core
{
    /// <summary>
    /// 服务端全局域消息分发器。
    /// </summary>
    public sealed class GlobalDispatcher
    {
        /// <summary>
        /// 全局域协议处理器表。
        /// </summary>
        private readonly Dictionary<int, Action<Session, Packet>> _handlers =
            new Dictionary<int, Action<Session, Packet>>();

        /// <summary>
        /// 注册全局消息处理器。
        /// </summary>
        public void Register(int msgId, Action<Session, Packet> handler)
        {
            if (handler == null)
            {
                NetLogger.LogError("GlobalDispatcher", $"注册失败: Handler 为空, MsgId:{msgId}");
                return;
            }

            if (_handlers.ContainsKey(msgId))
            {
                NetLogger.LogError("GlobalDispatcher", $"注册失败: MsgId 重复, MsgId:{msgId}");
                return;
            }

            _handlers.Add(msgId, handler);
        }

        /// <summary>
        /// 分发一条全局域消息。
        /// </summary>
        public void Dispatch(Session session, Packet packet)
        {
            if (session == null)
            {
                NetLogger.LogError("GlobalDispatcher", $"分发失败: Session 为空, MsgId:{packet.MsgId}");
                return;
            }

            if (_handlers.TryGetValue(packet.MsgId, out Action<Session, Packet> handler))
            {
                handler.Invoke(session, packet);
                return;
            }

            NetLogger.LogWarning("GlobalDispatcher", $"分发跳过: 未找到处理器, MsgId:{packet.MsgId}", sessionId: session.SessionId);
        }

        /// <summary>
        /// 清空所有已注册处理器。
        /// </summary>
        public void Clear()
        {
            _handlers.Clear();
        }
    }

    /// <summary>
    /// 服务端房间域消息分发器。
    /// </summary>
    public sealed class RoomDispatcher
    {
        /// <summary>
        /// 房间域协议处理器表。
        /// </summary>
        private readonly Dictionary<int, Action<Session, Packet>> _handlers =
            new Dictionary<int, Action<Session, Packet>>();

        /// <summary>
        /// 当前分发器归属的房间 Id。
        /// </summary>
        private readonly string _roomId;

        /// <summary>
        /// 创建一个房间消息分发器。
        /// </summary>
        public RoomDispatcher(string roomId)
        {
            _roomId = roomId;
        }

        /// <summary>
        /// 注册房间消息处理器。
        /// </summary>
        public void Register(int msgId, Action<Session, Packet> handler)
        {
            if (handler == null)
            {
                NetLogger.LogError("RoomDispatcher", $"注册失败: Handler 为空, MsgId:{msgId}", roomId: _roomId);
                return;
            }

            if (_handlers.ContainsKey(msgId))
            {
                NetLogger.LogError("RoomDispatcher", $"注册失败: MsgId 重复, MsgId:{msgId}", roomId: _roomId);
                return;
            }

            _handlers.Add(msgId, handler);
        }

        /// <summary>
        /// 分发一条房间域消息。
        /// </summary>
        public void Dispatch(Session session, Packet packet)
        {
            if (session == null)
            {
                NetLogger.LogError("RoomDispatcher", $"分发失败: Session 为空, MsgId:{packet.MsgId}", roomId: _roomId);
                return;
            }

            if (_handlers.TryGetValue(packet.MsgId, out Action<Session, Packet> handler))
            {
                handler.Invoke(session, packet);
                return;
            }

            NetLogger.LogWarning("RoomDispatcher", $"分发跳过: 未找到处理器, MsgId:{packet.MsgId}", _roomId, session.SessionId);
        }

        /// <summary>
        /// 清空所有已注册处理器。
        /// </summary>
        public void Clear()
        {
            _handlers.Clear();
        }
    }
}
