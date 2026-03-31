using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
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
            if (msg == null)
            {
                NetLogger.LogError("ClientUserModule", "收到非法同步包: Msg 为空");
                return;
            }

            // 登录成功后，客户端会根据服务端结果决定是否进入恢复链。
            if (msg.Success)
            {
                string safeUid = !string.IsNullOrEmpty(_app?.Session?.AccountId)
                    ? _app.Session.AccountId
                    : msg.SessionId;

                if (_app == null)
                {
                    NetLogger.LogError("ClientUserModule", $"处理登录成功失败: _app 为空, SessionId:{msg.SessionId}");
                    return;
                }

                _app.Session.OnLoginSuccess(msg.SessionId, safeUid);
                NetLogger.LogInfo("ClientUserModule", $"登录成功, SessionId:{msg.SessionId}, Uid:{safeUid}");

                // 有恢复房间时，不直接回大厅，而是继续走重连确认。
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
                // 挂起态下如果恢复失败，直接回到大厅并清掉恢复上下文。
                if (_app != null && _app.State == ClientAppState.ConnectionSuspended)
                {
                    _app.AbortConnection();
                }
            }

            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_ReconnectResult(S2C_ReconnectResult msg)
        {
            if (msg == null)
            {
                NetLogger.LogError("ClientUserModule", "收到非法同步包: Msg 为空");
                return;
            }

            if (_app == null)
            {
                NetLogger.LogError("ClientUserModule", $"处理重连结果失败: _app 为空, Success:{msg.Success}, RoomId:{msg.RoomId}");
                return;
            }

            if (_app.State == ClientAppState.ReplayRoom)
            {
                NetLogger.LogWarning("ClientUserModule", "拦截: 当前处于回放模式，忽略重连结果");
                return;
            }

            // 重连成功后，本地重新创建在线房间并重建组件。
            if (msg.Success)
            {
                _app.EnterOnlineRoom(msg.RoomId);
                if (_app.CurrentRoom == null)
                {
                    NetLogger.LogError("ClientUserModule", $"重连失败: CurrentRoom 创建失败, RoomId:{msg.RoomId}");
                    _app.LeaveRoom();
                    GlobalTypeNetEvent.Broadcast(msg);
                    return;
                }

                bool buildSuccess = ClientRoomFactory.BuildComponents(_app.CurrentRoom, msg.ComponentIds);
                if (!buildSuccess)
                {
                    NetLogger.LogError("ClientUserModule", $"重连房间本地装配失败，已强制销毁本地实例并终止重连握手。RoomId:{msg.RoomId}");
                    _app.LeaveRoom();
                    GlobalTypeNetEvent.Broadcast(msg);
                    return;
                }

                GlobalTypeNetEvent.Broadcast(new Local_RoomEntered { Room = _app.CurrentRoom });
                NetLogger.LogInfo("ClientUserModule", $"重连房间本地装配完毕，准备发送就绪握手。RoomId:{msg.RoomId}");

                // 本地装配完成后，再向服务端发送 ReconnectReady。
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
            if (msg == null)
            {
                NetLogger.LogError("ClientUserModule", "收到非法同步包: Msg 为空");
                return;
            }

            if (_app == null)
            {
                NetLogger.LogError("ClientUserModule", $"处理踢下线失败: _app 为空, Reason:{msg.Reason}");
                return;
            }

            // 踢下线属于硬清理场景，会彻底清空本地会话。
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
