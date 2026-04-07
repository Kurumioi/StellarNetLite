using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Client.Modules
{
    [ClientModule("ClientUserModule", "客户端用户模块")]
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
            if (msg.Success)
            {
                string safeUid = !string.IsNullOrEmpty(_app.Session.AccountId) ? _app.Session.AccountId : msg.SessionId;
                _app.Session.OnLoginSuccess(msg.SessionId, safeUid);

                if (msg.HasReconnectRoom)
                {
                    if (_app.State == ClientAppState.ConnectionSuspended)
                        _app.SendMessage(new C2S_ConfirmReconnect { Accept = true });
                    else
                        _app.Session.IsReconnecting = true;
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
                if (_app.State == ClientAppState.ConnectionSuspended) _app.AbortConnection();
            }

            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_ReconnectResult(S2C_ReconnectResult msg)
        {
            if (_app.State == ClientAppState.ReplayRoom) return;

            if (msg.Success)
            {
                _app.EnterOnlineRoom(msg.RoomId);
                if (_app.CurrentRoom == null || !ClientRoomFactory.BuildComponents(_app.CurrentRoom, msg.ComponentIds))
                {
                    _app.LeaveRoom();
                    GlobalTypeNetEvent.Broadcast(msg);
                    return;
                }

                GlobalTypeNetEvent.Broadcast(new Local_RoomEntered { Room = _app.CurrentRoom });
                _app.SendMessage(new C2S_ReconnectReady());
            }
            else
            {
                _app.Session.ClearRecoveryContext();
                if (_app.State == ClientAppState.ConnectionSuspended)
                {
                    _app.LeaveRoom();
                    GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = $"重连失败: {msg.Reason}" });
                }
            }

            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_KickOut(S2C_KickOut msg)
        {
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