using System;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

namespace StellarNet.Lite.Client.Core
{
    public sealed class ClientSession
    {
        public string SessionId { get; private set; }
        public string Uid { get; private set; }
        public string CurrentRoomId { get; private set; }
        public bool IsLoggedIn => !string.IsNullOrEmpty(SessionId);

        // 核心新增：断线恢复上下文。必须缓存这些数据，以便在物理断线且房间销毁后，依然能发起重连协商。
        public string AccountId { get; private set; }
        public bool IsPhysicalOnline { get; set; } = true;
        public string LastBoundRoomId { get; private set; }
        public DateTime LastDisconnectRealtime { get; set; }
        public bool IsReconnecting { get; set; }

        // 提供独立的账号设置入口，因为 AccountId 是在发送 C2S_Login 时就确定的，而 SessionId 是服务端返回的
        public void SetAccountId(string accountId)
        {
            AccountId = accountId;
        }

        public void OnLoginSuccess(string sessionId, string uid)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                NetLogger.LogError("[ClientSession]", "登录成功回调失败: 下发的 SessionId 为空");
                return;
            }

            SessionId = sessionId;
            Uid = uid;
            IsPhysicalOnline = true;
            IsReconnecting = false;
        }

        public void BindRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("[ClientSession]", "绑定房间失败: 传入的 roomId 为空");
                return;
            }

            CurrentRoomId = roomId;
            // 缓存最近一次绑定的房间 ID，用于断线后向服务端确认恢复目标
            LastBoundRoomId = roomId;
        }

        public void UnbindRoom()
        {
            CurrentRoomId = string.Empty;
        }

        public void Clear()
        {
            SessionId = string.Empty;
            Uid = string.Empty;
            CurrentRoomId = string.Empty;

            // 触发硬清理时，必须彻底清空恢复上下文，防止后续的普通登录误触发重连链
            AccountId = string.Empty;
            LastBoundRoomId = string.Empty;
            IsReconnecting = false;
        }

        // 平滑降级时调用：仅清理恢复标记，但不清空当前 SessionId，保持大厅在线态
        public void ClearRecoveryContext()
        {
            LastBoundRoomId = string.Empty;
            IsReconnecting = false;
        }
    }
}