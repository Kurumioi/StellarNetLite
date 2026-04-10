using System;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

namespace StellarNet.Lite.Server.Core
{
    public sealed class Session
    {
        public string SessionId { get; }
        public string AccountId { get; }
        public int ConnectionId { get; private set; }
        public string CurrentRoomId { get; private set; }
        public string AuthorizedRoomId { get; private set; }
        public bool IsOnline { get; private set; }
        public bool IsRoomReady { get; private set; }
        public DateTime LastOfflineTime { get; private set; }
        public uint LastReceivedSeq { get; private set; }
        
        public float LastActiveRealtime { get; private set; }
        // 新增：专门记录最后一次收到房间业务包的时间，用于精准的防滑冰判定
        public float LastRoomActiveRealtime { get; private set; }

        public Session(string sessionId, string accountId, int connectionId)
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
            LastActiveRealtime = Time.realtimeSinceStartup;
            LastRoomActiveRealtime = Time.realtimeSinceStartup;
        }

        #region 连接与生命周期管理

        public void UpdateConnection(int newConnectionId)
        {
            ConnectionId = newConnectionId;
            IsOnline = true;
            LastActiveRealtime = Time.realtimeSinceStartup;
            LastRoomActiveRealtime = Time.realtimeSinceStartup;
        }

        public void MarkOffline()
        {
            IsOnline = false;
            LastOfflineTime = DateTime.UtcNow;
            IsRoomReady = false;
        }

        public void MarkActive(float time)
        {
            LastActiveRealtime = time;
        }

        public void MarkRoomActive(float time)
        {
            LastRoomActiveRealtime = time;
        }

        #endregion

        #region 房间状态管理

        public void BindRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("Session", "绑定房间失败: 传入的 roomId 为空", "-", SessionId);
                return;
            }
            CurrentRoomId = roomId;
            IsRoomReady = true;
        }

        public void UnbindRoom()
        {
            CurrentRoomId = string.Empty;
            IsRoomReady = false;
        }

        public void AuthorizeRoom(string roomId)
        {
            AuthorizedRoomId = roomId;
        }

        public void ClearAuthorizedRoom()
        {
            AuthorizedRoomId = string.Empty;
        }

        public void SetRoomReady(bool ready)
        {
            IsRoomReady = ready;
        }

        #endregion

        #region 安全与防重放

        public bool TryConsumeSeq(uint seq)
        {
            if (seq <= LastReceivedSeq)
            {
                return false;
            }
            LastReceivedSeq = seq;
            return true;
        }

        public void ResetSeq(uint seq)
        {
            LastReceivedSeq = seq;
        }

        #endregion
    }
}
