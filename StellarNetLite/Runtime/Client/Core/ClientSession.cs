using System;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Core
{
    /// <summary>
    /// 客户端会话状态。
    /// </summary>
    public sealed class ClientSession
    {
        /// <summary>
        /// 当前会话 Id。
        /// </summary>
        public string SessionId { get; private set; }

        /// <summary>
        /// 当前登录账号 Id。
        /// </summary>
        public string AccountId { get; private set; }

        /// <summary>
        /// 当前正式所在的房间 Id。
        /// </summary>
        public string CurrentRoomId { get; private set; }

        /// <summary>
        /// 当前是否已完成登录。
        /// </summary>
        public bool IsLoggedIn => !string.IsNullOrEmpty(SessionId);

        /// <summary>
        /// 当前物理连接是否在线。
        /// </summary>
        public bool IsPhysicalOnline { get; set; } = true;

        /// <summary>
        /// 最近一次成功绑定的房间 Id。
        /// </summary>
        public string LastBoundRoomId { get; private set; }

        /// <summary>
        /// 最近一次断线时间。
        /// </summary>
        public DateTime LastDisconnectRealtime { get; set; }

        /// <summary>
        /// 当前是否处于重连流程。
        /// </summary>
        public bool IsReconnecting { get; set; }

        /// <summary>
        /// 设置当前账号 Id。
        /// </summary>
        public void SetAccountId(string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId))
            {
                NetLogger.LogError("ClientSession", "设置账号失败: AccountId 为空");
                return;
            }

            AccountId = accountId.Trim();
        }

        /// <summary>
        /// 更新登录成功后的会话状态。
        /// </summary>
        public void OnLoginSuccess(string sessionId, string accountId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                NetLogger.LogError("ClientSession", "登录态更新失败: SessionId 为空");
                return;
            }

            SessionId = sessionId;
            AccountId = accountId ?? string.Empty;
            IsPhysicalOnline = true;
            IsReconnecting = false;

            NetLogger.LogInfo("ClientSession", "登录态更新完成", sessionId: SessionId, extraContext: $"AccountId:{AccountId}");
        }

        /// <summary>
        /// 绑定当前所在房间。
        /// </summary>
        public void BindRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("ClientSession", "绑定房间失败: RoomId 为空");
                return;
            }

            CurrentRoomId = roomId;
            LastBoundRoomId = roomId;
            NetLogger.LogInfo("ClientSession", "房间绑定完成", roomId, SessionId);
        }

        /// <summary>
        /// 解绑当前所在房间。
        /// </summary>
        public void UnbindRoom()
        {
            string roomId = CurrentRoomId;
            CurrentRoomId = string.Empty;

            if (!string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogInfo("ClientSession", "房间解绑完成", roomId, SessionId);
            }
        }

        /// <summary>
        /// 清理全部会话状态。
        /// </summary>
        public void Clear()
        {
            string sessionId = SessionId;
            string roomId = CurrentRoomId;

            SessionId = string.Empty;
            AccountId = string.Empty;
            CurrentRoomId = string.Empty;
            LastBoundRoomId = string.Empty;
            IsReconnecting = false;
            IsPhysicalOnline = false;
            LastDisconnectRealtime = default;

            NetLogger.LogInfo("ClientSession", "会话清理完成", roomId, sessionId);
        }

        /// <summary>
        /// 清理重连上下文。
        /// </summary>
        public void ClearRecoveryContext()
        {
            LastBoundRoomId = string.Empty;
            IsReconnecting = false;

            NetLogger.LogInfo("ClientSession", "重连上下文清理完成", sessionId: SessionId);
        }
    }
}
