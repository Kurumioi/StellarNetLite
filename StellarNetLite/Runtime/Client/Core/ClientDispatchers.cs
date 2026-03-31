using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Core
{
    /// <summary>
    /// 客户端全局域消息分发器。
    /// 用于大厅、登录、回放下载等协议。
    /// </summary>
    public sealed class ClientGlobalDispatcher
    {
        // MsgId -> Handler。
        private readonly Dictionary<int, Action<Packet>> _handlers = new Dictionary<int, Action<Packet>>();

        public void Register(int msgId, Action<Packet> handler)
        {
            if (handler == null)
            {
                NetLogger.LogError("ClientGlobalDispatcher", $"注册失败: handler 为空, MsgId:{msgId}");
                return;
            }

            // 每个 MsgId 只允许注册一个处理器，避免重复触发。
            if (_handlers.ContainsKey(msgId))
            {
                NetLogger.LogError("ClientGlobalDispatcher", $"注册失败: MsgId 重复注册会导致重复分发, MsgId:{msgId}");
                return;
            }

            _handlers.Add(msgId, handler);
        }

        public void Dispatch(Packet packet)
        {
            // 找到 MsgId 对应处理器后立即执行。
            if (_handlers.TryGetValue(packet.MsgId, out Action<Packet> handler))
            {
                handler.Invoke(packet);
                return;
            }

            NetLogger.LogWarning("ClientGlobalDispatcher", $"未找到 MsgId 对应处理函数，消息已忽略。MsgId:{packet.MsgId}");
        }

        public void Clear()
        {
            _handlers.Clear();
        }
    }

    /// <summary>
    /// 客户端房间域消息分发器。
    /// 只处理当前房间上下文下的协议。
    /// </summary>
    public sealed class ClientRoomDispatcher
    {
        // MsgId -> Handler。
        private readonly Dictionary<int, Action<Packet>> _handlers = new Dictionary<int, Action<Packet>>();
        // 当前分发器归属的房间 Id，仅用于日志定位。
        private readonly string _roomId;

        public ClientRoomDispatcher(string roomId)
        {
            _roomId = roomId;
        }

        public void Register(int msgId, Action<Packet> handler)
        {
            if (handler == null)
            {
                NetLogger.LogError("ClientRoomDispatcher", $"注册失败: handler 为空, RoomId:{_roomId}, MsgId:{msgId}");
                return;
            }

            // 每个 MsgId 只允许注册一个处理器，避免重复触发。
            if (_handlers.ContainsKey(msgId))
            {
                NetLogger.LogError("ClientRoomDispatcher", $"注册失败: MsgId 重复注册会导致重复分发, RoomId:{_roomId}, MsgId:{msgId}");
                return;
            }

            _handlers.Add(msgId, handler);
        }

        public void Dispatch(Packet packet)
        {
            // 房间分发器只关心当前房间内的消息处理。
            if (_handlers.TryGetValue(packet.MsgId, out Action<Packet> handler))
            {
                handler.Invoke(packet);
                return;
            }

            NetLogger.LogWarning("ClientRoomDispatcher", $"未找到 MsgId 对应处理函数，消息已忽略。RoomId:{_roomId}, MsgId:{packet.MsgId}");
        }

        public void Clear()
        {
            _handlers.Clear();
        }
    }
}
