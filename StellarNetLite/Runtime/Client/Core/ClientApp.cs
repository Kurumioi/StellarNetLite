using System;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Client.Core.Events;

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

        public Action<Packet> NetworkSender { get; }
        public Func<object, byte[]> SerializeFunc { get; }

        private uint _sendSeq = 0;
        private bool _isDisposed = false;

        public ClientApp(Action<Packet> networkSender, Func<object, byte[]> serializeFunc)
        {
            NetworkSender = networkSender;
            SerializeFunc = serializeFunc;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            NetLogger.LogWarning("ClientApp", "执行 ClientApp 深度销毁与资源回收");

            if (CurrentRoom != null)
            {
                CurrentRoom.Destroy();
                CurrentRoom = null;
            }

            Session.Clear();
            GlobalDispatcher.Clear();
        }

        public void OnReceivePacket(Packet packet)
        {
            if (_isDisposed) return;

            if (packet.Scope == NetScope.Global)
            {
                GlobalDispatcher.Dispatch(packet);
            }
            else if (packet.Scope == NetScope.Room)
            {
                if (State == ClientAppState.ReplayRoom || State == ClientAppState.ConnectionSuspended) return;
                if (CurrentRoom == null || packet.RoomId != CurrentRoom.RoomId) return;

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
                    isValidTransition = (targetState == ClientAppState.OnlineRoom || targetState == ClientAppState.ReplayRoom);
                    break;
                case ClientAppState.OnlineRoom:
                    isValidTransition = (targetState == ClientAppState.InLobby || targetState == ClientAppState.ConnectionSuspended);
                    break;
                case ClientAppState.ReplayRoom:
                    isValidTransition = (targetState == ClientAppState.InLobby);
                    break;
                case ClientAppState.ConnectionSuspended:
                    isValidTransition = (targetState == ClientAppState.InLobby || targetState == ClientAppState.OnlineRoom);
                    break;
            }

            if (!isValidTransition)
            {
                NetLogger.LogError("ClientApp", $"状态机跃迁非法: 拒绝从 {State} 切换到 {targetState}");
                return false;
            }

            State = targetState;
            return true;
        }

        public void EnterOnlineRoom(string roomId)
        {
            if (_isDisposed) return;
            if (string.IsNullOrEmpty(roomId)) return;
            if (!TryChangeState(ClientAppState.OnlineRoom)) return;

            CurrentRoom = ClientRoom.Create(roomId);
            if (CurrentRoom == null)
            {
                TryChangeState(ClientAppState.InLobby);
                return;
            }

            Session.BindRoom(roomId);
        }

        public void EnterReplayRoom(string roomId)
        {
            if (_isDisposed) return;
            if (string.IsNullOrEmpty(roomId)) return;
            if (!TryChangeState(ClientAppState.ReplayRoom)) return;

            CurrentRoom = ClientRoom.Create(roomId);
            if (CurrentRoom == null)
            {
                TryChangeState(ClientAppState.InLobby);
                return;
            }
        }

        // 核心修复：引入 silent 参数，支持静默销毁房间而不触发全局 UI 跳转
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

            NetLogger.LogWarning("ClientApp", "触发软清理: 销毁当前房间实例，进入挂起态");
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

            NetLogger.LogWarning("ClientApp", "触发硬清理: 彻底清空会话与房间状态");
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
            if (_isDisposed || msg == null) return;

            if (!NetMessageMapper.TryGetMeta(typeof(T), out var meta)) return;

            if (meta.Dir != NetDir.C2S) return;

            if (State == ClientAppState.ReplayRoom) return;

            if (State == ClientAppState.ConnectionSuspended)
            {
                if (meta.Id != MsgIdConst.C2S_Login &&
                    meta.Id != MsgIdConst.C2S_ConfirmReconnect &&
                    meta.Id != MsgIdConst.C2S_ReconnectReady)
                {
                    return;
                }
            }

            if (meta.Scope == NetScope.Room && (State != ClientAppState.OnlineRoom || CurrentRoom == null)) return;

            _sendSeq++;
            byte[] payload = SerializeFunc(msg);
            string roomId = meta.Scope == NetScope.Room ? CurrentRoom.RoomId : string.Empty;
            var packet = new Packet(_sendSeq, meta.Id, meta.Scope, roomId, payload);

            NetworkSender?.Invoke(packet);
        }
    }
}