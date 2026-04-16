using System;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Core
{
    /// <summary>
    /// 服务端会话。
    /// </summary>
    public sealed class Session
    {
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
        public int ConnectionId { get; private set; }

        /// <summary>
        /// 当前正式加入的房间 Id。
        /// </summary>
        public string CurrentRoomId { get; private set; }

        /// <summary>
        /// 当前被授权进入的房间 Id。
        /// </summary>
        public string AuthorizedRoomId { get; private set; }

        /// <summary>
        /// 当前物理连接是否在线。
        /// </summary>
        public bool IsOnline { get; private set; }

        /// <summary>
        /// 当前会话是否已完成业务鉴权。
        /// </summary>
        public bool IsAuthenticated => !string.IsNullOrEmpty(AccountId) && AccountId != "UNAUTH";

        /// <summary>
        /// 当前房间数据是否已准备完成。
        /// </summary>
        public bool IsRoomReady { get; private set; }

        /// <summary>
        /// 最后一次离线时间。
        /// </summary>
        public DateTime LastOfflineTime { get; private set; }

        /// <summary>
        /// 最后一次接收并通过校验的序号。
        /// </summary>
        public uint LastReceivedSeq { get; private set; }

        /// <summary>
        /// 最后一次收到任意业务包的时间。
        /// </summary>
        public float LastActiveRealtime { get; private set; }

        /// <summary>
        /// 最后一次收到房间业务包的时间。
        /// </summary>
        public float LastRoomActiveRealtime { get; private set; }

        /// <summary>
        /// 创建一个在线会话。
        /// </summary>
        public Session(string sessionId, string accountId, int connectionId, float initialRealtimeSinceStartup = 0f)
        {
            SessionId = sessionId;
            AccountId = accountId;
            ConnectionId = connectionId;
            IsOnline = true;
            CurrentRoomId = string.Empty;
            AuthorizedRoomId = string.Empty;
            LastOfflineTime = DateTime.UtcNow;
            LastReceivedSeq = 0;
            IsRoomReady = false;
            LastActiveRealtime = initialRealtimeSinceStartup;
            LastRoomActiveRealtime = initialRealtimeSinceStartup;
        }

        /// <summary>
        /// 绑定新的物理连接并重置在线态。
        /// </summary>
        public void UpdateConnection(int newConnectionId, float currentRealtimeSinceStartup)
        {
            ConnectionId = newConnectionId;
            IsOnline = true;
            LastActiveRealtime = currentRealtimeSinceStartup;
            LastRoomActiveRealtime = currentRealtimeSinceStartup;
        }

        /// <summary>
        /// 标记当前会话已离线。
        /// </summary>
        public void MarkOffline(DateTime? offlineUtc = null)
        {
            IsOnline = false;
            LastOfflineTime = offlineUtc ?? DateTime.UtcNow;
            IsRoomReady = false;
        }

        /// <summary>
        /// 刷新任意业务包的活跃时间。
        /// </summary>
        public void MarkActive(float time)
        {
            LastActiveRealtime = time;
        }

        /// <summary>
        /// 刷新房间业务包的活跃时间。
        /// </summary>
        public void MarkRoomActive(float time)
        {
            LastRoomActiveRealtime = time;
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

            CurrentRoomId = roomId;
            IsRoomReady = true;
        }

        /// <summary>
        /// 解除当前正式所在房间。
        /// </summary>
        public void UnbindRoom()
        {
            CurrentRoomId = string.Empty;
            IsRoomReady = false;
        }

        /// <summary>
        /// 记录允许进入的目标房间。
        /// </summary>
        public void AuthorizeRoom(string roomId)
        {
            AuthorizedRoomId = roomId;
        }

        /// <summary>
        /// 清理进入房间授权。
        /// </summary>
        public void ClearAuthorizedRoom()
        {
            AuthorizedRoomId = string.Empty;
        }

        /// <summary>
        /// 更新房间准备状态。
        /// </summary>
        public void SetRoomReady(bool ready)
        {
            IsRoomReady = ready;
        }

        /// <summary>
        /// 消费新的消息序号。
        /// </summary>
        public bool TryConsumeSeq(uint seq)
        {
            if (seq <= LastReceivedSeq)
            {
                return false;
            }

            LastReceivedSeq = seq;
            return true;
        }

        /// <summary>
        /// 重置当前消息序号基线。
        /// </summary>
        public void ResetSeq(uint seq)
        {
            LastReceivedSeq = seq;
        }
    }
}
