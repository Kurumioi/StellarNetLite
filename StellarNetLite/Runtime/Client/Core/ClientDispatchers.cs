using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Core
{
    public sealed class ClientGlobalDispatcher
    {
        private readonly Dictionary<int, Action<Packet>> _handlers = new Dictionary<int, Action<Packet>>();

        public void Register(int msgId, Action<Packet> handler)
        {
            if (handler == null)
            {
                NetLogger.LogError("ClientGlobalDispatcher", $"注册失败: handler 为空, MsgId:{msgId}");
                return;
            }

            if (_handlers.ContainsKey(msgId))
            {
                NetLogger.LogError("ClientGlobalDispatcher", $"注册失败: MsgId 重复注册会导致重复分发, MsgId:{msgId}");
                return;
            }

            _handlers.Add(msgId, handler);
        }

        public void Dispatch(Packet packet)
        {
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

    public sealed class ClientRoomDispatcher
    {
        private readonly Dictionary<int, Action<Packet>> _handlers = new Dictionary<int, Action<Packet>>();
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

            if (_handlers.ContainsKey(msgId))
            {
                NetLogger.LogError("ClientRoomDispatcher", $"注册失败: MsgId 重复注册会导致重复分发, RoomId:{_roomId}, MsgId:{msgId}");
                return;
            }

            _handlers.Add(msgId, handler);
        }

        public void Dispatch(Packet packet)
        {
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