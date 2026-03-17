using System;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Client.Core.Events;

namespace StellarNet.Lite.Client.Modules
{
    [GlobalModule("ClientUserModule", "客户端用户模块")]
    public sealed class ClientUserModule
    {
        private readonly ClientApp _app;

        public ClientUserModule(ClientApp app)
        {
            _app = app;
        }

        [NetHandler]
        public void OnS2C_LoginResult(S2C_LoginResult msg)
        {
            if (msg == null) return;

            if (msg.Success)
            {
                _app.Session.OnLoginSuccess(msg.SessionId, "UID_PLACEHOLDER");
                NetLogger.LogInfo($"[ClientUserModule]", $"登录成功, SessionId: {msg.SessionId}");

                if (msg.HasReconnectRoom)
                {
                    if (_app.State == ClientAppState.ConnectionSuspended)
                    {
                        _app.SendMessage(new C2S_ConfirmReconnect { Accept = true });
                    }
                    else
                    {
                        _app.Session.IsReconnecting = true;
                    }
                }
                else
                {
                    if (_app.State == ClientAppState.ConnectionSuspended)
                    {
                        _app.Session.ClearRecoveryContext();
                        _app.LeaveRoom();
                        GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = "对局已结束，已返回大厅" });
                    }
                }
            }
            else
            {
                if (_app.State == ClientAppState.ConnectionSuspended)
                {
                    _app.AbortConnection();
                }
            }

            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_ReconnectResult(S2C_ReconnectResult msg)
        {
            if (msg == null) return;
            if (_app.State == ClientAppState.ReplayRoom) return;

            if (msg.Success)
            {
                _app.EnterOnlineRoom(msg.RoomId);
                bool buildSuccess = ClientRoomFactory.BuildComponents(_app.CurrentRoom, msg.ComponentIds);
                if (!buildSuccess)
                {
                    NetLogger.LogError($"[ClientUserModule]", $"重连房间 {msg.RoomId} 本地装配失败，已强制销毁本地实例并终止重连握手");
                    _app.LeaveRoom();
                    return;
                }

                // 核心修复：重连装配完毕后，同步抛出房间进入事件
                GlobalTypeNetEvent.Broadcast(new Local_RoomEntered { Room = _app.CurrentRoom });

                NetLogger.LogInfo($"[ClientUserModule]", $"重连房间 {msg.RoomId} 本地装配完毕，准备发送就绪握手");
                var readyMsg = new C2S_ReconnectReady();
                _app.SendMessage(readyMsg);
            }
            else
            {
                if (_app.State == ClientAppState.ConnectionSuspended)
                {
                    _app.Session.ClearRecoveryContext();
                    _app.LeaveRoom();
                    GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = $"重连失败: {msg.Reason}" });
                }
                else
                {
                    _app.Session.ClearRecoveryContext();
                }
            }

            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_KickOut(S2C_KickOut msg)
        {
            if (msg == null) return;

            if (_app.State == ClientAppState.OnlineRoom || _app.State == ClientAppState.ConnectionSuspended)
            {
                _app.LeaveRoom();
            }

            _app.Session.Clear();
            GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = $"您已被踢下线: {msg.Reason}" });
            GlobalTypeNetEvent.Broadcast(msg);
        }
    }
}