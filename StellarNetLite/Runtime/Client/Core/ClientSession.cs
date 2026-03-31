using System;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Core
{
    /// <summary>
    /// 客户端会话镜像。
    /// 保存登录态、房间态和断线恢复上下文。
    /// </summary>
    public sealed class ClientSession
    {
        // 服务端返回的正式 SessionId。
        public string SessionId { get; private set; }
        // 当前账号显示用 Uid。
        public string Uid { get; private set; }
        // 当前所在房间 Id。
        public string CurrentRoomId { get; private set; }
        public bool IsLoggedIn => !string.IsNullOrEmpty(SessionId);

        // 本地输入的账号 Id。
        public string AccountId { get; private set; }
        // 物理连接是否在线。
        public bool IsPhysicalOnline { get; set; } = true;
        // 最近一次成功绑定过的房间，用于恢复提示。
        public string LastBoundRoomId { get; private set; }
        // 最近一次掉线时间。
        public DateTime LastDisconnectRealtime { get; set; }
        // 当前是否处于恢复链中。
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

        public void OnLoginSuccess(string sessionId, string uid)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                NetLogger.LogError("ClientSession", "登录成功回调失败: SessionId 为空");
                return;
            }

            // 登录成功后更新正式身份，并退出恢复等待态。
            SessionId = sessionId;
            Uid = uid ?? string.Empty;
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

            // 绑定房间时顺便刷新最近房间上下文。
            CurrentRoomId = roomId;
            LastBoundRoomId = roomId;
        }

        public void UnbindRoom()
        {
            CurrentRoomId = string.Empty;
        }

        public void Clear()
        {
            // 硬清理会话时一并清掉恢复链上下文。
            SessionId = string.Empty;
            Uid = string.Empty;
            CurrentRoomId = string.Empty;
            AccountId = string.Empty;
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
