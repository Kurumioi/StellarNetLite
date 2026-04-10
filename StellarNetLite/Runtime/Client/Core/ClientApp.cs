using System;
using System.Buffers;
using System.Collections.Generic;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Client.Core
{
    public enum ClientAppState
    {
        InLobby,
        OnlineRoom,
        ReplayRoom,
        ConnectionSuspended
    }

    public sealed class ClientApp
    {
        public ClientSession Session { get; } = new ClientSession();
        public ClientGlobalDispatcher GlobalDispatcher { get; } = new ClientGlobalDispatcher();
        public ClientRoom CurrentRoom { get; private set; }
        public ClientAppState State { get; private set; } = ClientAppState.InLobby;
        
        public bool IsNetworkBlocked { get; set; }

        private readonly INetworkTransport _transport;
        private readonly INetSerializer _serializer;
        private uint _sendSeq;
        private bool _isDisposed;

        // 遵循开闭原则：使用注册机制替代硬编码的豁免白名单
        private readonly HashSet<int> _weakNetBypassMsgIds = new HashSet<int>();

        public ClientApp(INetworkTransport transport, INetSerializer serializer)
        {
            _transport = transport;
            _serializer = serializer;
        }

        public void RegisterWeakNetBypassProtocol(int msgId)
        {
            _weakNetBypassMsgIds.Add(msgId);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            NetLogger.LogWarning("ClientApp", "执行 ClientApp 深度销毁与资源回收");
            GlobalTypeNetEvent.Broadcast(new Local_ConnectionAborted());
            if (CurrentRoom != null)
            {
                CurrentRoom.Destroy();
                CurrentRoom = null;
            }
            Session.Clear();
            GlobalDispatcher.Clear();
            _weakNetBypassMsgIds.Clear();
        }

        public void OnReceivePacket(Packet packet)
        {
            if (_isDisposed) return;
            if (packet.Scope == NetScope.Global)
            {
                GlobalDispatcher.Dispatch(packet);
                return;
            }
            if (packet.Scope == NetScope.Room)
            {
                if (State == ClientAppState.ReplayRoom || State == ClientAppState.ConnectionSuspended) return;
                if (CurrentRoom == null) return;
                if (packet.RoomId != CurrentRoom.RoomId) return;
                CurrentRoom.Dispatcher.Dispatch(packet);
            }
        }

        private bool TryChangeState(ClientAppState targetState)
        {
            if (State == targetState) return true;
            bool isValidTransition = false;
            switch (State)
            {
                case ClientAppState.InLobby:
                    isValidTransition = targetState == ClientAppState.OnlineRoom || targetState == ClientAppState.ReplayRoom;
                    break;
                case ClientAppState.OnlineRoom:
                    isValidTransition = targetState == ClientAppState.InLobby || targetState == ClientAppState.ConnectionSuspended;
                    break;
                case ClientAppState.ReplayRoom:
                    isValidTransition = targetState == ClientAppState.InLobby;
                    break;
                case ClientAppState.ConnectionSuspended:
                    isValidTransition = targetState == ClientAppState.InLobby || targetState == ClientAppState.OnlineRoom;
                    break;
            }
            if (!isValidTransition) return false;
            State = targetState;
            return true;
        }

        public void EnterOnlineRoom(string roomId)
        {
            if (_isDisposed || string.IsNullOrEmpty(roomId)) return;
            ClientRoom newRoom = ClientRoom.Create(roomId);
            if (newRoom == null) return;
            if (!TryChangeState(ClientAppState.OnlineRoom))
            {
                newRoom.Destroy();
                return;
            }
            if (CurrentRoom != null)
            {
                CurrentRoom.Destroy();
                CurrentRoom = null;
            }
            CurrentRoom = newRoom;
            Session.BindRoom(roomId);
        }

        public void EnterReplayRoom(string roomId)
        {
            if (_isDisposed || string.IsNullOrEmpty(roomId)) return;
            ClientRoom newRoom = ClientRoom.Create(roomId);
            if (newRoom == null) return;
            if (!TryChangeState(ClientAppState.ReplayRoom))
            {
                newRoom.Destroy();
                return;
            }
            if (CurrentRoom != null)
            {
                CurrentRoom.Destroy();
                CurrentRoom = null;
            }
            CurrentRoom = newRoom;
        }

        public void LeaveRoom(bool silent = false)
        {
            if (_isDisposed) return;
            if (CurrentRoom != null)
            {
                CurrentRoom.Destroy();
                CurrentRoom = null;
                GlobalTypeNetEvent.Broadcast(new Local_RoomLeft { IsSuspended = false, IsSilent = silent });
            }
            Session.UnbindRoom();
            TryChangeState(ClientAppState.InLobby);
        }

        public void SuspendConnection()
        {
            if (_isDisposed || State != ClientAppState.OnlineRoom) return;
            if (CurrentRoom != null)
            {
                CurrentRoom.Destroy();
                CurrentRoom = null;
                GlobalTypeNetEvent.Broadcast(new Local_RoomLeft { IsSuspended = true, IsSilent = false });
            }
            Session.IsPhysicalOnline = false;
            Session.LastDisconnectRealtime = DateTime.UtcNow;
            TryChangeState(ClientAppState.ConnectionSuspended);
        }

        public void AbortConnection()
        {
            if (_isDisposed) return;
            GlobalTypeNetEvent.Broadcast(new Local_ConnectionAborted());
            if (CurrentRoom != null)
            {
                CurrentRoom.Destroy();
                CurrentRoom = null;
                GlobalTypeNetEvent.Broadcast(new Local_RoomLeft { IsSuspended = false, IsSilent = false });
            }
            Session.Clear();
            TryChangeState(ClientAppState.InLobby);
        }

        public void SendMessage<T>(T msg) where T : class
        {
            if (_isDisposed || msg == null || _serializer == null || _transport == null) return;
            if (!NetMessageMapper.TryGetMeta(typeof(T), out NetMessageMeta meta)) return;
            if (meta.Dir != NetDir.C2S) return;

            // 底层拦截：仅查阅注册表，彻底剥离业务协议硬编码
            if (IsNetworkBlocked && !_weakNetBypassMsgIds.Contains(meta.Id))
            {
                return;
            }

            if (State == ClientAppState.ReplayRoom) return;
            if (State == ClientAppState.ConnectionSuspended && !_weakNetBypassMsgIds.Contains(meta.Id))
            {
                return;
            }

            string roomId = string.Empty;
            if (meta.Scope == NetScope.Room)
            {
                if (State != ClientAppState.OnlineRoom || CurrentRoom == null) return;
                roomId = CurrentRoom.RoomId;
                if (string.IsNullOrEmpty(roomId)) return;
            }

            _sendSeq++;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(131072);
            try
            {
                int length = _serializer.Serialize(msg, buffer);
                if (length <= 0) return;
                var packet = new Packet(_sendSeq, meta.Id, meta.Scope, roomId, buffer, 0, length);
                _transport.SendToServer(packet);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
