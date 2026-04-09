using System;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Core
{
    public sealed class ClientSession
    {
        public string SessionId { get; private set; }

        /// <summary>
        /// 统一使用 AccountId 作为唯一业务标识，剔除了冗余的 Uid
        /// </summary>
        public string AccountId { get; private set; }

        public string CurrentRoomId { get; private set; }

        public bool IsLoggedIn => !string.IsNullOrEmpty(SessionId);

        public bool IsPhysicalOnline { get; set; } = true;
        public string LastBoundRoomId { get; private set; }
        public DateTime LastDisconnectRealtime { get; set; }
        public bool IsReconnecting { get; set; }

        public void SetAccountId(string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId))
            {
                NetLogger.LogError("ClientSession", "设置账号失败: accountId 为空");
                return;
            }

            AccountId = accountId.Trim();
        }

        public void OnLoginSuccess(string sessionId, string accountId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                NetLogger.LogError("ClientSession", "登录成功回调失败: SessionId 为空");
                return;
            }

            SessionId = sessionId;
            AccountId = accountId ?? string.Empty;
            IsPhysicalOnline = true;
            IsReconnecting = false;
        }

        public void BindRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("ClientSession", "绑定房间失败: roomId 为空");
                return;
            }

            CurrentRoomId = roomId;
            LastBoundRoomId = roomId;
        }

        public void UnbindRoom()
        {
            CurrentRoomId = string.Empty;
        }

        public void Clear()
        {
            SessionId = string.Empty;
            AccountId = string.Empty;
            CurrentRoomId = string.Empty;
            LastBoundRoomId = string.Empty;
            IsReconnecting = false;
            IsPhysicalOnline = false;
            LastDisconnectRealtime = default;
        }

        public void ClearRecoveryContext()
        {
            LastBoundRoomId = string.Empty;
            IsReconnecting = false;
        }
    }
}