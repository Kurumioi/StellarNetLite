using System;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Core
{
    /// <summary>
    /// 服务端会话。
    /// </summary>
    public sealed class Session
    {
        private readonly object _gate = new object();
        private int _connectionId;
        private string _currentRoomId;
        private string _authorizedRoomId;
        private string _recoverableRoomId;
        private bool _isOnline;
        private bool _isRoomReady;
        private DateTime _lastOfflineTime;
        private uint _lastReceivedSeq;
        private float _lastActiveRealtime;
        private float _lastRoomActiveRealtime;

        /// <summary>
        /// 当前会话 Id。
        /// </summary>
        public string SessionId { get; }

        /// <summary>
        /// 当前会话绑定的账号 Id。
        /// </summary>
        public string AccountId { get; }

        /// <summary>
        /// 当前物理连接 Id。
        /// </summary>
        public int ConnectionId
        {
            get
            {
                lock (_gate)
                {
                    return _connectionId;
                }
            }
        }

        /// <summary>
        /// 当前正式加入的房间 Id。
        /// </summary>
        public string CurrentRoomId
        {
            get
            {
                lock (_gate)
                {
                    return _currentRoomId;
                }
            }
        }

        /// <summary>
        /// 当前被授权进入的房间 Id。
        /// </summary>
        public string AuthorizedRoomId
        {
            get
            {
                lock (_gate)
                {
                    return _authorizedRoomId;
                }
            }
        }

        /// <summary>
        /// 当前唯一可恢复的房间 Id。
        /// 客户端进入其它房间后，该值会被新房间覆盖或清空。
        /// </summary>
        public string RecoverableRoomId
        {
            get
            {
                lock (_gate)
                {
                    return _recoverableRoomId;
                }
            }
        }

        /// <summary>
        /// 当前物理连接是否在线。
        /// </summary>
        public bool IsOnline
        {
            get
            {
                lock (_gate)
                {
                    return _isOnline;
                }
            }
        }

        /// <summary>
        /// 当前会话是否已完成业务鉴权。
        /// </summary>
        public bool IsAuthenticated => !string.IsNullOrEmpty(AccountId) && AccountId != "UNAUTH";

        /// <summary>
        /// 当前房间数据是否已准备完成。
        /// </summary>
        public bool IsRoomReady
        {
            get
            {
                lock (_gate)
                {
                    return _isRoomReady;
                }
            }
        }

        /// <summary>
        /// 最后一次离线时间。
        /// </summary>
        public DateTime LastOfflineTime
        {
            get
            {
                lock (_gate)
                {
                    return _lastOfflineTime;
                }
            }
        }

        /// <summary>
        /// 最后一次接收并通过校验的序号。
        /// </summary>
        public uint LastReceivedSeq
        {
            get
            {
                lock (_gate)
                {
                    return _lastReceivedSeq;
                }
            }
        }

        /// <summary>
        /// 最后一次收到任意业务包的时间。
        /// </summary>
        public float LastActiveRealtime
        {
            get
            {
                lock (_gate)
                {
                    return _lastActiveRealtime;
                }
            }
        }

        /// <summary>
        /// 最后一次收到房间业务包的时间。
        /// </summary>
        public float LastRoomActiveRealtime
        {
            get
            {
                lock (_gate)
                {
                    return _lastRoomActiveRealtime;
                }
            }
        }

        /// <summary>
        /// 创建一个在线会话。
        /// </summary>
        public Session(string sessionId, string accountId, int connectionId, float initialRealtimeSinceStartup = 0f)
        {
            SessionId = sessionId;
            AccountId = accountId;
            _connectionId = connectionId;
            _isOnline = true;
            _currentRoomId = string.Empty;
            _authorizedRoomId = string.Empty;
            _recoverableRoomId = string.Empty;
            _lastOfflineTime = DateTime.UtcNow;
            _lastReceivedSeq = 0;
            _isRoomReady = false;
            _lastActiveRealtime = initialRealtimeSinceStartup;
            _lastRoomActiveRealtime = initialRealtimeSinceStartup;
        }

        /// <summary>
        /// 绑定新的物理连接并重置在线态。
        /// </summary>
        public void UpdateConnection(int newConnectionId, float currentRealtimeSinceStartup)
        {
            lock (_gate)
            {
                _connectionId = newConnectionId;
                _isOnline = true;
                _lastActiveRealtime = currentRealtimeSinceStartup;
                _lastRoomActiveRealtime = currentRealtimeSinceStartup;
            }
        }

        /// <summary>
        /// 标记当前会话已离线。
        /// </summary>
        public void MarkOffline(DateTime? offlineUtc = null)
        {
            lock (_gate)
            {
                _isOnline = false;
                _lastOfflineTime = offlineUtc ?? DateTime.UtcNow;
                _isRoomReady = false;
            }
        }

        /// <summary>
        /// 刷新任意业务包的活跃时间。
        /// </summary>
        public void MarkActive(float time)
        {
            lock (_gate)
            {
                _lastActiveRealtime = time;
            }
        }

        /// <summary>
        /// 刷新房间业务包的活跃时间。
        /// </summary>
        public void MarkRoomActive(float time)
        {
            lock (_gate)
            {
                _lastRoomActiveRealtime = time;
            }
        }

        /// <summary>
        /// 绑定当前正式所在房间。
        /// </summary>
        public void BindRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("Session", "绑定房间失败: RoomId 为空", sessionId: SessionId);
                return;
            }

            lock (_gate)
            {
                _currentRoomId = roomId;
                _recoverableRoomId = string.Empty;
                _isRoomReady = true;
            }
        }

        /// <summary>
        /// 解除当前正式所在房间。
        /// </summary>
        public void UnbindRoom()
        {
            lock (_gate)
            {
                _currentRoomId = string.Empty;
                _isRoomReady = false;
            }
        }

        /// <summary>
        /// 记录允许进入的目标房间。
        /// </summary>
        public void AuthorizeRoom(string roomId)
        {
            lock (_gate)
            {
                _authorizedRoomId = roomId;
            }
        }

        /// <summary>
        /// 清理进入房间授权。
        /// </summary>
        public void ClearAuthorizedRoom()
        {
            lock (_gate)
            {
                _authorizedRoomId = string.Empty;
            }
        }

        /// <summary>
        /// 标记当前存在一个唯一可恢复房间。
        /// </summary>
        public void SetRecoverableRoom(string roomId)
        {
            lock (_gate)
            {
                _recoverableRoomId = roomId ?? string.Empty;
            }
        }

        /// <summary>
        /// 清理当前可恢复房间。
        /// </summary>
        public void ClearRecoverableRoom()
        {
            lock (_gate)
            {
                _recoverableRoomId = string.Empty;
            }
        }

        /// <summary>
        /// 更新房间准备状态。
        /// </summary>
        public void SetRoomReady(bool ready)
        {
            lock (_gate)
            {
                _isRoomReady = ready;
            }
        }

        /// <summary>
        /// 消费新的消息序号。
        /// </summary>
        public bool TryConsumeSeq(uint seq)
        {
            lock (_gate)
            {
                if (seq <= _lastReceivedSeq)
                {
                    return false;
                }

                _lastReceivedSeq = seq;
                return true;
            }
        }

        /// <summary>
        /// 重置当前消息序号基线。
        /// </summary>
        public void ResetSeq(uint seq)
        {
            lock (_gate)
            {
                _lastReceivedSeq = seq;
            }
        }
    }
}
