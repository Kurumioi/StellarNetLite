using System;
using System.Buffers;
using System.Collections.Generic;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Core
{
    /// <summary>
    /// 客户端运行状态。
    /// </summary>
    public enum ClientAppState
    {
        InLobby,
        OnlineRoom,
        ReplayRoom,
        ConnectionSuspended
    }

    /// <summary>
    /// 客户端逻辑宿主。
    /// 负责状态流转、消息发送和当前房间生命周期管理。
    /// </summary>
    public sealed class ClientApp
    {
        /// <summary>
        /// 当前客户端会话。
        /// </summary>
        public ClientSession Session { get; } = new ClientSession();

        /// <summary>
        /// 当前全局消息分发器。
        /// </summary>
        public ClientGlobalDispatcher GlobalDispatcher { get; } = new ClientGlobalDispatcher();

        /// <summary>
        /// 当前在线房间或回放房间。
        /// </summary>
        public ClientRoom CurrentRoom { get; private set; }

        /// <summary>
        /// 当前客户端逻辑状态。
        /// </summary>
        public ClientAppState State { get; private set; } = ClientAppState.InLobby;

        /// <summary>
        /// 是否进入弱网发送阻断态。
        /// </summary>
        public bool IsNetworkBlocked { get; set; }

        /// <summary>
        /// 当前在线房间是否已经收到最终确认。
        /// </summary>
        public bool IsCurrentRoomConfirmed => _isCurrentRoomConfirmed;

        private readonly INetworkTransport _transport;
        private readonly INetSerializer _serializer;
        private readonly List<Action> _disposeCallbacks = new List<Action>();
        private uint _sendSeq;
        private bool _isDisposed;
        private bool _isCurrentRoomConfirmed;

        // 使用注册表维护弱网豁免消息，避免在底层写死协议 Id。
        private readonly HashSet<int> _weakNetBypassMsgIds = new HashSet<int>();

        public ClientApp(INetworkTransport transport, INetSerializer serializer)
        {
            _transport = transport;
            _serializer = serializer;
        }

        public void RegisterWeakNetBypassProtocol(int msgId)
        {
            _weakNetBypassMsgIds.Add(msgId);
            NetLogger.LogInfo("ClientApp", $"注册弱网豁免协议成功。MsgId:{msgId}");
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            NetLogger.LogWarning("ClientApp", "执行 ClientApp 深度销毁与资源回收");
            GlobalTypeNetEvent.Broadcast(new Local_ConnectionAborted());
            ExecuteDisposeCallbacks();
            if (CurrentRoom != null)
            {
                CurrentRoom.Destroy();
                CurrentRoom = null;
            }

            _isCurrentRoomConfirmed = false;
            Session.Clear();
            GlobalDispatcher.Clear();
            _weakNetBypassMsgIds.Clear();
            NetLogger.LogInfo("ClientApp", "ClientApp 资源回收完成");
        }

        public void RegisterDisposeCallback(Action callback)
        {
            if (_isDisposed || callback == null)
            {
                return;
            }

            _disposeCallbacks.Add(callback);
        }

        public bool ConfirmCurrentRoom(string roomId)
        {
            if (_isDisposed ||
                State != ClientAppState.OnlineRoom ||
                CurrentRoom == null ||
                string.IsNullOrEmpty(roomId) ||
                CurrentRoom.RoomId != roomId)
            {
                return false;
            }

            _isCurrentRoomConfirmed = true;
            return true;
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

            if (!isValidTransition)
            {
                NetLogger.LogWarning("ClientApp", $"状态切换失败: {State} -> {targetState} 非法");
                return false;
            }

            ClientAppState oldState = State;
            State = targetState;
            NetLogger.LogInfo("ClientApp", $"状态切换成功: {oldState} -> {targetState}");
            return true;
        }

        public void EnterOnlineRoom(string roomId)
        {
            if (_isDisposed)
            {
                NetLogger.LogError("ClientApp", $"进入在线房间失败: ClientApp 已销毁, RoomId:{roomId}");
                return;
            }

            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("ClientApp", "进入在线房间失败: roomId 为空");
                return;
            }

            ClientRoom newRoom = ClientRoom.Create(roomId);
            if (newRoom == null)
            {
                NetLogger.LogError("ClientApp", $"进入在线房间失败: ClientRoom 创建失败, RoomId:{roomId}");
                return;
            }

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
            _isCurrentRoomConfirmed = false;
            Session.BindRoom(roomId);
            NetLogger.LogInfo("ClientApp", $"已进入在线房间。RoomId:{roomId}");
        }

        public void EnterReplayRoom(string roomId)
        {
            if (_isDisposed)
            {
                NetLogger.LogError("ClientApp", $"进入回放房间失败: ClientApp 已销毁, RoomId:{roomId}");
                return;
            }

            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("ClientApp", "进入回放房间失败: roomId 为空");
                return;
            }

            ClientRoom newRoom = ClientRoom.Create(roomId);
            if (newRoom == null)
            {
                NetLogger.LogError("ClientApp", $"进入回放房间失败: ClientRoom 创建失败, RoomId:{roomId}");
                return;
            }

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
            _isCurrentRoomConfirmed = true;
            NetLogger.LogInfo("ClientApp", $"已进入回放房间。RoomId:{roomId}");
        }

        public void LeaveRoom(bool silent = false)
        {
            if (_isDisposed)
            {
                NetLogger.LogWarning("ClientApp", $"离开房间跳过: ClientApp 已销毁, Silent:{silent}");
                return;
            }

            string roomId = CurrentRoom != null ? CurrentRoom.RoomId : Session.CurrentRoomId;
            if (CurrentRoom != null)
            {
                CurrentRoom.Destroy();
                CurrentRoom = null;
                GlobalTypeNetEvent.Broadcast(new Local_RoomLeft { IsSuspended = false, IsSilent = silent });
            }

            _isCurrentRoomConfirmed = false;
            Session.UnbindRoom();
            TryChangeState(ClientAppState.InLobby);
            NetLogger.LogInfo("ClientApp", $"已离开房间。RoomId:{roomId}, Silent:{silent}");
        }

        public void SuspendConnection()
        {
            if (_isDisposed)
            {
                NetLogger.LogWarning("ClientApp", "挂起连接跳过: ClientApp 已销毁");
                return;
            }

            if (State != ClientAppState.OnlineRoom)
            {
                NetLogger.LogWarning("ClientApp", $"挂起连接跳过: 当前状态非法, State:{State}");
                return;
            }

            if (CurrentRoom != null)
            {
                CurrentRoom.Destroy();
                CurrentRoom = null;
                GlobalTypeNetEvent.Broadcast(new Local_RoomLeft { IsSuspended = true, IsSilent = false });
            }

            _isCurrentRoomConfirmed = false;
            Session.IsPhysicalOnline = false;
            Session.LastDisconnectRealtime = DateTime.UtcNow;
            TryChangeState(ClientAppState.ConnectionSuspended);
            NetLogger.LogWarning("ClientApp", $"连接已挂起，等待恢复。RoomId:{Session.CurrentRoomId}");
        }

        public void AbortConnection()
        {
            if (_isDisposed)
            {
                NetLogger.LogWarning("ClientApp", "中止连接跳过: ClientApp 已销毁");
                return;
            }

            GlobalTypeNetEvent.Broadcast(new Local_ConnectionAborted());
            if (CurrentRoom != null)
            {
                CurrentRoom.Destroy();
                CurrentRoom = null;
                GlobalTypeNetEvent.Broadcast(new Local_RoomLeft { IsSuspended = false, IsSilent = false });
            }

            _isCurrentRoomConfirmed = false;
            Session.Clear();
            TryChangeState(ClientAppState.InLobby);
            NetLogger.LogWarning("ClientApp", "连接已执行硬中止并回到大厅态");
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
                if (State != ClientAppState.OnlineRoom || CurrentRoom == null || !_isCurrentRoomConfirmed) return;
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

        private void ExecuteDisposeCallbacks()
        {
            for (int i = 0; i < _disposeCallbacks.Count; i++)
            {
                Action callback = _disposeCallbacks[i];
                if (callback == null)
                {
                    continue;
                }

                try
                {
                    callback();
                }
                catch (Exception ex)
                {
                    NetLogger.LogError("ClientApp", $"执行销毁回调失败: {ex.Message}");
                }
            }

            _disposeCallbacks.Clear();
        }
    }
}