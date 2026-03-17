using System;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Client.Core.Events;
using UnityEngine;

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

        public ClientApp(Action<Packet> networkSender, Func<object, byte[]> serializeFunc)
        {
            NetworkSender = networkSender;
            SerializeFunc = serializeFunc;
        }

        public void OnReceivePacket(Packet packet)
        {
            if (packet.Scope == NetScope.Global)
            {
                GlobalDispatcher.Dispatch(packet);
            }
            else if (packet.Scope == NetScope.Room)
            {
                if (State == ClientAppState.ReplayRoom) return;
                if (State == ClientAppState.ConnectionSuspended) return;

                if (CurrentRoom == null || packet.RoomId != CurrentRoom.RoomId) return;

                CurrentRoom.Dispatcher.Dispatch(packet);
            }
        }

        private bool TryChangeState(ClientAppState targetState)
        {
            if (State == targetState) return true;
            State = targetState;
            return true;
        }

        public void EnterOnlineRoom(string roomId)
        {
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
            if (string.IsNullOrEmpty(roomId)) return;
            if (!TryChangeState(ClientAppState.ReplayRoom)) return;

            CurrentRoom = ClientRoom.Create(roomId);
            if (CurrentRoom == null)
            {
                TryChangeState(ClientAppState.InLobby);
                return;
            }
        }

        public void LeaveRoom()
        {
            if (CurrentRoom != null)
            {
                CurrentRoom.Destroy();
                CurrentRoom = null;
                // 核心修复：房间销毁时同步抛出事件，通知所有 View 解绑
                GlobalTypeNetEvent.Broadcast(new Local_RoomLeft { IsSuspended = false });
            }

            Session.UnbindRoom();
            TryChangeState(ClientAppState.InLobby);
        }

        public void SuspendConnection()
        {
            if (State != ClientAppState.OnlineRoom) return;

            NetLogger.LogWarning("ClientApp", "触发软清理: 销毁当前房间实例，进入挂起态");
            if (CurrentRoom != null)
            {
                CurrentRoom.Destroy();
                CurrentRoom = null;
                // 核心修复：通知 View 层进入挂起态（保留画面定格）
                GlobalTypeNetEvent.Broadcast(new Local_RoomLeft { IsSuspended = true });
            }

            Session.IsPhysicalOnline = false;
            Session.LastDisconnectRealtime = DateTime.UtcNow;
            TryChangeState(ClientAppState.ConnectionSuspended);
        }

        public void AbortConnection()
        {
            NetLogger.LogWarning("ClientApp", "触发硬清理: 彻底清空会话与房间状态");
            if (CurrentRoom != null)
            {
                CurrentRoom.Destroy();
                CurrentRoom = null;
                GlobalTypeNetEvent.Broadcast(new Local_RoomLeft { IsSuspended = false });
            }

            Session.Clear();
            TryChangeState(ClientAppState.InLobby);
        }

        public void SendMessage<T>(T msg) where T : class
        {
            if (msg == null) return;
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