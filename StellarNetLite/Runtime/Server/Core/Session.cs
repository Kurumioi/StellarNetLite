using System;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

namespace StellarNet.Lite.Server.Core
{
    /// <summary>
    /// 服务端物理会话与业务状态的聚合体。
    /// 严格遵循 MSV 架构，作为底层网络状态的 Model 容器。
    /// </summary>
    public sealed class Session
    {
        /// <summary>
        /// 物理网络层分配的唯一会话标识（通常为随机 GUID）
        /// </summary>
        public string SessionId { get; }

        /// <summary>
        /// 底层鉴权系统的私密通行证标识与业务层的公开角色标识。
        /// 在单角色模型下，账号即角色，全局统一使用此字段进行寻址与广播。
        /// </summary>
        public string AccountId { get; }

        /// <summary>
        /// 当前绑定的底层物理连接 ID（由 Transport 层分配）
        /// </summary>
        public int ConnectionId { get; private set; }

        /// <summary>
        /// 当前所在的房间 ID
        /// </summary>
        public string CurrentRoomId { get; private set; }

        /// <summary>
        /// 已授权但尚未正式加入的房间 ID（用于加入房间的异步握手校验）
        /// </summary>
        public string AuthorizedRoomId { get; private set; }

        /// <summary>
        /// 物理连接是否在线
        /// </summary>
        public bool IsOnline { get; private set; }

        /// <summary>
        /// 房间内业务状态是否已就绪（用于拦截未加载完成时的广播消息）
        /// </summary>
        public bool IsRoomReady { get; private set; }

        /// <summary>
        /// 最后一次物理断线的时间（用于离线 GC 判定）
        /// </summary>
        public DateTime LastOfflineTime { get; private set; }

        /// <summary>
        /// 客户端上报的最新协议序列号（用于防重放攻击）
        /// </summary>
        public uint LastReceivedSeq { get; private set; }

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
        }

        #region 连接与生命周期管理

        public void UpdateConnection(int newConnectionId)
        {
            ConnectionId = newConnectionId;
            IsOnline = true;
        }

        public void MarkOffline()
        {
            IsOnline = false;
            LastOfflineTime = DateTime.UtcNow;
            IsRoomReady = false;
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