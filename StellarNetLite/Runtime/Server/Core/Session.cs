using System;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

namespace StellarNet.Lite.Server.Core
{
    /// <summary>
    /// 服务端会话对象。
    /// 表示一个账号在服务端侧的业务连接上下文。
    /// </summary>
    public sealed class Session
    {
        // 业务层会话标识。
        public string SessionId { get; }
        // 当前登录账号 Id。
        public string Uid { get; }
        // 当前绑定的物理连接 Id。
        public int ConnectionId { get; private set; }
        // 当前所在房间。
        public string CurrentRoomId { get; private set; }
        // 已授权但尚未正式加入的目标房间。
        public string AuthorizedRoomId { get; private set; }

        // 物理连接在线状态。
        public bool IsOnline { get; private set; }

        // 当前客户端是否已完成房间装配握手。
        public bool IsRoomReady { get; private set; }
        // 最近一次离线时间。
        public DateTime LastOfflineTime { get; private set; }
        // 最近成功消费的客户端 Seq。
        public uint LastReceivedSeq { get; private set; }

        public Session(string sessionId, string uid, int connectionId)
        {
            SessionId = sessionId;
            Uid = uid;
            ConnectionId = connectionId;
            IsOnline = true;
            CurrentRoomId = string.Empty;
            AuthorizedRoomId = string.Empty;
            LastOfflineTime = DateTime.UtcNow;
            LastReceivedSeq = 0;
            IsRoomReady = false;
        }

        public void UpdateConnection(int newConnectionId)
        {
            // 重连成功时切换物理连接，并恢复在线状态。
            ConnectionId = newConnectionId;
            IsOnline = true;
        }

        public void BindRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("Session", "绑定房间失败: 传入的 roomId 为空", "-", SessionId);
                return;
            }

            // 进入房间后默认视为房间链路 ready。
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
            // 先授权，等待客户端本地装配完成后再正式入房。
            AuthorizedRoomId = roomId;
        }

        public void ClearAuthorizedRoom()
        {
            AuthorizedRoomId = string.Empty;
        }

        public void MarkOffline()
        {
            // 断线后保留房间上下文，但清掉 ready 状态。
            IsOnline = false;
            LastOfflineTime = DateTime.UtcNow;
            IsRoomReady = false;
        }

        public void SetRoomReady(bool ready)
        {
            IsRoomReady = ready;
        }

        public bool TryConsumeSeq(uint seq)
        {
            // Seq 只允许严格递增，重复包会被拦截。
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
