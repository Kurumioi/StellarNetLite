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
        /// <summary>
        /// 当前处于大厅态。
        /// </summary>
        InLobby,

        /// <summary>
        /// 当前处于正式在线房间态。
        /// </summary>
        OnlineRoom,

        /// <summary>
        /// 当前处于本地沙盒或回放房间态。
        /// </summary>
        SandboxRoom,

        /// <summary>
        /// 当前物理连接已断开，正在等待恢复。
        /// </summary>
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
        /// 当前在线房间或沙盒房间。
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

        /// <summary>
        /// 底层网络传输实例。
        /// </summary>
        private readonly INetworkTransport _transport;

        /// <summary>
        /// 协议序列化器。
        /// </summary>
        private readonly INetSerializer _serializer;

        /// <summary>
        /// 销毁时要执行的回调列表。
        /// </summary>
        private readonly List<Action> _disposeCallbacks = new List<Action>();

        /// <summary>
        /// 发送序号。
        /// </summary>
        private uint _sendSeq;

        /// <summary>
        /// 当前 App 是否已销毁。
        /// </summary>
        private bool _isDisposed;

        /// <summary>
        /// 当前在线房间是否已收到最终进房确认。
        /// </summary>
        private bool _isCurrentRoomConfirmed;

        /// <summary>
        /// 弱网阻断时仍允许发送的协议 Id 集合。
        /// </summary>
        private readonly HashSet<int> _weakNetBypassMsgIds = new HashSet<int>();

        /// <summary>
        /// 创建客户端逻辑宿主。
        /// </summary>
        public ClientApp(INetworkTransport transport, INetSerializer serializer)
        {
            _transport = transport;
            _serializer = serializer;
        }

        /// <summary>
        /// 注册一个弱网阻断时仍可发送的协议 Id。
        /// </summary>
        public void RegisterWeakNetBypassProtocol(int msgId)
        {
            _weakNetBypassMsgIds.Add(msgId);
            NetLogger.LogInfo("ClientApp", $"注册弱网豁免协议成功。MsgId:{msgId}");
        }

        /// <summary>
        /// 按协议类型注册弱网豁免协议。
        /// </summary>
        public bool RegisterWeakNetBypassProtocol<T>() where T : class
        {
            if (!NetMessageMapper.TryGetMeta(typeof(T), out NetMessageMeta meta))
            {
                NetLogger.LogError("ClientApp", $"注册弱网豁免协议失败: 未找到静态网络元数据, Type:{typeof(T).FullName}");
                return false;
            }

            if (meta.Dir != NetDir.C2S)
            {
                NetLogger.LogError("ClientApp", $"注册弱网豁免协议失败: 仅允许 C2S 协议, Type:{typeof(T).FullName}, Dir:{meta.Dir}");
                return false;
            }

            RegisterWeakNetBypassProtocol(meta.Id);
            return true;
        }

        /// <summary>
        /// 销毁客户端逻辑宿主并回收全部资源。
        /// </summary>
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

        /// <summary>
        /// 注册一个随 App 销毁执行的回调。
        /// </summary>
        public void RegisterDisposeCallback(Action callback)
        {
            if (_isDisposed || callback == null)
            {
                return;
            }

            _disposeCallbacks.Add(callback);
        }

        /// <summary>
        /// 标记当前在线房间已经收到最终确认。
        /// </summary>
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

        /// <summary>
        /// 处理收到的网络包并按作用域分发。
        /// </summary>
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
                if (State == ClientAppState.SandboxRoom || State == ClientAppState.ConnectionSuspended) return;
                if (CurrentRoom == null) return;
                if (packet.RoomId != CurrentRoom.RoomId) return;
                CurrentRoom.Dispatcher.Dispatch(packet);
            }
        }

        /// <summary>
        /// 尝试切换客户端逻辑状态。
        /// </summary>
        private bool TryChangeState(ClientAppState targetState)
        {
            if (State == targetState) return true;
            bool isValidTransition = false;
            switch (State)
            {
                case ClientAppState.InLobby:
                    isValidTransition = targetState == ClientAppState.OnlineRoom || targetState == ClientAppState.SandboxRoom;
                    break;
                case ClientAppState.OnlineRoom:
                    isValidTransition = targetState == ClientAppState.InLobby || targetState == ClientAppState.ConnectionSuspended;
                    break;
                case ClientAppState.SandboxRoom:
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

        /// <summary>
        /// 进入正式在线房间态。
        /// </summary>
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

        /// <summary>
        /// 进入本地沙盒房间态。
        /// </summary>
        public void EnterSandboxRoom(string roomId)
        {
            if (_isDisposed)
            {
                NetLogger.LogError("ClientApp", $"进入沙盒房间失败: ClientApp 已销毁, RoomId:{roomId}");
                return;
            }

            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("ClientApp", "进入沙盒房间失败: roomId 为空");
                return;
            }

            ClientRoom newRoom = ClientRoom.Create(roomId);
            if (newRoom == null)
            {
                NetLogger.LogError("ClientApp", $"进入沙盒房间失败: ClientRoom 创建失败, RoomId:{roomId}");
                return;
            }

            if (!TryChangeState(ClientAppState.SandboxRoom))
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
            NetLogger.LogInfo("ClientApp", $"已进入沙盒房间。RoomId:{roomId}");
        }

        /// <summary>
        /// 仅做客户端本地房间上下文清理。
        /// 是否已经通知服务端，由调用方决定。
        /// 例如：
        /// - 正式离房结果回包后调用
        /// - 挂起房间结果回包后调用
        /// </summary>
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
            }

            _isCurrentRoomConfirmed = false;
            Session.UnbindRoom();
            TryChangeState(ClientAppState.InLobby);
            NetLogger.LogInfo("ClientApp", $"已离开房间。RoomId:{roomId}, Silent:{silent}");
        }

        /// <summary>
        /// 挂起当前连接，保留会话恢复上下文。
        /// </summary>
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
            }

            _isCurrentRoomConfirmed = false;
            Session.IsPhysicalOnline = false;
            Session.LastDisconnectRealtime = DateTime.UtcNow;
            TryChangeState(ClientAppState.ConnectionSuspended);
            NetLogger.LogWarning("ClientApp", $"连接已挂起，等待恢复。RoomId:{Session.CurrentRoomId}");
        }

        /// <summary>
        /// 中止当前连接并清理全部会话上下文。
        /// </summary>
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
            }

            _isCurrentRoomConfirmed = false;
            Session.Clear();
            TryChangeState(ClientAppState.InLobby);
            NetLogger.LogWarning("ClientApp", "连接已执行硬中止并回到大厅态");
        }

        /// <summary>
        /// 发送一条客户端到服务端协议消息。
        /// </summary>
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

            if (State == ClientAppState.SandboxRoom) return;
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

        /// <summary>
        /// 依次执行并清空全部销毁回调。
        /// </summary>
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
