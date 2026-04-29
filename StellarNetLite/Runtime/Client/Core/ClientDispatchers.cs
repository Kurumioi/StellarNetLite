using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Core
{
    /// <summary>
    /// 客户端全局域消息分发器。
    /// </summary>
    public sealed class ClientGlobalDispatcher
    {
        /// <summary>
        /// 全局域协议处理器表。
        /// </summary>
        private readonly Dictionary<int, Action<Packet>> _handlers = new Dictionary<int, Action<Packet>>();

        /// <summary>
        /// 注册全局消息处理器。
        /// </summary>
        public void Register(int msgId, Action<Packet> handler)
        {
            if (handler == null)
            {
                NetLogger.LogError("ClientGlobalDispatcher", $"注册失败: Handler 为空, MsgId:{msgId}");
                return;
            }

            if (_handlers.ContainsKey(msgId))
            {
                NetLogger.LogError("ClientGlobalDispatcher", $"注册失败: MsgId 重复, MsgId:{msgId}");
                return;
            }

            _handlers.Add(msgId, handler);
        }

        /// <summary>
        /// 分发一条全局域消息。
        /// </summary>
        public void Dispatch(Packet packet)
        {
            if (_handlers.TryGetValue(packet.MsgId, out Action<Packet> handler))
            {
                handler.Invoke(packet);
                return;
            }

            NetLogger.LogWarning("ClientGlobalDispatcher", $"分发跳过: 未找到处理器, MsgId:{packet.MsgId}");
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
    /// 客户端房间域消息分发器。
    /// </summary>
    public sealed class ClientRoomDispatcher
    {
        /// <summary>
        /// 房间域协议处理器表。
        /// </summary>
        private readonly Dictionary<int, Action<Packet>> _handlers = new Dictionary<int, Action<Packet>>();

        /// <summary>
        /// 当前分发器归属的房间 Id。
        /// </summary>
        private readonly string _roomId;

        /// <summary>
        /// 创建一个房间消息分发器。
        /// </summary>
        public ClientRoomDispatcher(string roomId)
        {
            _roomId = roomId;
        }

        /// <summary>
        /// 注册房间消息处理器。
        /// </summary>
        public void Register(int msgId, Action<Packet> handler)
        {
            if (handler == null)
            {
                NetLogger.LogError("ClientRoomDispatcher", $"注册失败: Handler 为空, MsgId:{msgId}", roomId: _roomId);
                return;
            }

            if (_handlers.ContainsKey(msgId))
            {
                NetLogger.LogError("ClientRoomDispatcher", $"注册失败: MsgId 重复, MsgId:{msgId}", roomId: _roomId);
                return;
            }

            _handlers.Add(msgId, handler);
        }

        /// <summary>
        /// 分发一条房间域消息。
        /// </summary>
        public void Dispatch(Packet packet)
        {
            if (_handlers.TryGetValue(packet.MsgId, out Action<Packet> handler))
            {
                handler.Invoke(packet);
                return;
            }

            NetLogger.LogWarning("ClientRoomDispatcher", $"分发跳过: 未找到处理器, MsgId:{packet.MsgId}", roomId: _roomId);
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
