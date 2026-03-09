using System;
using StellarNet.Lite.Shared.Core;
using UnityEngine;

namespace StellarNet.Lite.Client.Core
{
    public enum ClientAppState
    {
        Idle,
        OnlineRoom,
        ReplayRoom
    }

    public sealed class ClientApp
    {
        public ClientSession Session { get; } = new ClientSession();
        public ClientGlobalDispatcher GlobalDispatcher { get; } = new ClientGlobalDispatcher();
        public ClientRoom CurrentRoom { get; private set; }
        public ClientAppState State { get; private set; } = ClientAppState.Idle;

        private readonly Action<Packet> _networkSender;
        private readonly Func<object, byte[]> _serializeFunc;
        private uint _sendSeq = 0;

        public ClientApp(Action<Packet> networkSender, Func<object, byte[]> serializeFunc)
        {
            _networkSender = networkSender;
            _serializeFunc = serializeFunc;
        }

        public void OnReceivePacket(Packet packet)
        {
            if (packet.Scope == NetScope.Global)
            {
                GlobalDispatcher.Dispatch(packet);
            }
            else if (packet.Scope == NetScope.Room)
            {
                if (State == ClientAppState.ReplayRoom)
                {
                    Debug.LogWarning($"[ClientApp] 拦截: 回放模式下禁止接收真实网络房间包, MsgId: {packet.MsgId}");
                    return;
                }

                if (CurrentRoom == null)
                {
                    Debug.LogError($"[ClientApp] 路由阻断: 当前不在任何房间中，却收到 Room 消息，MsgId: {packet.MsgId}");
                    return;
                }

                if (packet.RoomId != CurrentRoom.RoomId)
                {
                    Debug.LogError(
                        $"[ClientApp] 路由阻断: 房间上下文不匹配。Packet.RoomId: {packet.RoomId}, CurrentRoom.RoomId: {CurrentRoom.RoomId}");
                    return;
                }

                CurrentRoom.Dispatcher.Dispatch(packet);
            }
        }

        public void EnterOnlineRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return;

            if (State != ClientAppState.Idle)
            {
                Debug.LogError($"[ClientApp] 进入在线房间失败: 当前状态为 {State}，必须先退出");
                return;
            }

            CurrentRoom = ClientRoom.Create(roomId);
            if (CurrentRoom == null) return;

            Session.BindRoom(roomId);
            State = ClientAppState.OnlineRoom;
        }

        public void EnterReplayRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return;

            if (State != ClientAppState.Idle)
            {
                Debug.LogError($"[ClientApp] 进入回放房间失败: 当前状态为 {State}，必须先退出");
                return;
            }

            CurrentRoom = ClientRoom.Create(roomId);
            if (CurrentRoom == null) return;

            State = ClientAppState.ReplayRoom;
        }

        public void LeaveRoom()
        {
            if (CurrentRoom != null)
            {
                CurrentRoom.Destroy();
                CurrentRoom = null;
            }

            Session.UnbindRoom();
            State = ClientAppState.Idle;
        }

        /// <summary>
        /// 强类型统一发送器 (推荐使用)。
        /// 架构意图：业务层仅需传入协议对象，底层自动解析元数据并注入 Seq/RoomId，彻底隔离底层路由字段。
        /// </summary>
        public void SendMessage<T>(T msg) where T : class
        {
            if (msg == null)
            {
                Debug.LogError("[ClientApp] 发送失败: 消息对象为空");
                return;
            }

            if (!NetMessageMapper.TryGetMeta(typeof(T), out var meta))
            {
                Debug.LogError($"[ClientApp] 发送失败: 未找到类型 {typeof(T).Name} 的网络元数据，请检查是否添加了 [NetMsg] 特性");
                return;
            }

            if (State == ClientAppState.ReplayRoom)
            {
                Debug.LogWarning($"[ClientApp] 拦截: 回放模式下禁止发送网络包，已自动丢弃协议 {meta.Id}");
                return;
            }

            if (meta.Scope == NetScope.Room && (State != ClientAppState.OnlineRoom || CurrentRoom == null))
            {
                Debug.LogError($"[ClientApp] 发送失败: 协议 {meta.Id} 作用域为 Room，但当前不在在线房间中");
                return;
            }

            _sendSeq++;
            byte[] payload = _serializeFunc(msg);
            string roomId = meta.Scope == NetScope.Room ? CurrentRoom.RoomId : string.Empty;

            var packet = new Packet(_sendSeq, meta.Id, meta.Scope, roomId, payload);
            _networkSender?.Invoke(packet);
        }
    }
}