using System;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

namespace StellarNet.Lite.Server.Core
{
    public sealed class Session
    {
        public string SessionId { get; }
        public string Uid { get; }
        public int ConnectionId { get; private set; }
        public string CurrentRoomId { get; private set; }
        public string AuthorizedRoomId { get; private set; }

        public bool IsOnline => ConnectionId >= 0;

        // 核心修复 2：新增房间就绪状态。防止重连瞬间，客户端尚未装配完毕就被高频房间包轰炸
        public bool IsRoomReady { get; private set; }

        public DateTime LastOfflineTime { get; private set; }
        public uint LastReceivedSeq { get; private set; }

        public Session(string sessionId, string uid, int connectionId)
        {
            SessionId = sessionId;
            Uid = uid;
            ConnectionId = connectionId;
            CurrentRoomId = string.Empty;
            AuthorizedRoomId = string.Empty;
            LastOfflineTime = DateTime.UtcNow;
            LastReceivedSeq = 0;
            IsRoomReady = false;
        }

        public void UpdateConnection(int newConnectionId)
        {
            ConnectionId = newConnectionId;
        }

        public void BindRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("Session", "绑定房间失败: 传入的 roomId 为空", "-", SessionId);
                return;
            }

            CurrentRoomId = roomId;
            IsRoomReady = true; // 正常加入房间时，直接就绪
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

        public void MarkOffline()
        {
            ConnectionId = -1;
            LastOfflineTime = DateTime.UtcNow;
            IsRoomReady = false; // 离线时立刻取消就绪状态
        }

        public void SetRoomReady(bool ready)
        {
            IsRoomReady = ready;
        }

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
    }
}