using System;
using System.Buffers;
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

    /// <summary>
    /// 客户端主状态机。
    /// 负责收发包、房间生命周期、回放态和挂起态切换。
    /// </summary>
    public sealed class ClientApp
    {
        // 客户端轻量会话镜像。
        public ClientSession Session { get; } = new ClientSession();
        // 全局域消息分发器。
        public ClientGlobalDispatcher GlobalDispatcher { get; } = new ClientGlobalDispatcher();
        // 当前激活的房间实例，在线态和回放态都会使用。
        public ClientRoom CurrentRoom { get; private set; }
        public ClientAppState State { get; private set; } = ClientAppState.InLobby;

        private readonly INetworkTransport _transport;
        private readonly INetSerializer _serializer;
        private uint _sendSeq;
        private bool _isDisposed;

        public ClientApp(INetworkTransport transport, INetSerializer serializer)
        {
            _transport = transport;
            _serializer = serializer;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            NetLogger.LogWarning("ClientApp", "执行 ClientApp 深度销毁与资源回收");

            // 先广播硬中止，让下载/回放等模块释放外部资源。
            GlobalTypeNetEvent.Broadcast(new Local_ConnectionAborted());

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
            if (_isDisposed)
            {
                return;
            }

            // Global 包不依赖房间上下文，可直接分发。
            if (packet.Scope == NetScope.Global)
            {
                GlobalDispatcher.Dispatch(packet);
                return;
            }

            if (packet.Scope == NetScope.Room)
            {
                // 回放态和挂起态不消费在线房间消息。
                if (State == ClientAppState.ReplayRoom || State == ClientAppState.ConnectionSuspended)
                {
                    return;
                }

                if (CurrentRoom == null)
                {
                    NetLogger.LogWarning("ClientApp",
                        $"房间包已丢弃: CurrentRoom 为空, MsgId:{packet.MsgId}, RoomId:{packet.RoomId}");
                    return;
                }

                // 房间包必须严格命中当前房间。
                if (packet.RoomId != CurrentRoom.RoomId)
                {
                    NetLogger.LogWarning("ClientApp",
                        $"房间包已丢弃: RoomId 不匹配, Packet:{packet.RoomId}, Current:{CurrentRoom.RoomId}, MsgId:{packet.MsgId}");
                    return;
                }

                CurrentRoom.Dispatcher.Dispatch(packet);
            }
        }

        private bool TryChangeState(ClientAppState targetState)
        {
            if (State == targetState)
            {
                return true;
            }

            // 这里显式限制合法跃迁，避免 UI 或模块把状态机推坏。
            bool isValidTransition = false;
            switch (State)
            {
                case ClientAppState.InLobby:
                    isValidTransition = targetState == ClientAppState.OnlineRoom ||
                                        targetState == ClientAppState.ReplayRoom;
                    break;
                case ClientAppState.OnlineRoom:
                    isValidTransition = targetState == ClientAppState.InLobby ||
                                        targetState == ClientAppState.ConnectionSuspended;
                    break;
                case ClientAppState.ReplayRoom:
                    isValidTransition = targetState == ClientAppState.InLobby;
                    break;
                case ClientAppState.ConnectionSuspended:
                    isValidTransition = targetState == ClientAppState.InLobby ||
                                        targetState == ClientAppState.OnlineRoom;
                    break;
            }

            if (!isValidTransition)
            {
                NetLogger.LogError("ClientApp", $"状态机跃迁非法: {State} -> {targetState}");
                return false;
            }

            State = targetState;
            return true;
        }

        public void EnterOnlineRoom(string roomId)
        {
            if (_isDisposed)
            {
                return;
            }

            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("ClientApp", "进入在线房间失败: roomId 为空");
                return;
            }

            // 先创建房间实例，等装配成功后再正式切状态。
            ClientRoom newRoom = ClientRoom.Create(roomId);
            if (newRoom == null)
            {
                NetLogger.LogError("ClientApp", $"进入在线房间失败: ClientRoom.Create 返回 null, RoomId:{roomId}");
                return;
            }

            if (!TryChangeState(ClientAppState.OnlineRoom))
            {
                newRoom.Destroy();
                return;
            }

            if (CurrentRoom != null)
            {
                NetLogger.LogWarning("ClientApp",
                    $"进入在线房间前检测到旧房间残留，已执行覆盖销毁。OldRoom:{CurrentRoom.RoomId}, NewRoom:{roomId}");
                CurrentRoom.Destroy();
                CurrentRoom = null;
            }

            CurrentRoom = newRoom;
            Session.BindRoom(roomId);
        }

        public void EnterReplayRoom(string roomId)
        {
            if (_isDisposed)
            {
                return;
            }

            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("ClientApp", "进入回放房间失败: roomId 为空");
                return;
            }

            // 回放房间与在线房间共用 ClientRoom，但状态严格隔离。
            ClientRoom newRoom = ClientRoom.Create(roomId);
            if (newRoom == null)
            {
                NetLogger.LogError("ClientApp", $"进入回放房间失败: ClientRoom.Create 返回 null, RoomId:{roomId}");
                return;
            }

            if (!TryChangeState(ClientAppState.ReplayRoom))
            {
                newRoom.Destroy();
                return;
            }

            if (CurrentRoom != null)
            {
                NetLogger.LogWarning("ClientApp",
                    $"进入回放房间前检测到旧房间残留，已执行覆盖销毁。OldRoom:{CurrentRoom.RoomId}, NewRoom:{roomId}");
                CurrentRoom.Destroy();
                CurrentRoom = null;
            }

            CurrentRoom = newRoom;
        }

        public void LeaveRoom(bool silent = false)
        {
            if (_isDisposed)
            {
                return;
            }

            // 离房统一销毁房间实例，并抛给 UI 全局事件。
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
            if (_isDisposed)
            {
                return;
            }

            if (State != ClientAppState.OnlineRoom)
            {
                NetLogger.LogWarning("ClientApp", $"软挂起失败: 当前状态非法, State:{State}");
                return;
            }

            // 软挂起会保留恢复上下文，但销毁当前房间实例。
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
            if (_isDisposed)
            {
                return;
            }

            NetLogger.LogWarning("ClientApp", "触发硬清理: 彻底清空会话与房间状态");

            // 硬中止会彻底清掉会话和房间，不保留恢复上下文。
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
            if (_isDisposed)
            {
                return;
            }

            if (msg == null)
            {
                NetLogger.LogError("ClientApp", $"发送失败: msg 为空, Type:{typeof(T).FullName}");
                return;
            }

            if (_serializer == null)
            {
                NetLogger.LogError("ClientApp", $"发送失败: _serializer 为空, Type:{typeof(T).FullName}, State:{State}");
                return;
            }

            if (_transport == null)
            {
                NetLogger.LogError("ClientApp", $"发送失败: _transport 为空, Type:{typeof(T).FullName}, State:{State}");
                return;
            }

            // 发送前先查静态元数据，确保消息方向合法。
            if (!NetMessageMapper.TryGetMeta(typeof(T), out NetMessageMeta meta))
            {
                return;
            }

            if (meta.Dir != NetDir.C2S)
            {
                NetLogger.LogError("ClientApp",
                    $"发送阻断: 协议方向非法, Type:{typeof(T).FullName}, MsgId:{meta.Id}, Dir:{meta.Dir}");
                return;
            }

            // 回放态禁止发业务请求。
            if (State == ClientAppState.ReplayRoom)
            {
                NetLogger.LogWarning("ClientApp", $"发送阻断: 当前处于回放态, MsgId:{meta.Id}, Type:{typeof(T).FullName}");
                return;
            }

            // 挂起态只允许恢复链相关协议通过。
            if (State == ClientAppState.ConnectionSuspended)
            {
                if (meta.Id != MsgIdConst.C2S_Login &&
                    meta.Id != MsgIdConst.C2S_ConfirmReconnect &&
                    meta.Id != MsgIdConst.C2S_ReconnectReady)
                {
                    NetLogger.LogWarning("ClientApp", $"发送阻断: 挂起态下禁止发送该协议, MsgId:{meta.Id}, Type:{typeof(T).FullName}");
                    return;
                }
            }

            string roomId = string.Empty;
            // Room 协议必须带当前房间上下文。
            if (meta.Scope == NetScope.Room)
            {
                if (State != ClientAppState.OnlineRoom || CurrentRoom == null)
                {
                    NetLogger.LogError("ClientApp",
                        $"发送阻断: 房间协议缺失上下文, MsgId:{meta.Id}, State:{State}, CurrentRoomNull:{CurrentRoom == null}");
                    return;
                }

                roomId = CurrentRoom.RoomId;
                if (string.IsNullOrEmpty(roomId))
                {
                    NetLogger.LogError("ClientApp",
                        $"发送阻断: CurrentRoom.RoomId 为空, MsgId:{meta.Id}, Type:{typeof(T).FullName}");
                    return;
                }
            }

            // 每个客户端包都会自增 Seq，供服务端做防重放。
            _sendSeq++;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(131072);
            try
            {
                int length = _serializer.Serialize(msg, buffer);
                if (length <= 0)
                {
                    NetLogger.LogError("ClientApp",
                        $"发送失败: 序列化结果长度非法, MsgId:{meta.Id}, Type:{typeof(T).FullName}, Length:{length}");
                    return;
                }

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
