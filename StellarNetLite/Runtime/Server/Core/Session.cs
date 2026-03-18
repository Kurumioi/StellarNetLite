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

        // 核心修复：移除对 ConnectionId >= 0 的依赖，引入独立的 IsOnline 标识
        public bool IsOnline { get; private set; }

        public bool IsRoomReady { get; private set; }
        public DateTime LastOfflineTime { get; private set; }
        public uint LastReceivedSeq { get; private set; }

        public Session(string sessionId, string uid, int connectionId)
        {
            SessionId = sessionId;
            Uid = uid;
            ConnectionId = connectionId;
            IsOnline = true; // 实例化时即为在线状态
            CurrentRoomId = string.Empty;
            AuthorizedRoomId = string.Empty;
            LastOfflineTime = DateTime.UtcNow;
            LastReceivedSeq = 0;
            IsRoomReady = false;
        }

        public void UpdateConnection(int newConnectionId)
        {
            ConnectionId = newConnectionId;
            IsOnline = true; // 更新连接时恢复在线状态
        }

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

        public void MarkOffline()
        {
            IsOnline = false; // 显式标记离线
            LastOfflineTime = DateTime.UtcNow;
            IsRoomReady = false;
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