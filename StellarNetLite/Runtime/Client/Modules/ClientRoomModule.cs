using System;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Client.Core.Events;

namespace StellarNet.Lite.Client.Modules
{
    [ClientModule("ClientRoomModule", "客户端房间生命周期模块")]
    public sealed class ClientRoomModule
    {
        private readonly ClientApp _app;

        public ClientRoomModule(ClientApp app)
        {
            _app = app;
        }

        [NetHandler]
        public void OnS2C_CreateRoomResult(S2C_CreateRoomResult msg)
        {
            if (msg == null)
            {
                NetLogger.LogError("ClientRoomModule", "收到非法同步包: Msg 为空");
                return;
            }

            if (_app.State == ClientAppState.ReplayRoom)
            {
                NetLogger.LogWarning("ClientRoomModule", "拦截: 当前处于回放模式，忽略建房结果");
                return;
            }

            // 建房成功后，客户端先本地创建房间并装配组件。
            if (msg.Success)
            {
                _app.EnterOnlineRoom(msg.RoomId);
                bool buildSuccess = ClientRoomFactory.BuildComponents(_app.CurrentRoom, msg.ComponentIds);
                if (!buildSuccess)
                {
                    NetLogger.LogError("ClientRoomModule", $"房间 {msg.RoomId} 本地装配失败，已强制销毁本地实例并终止握手");
                    _app.LeaveRoom();
                    return;
                }

                GlobalTypeNetEvent.Broadcast(new Local_RoomEntered { Room = _app.CurrentRoom });
                NetLogger.LogInfo("ClientRoomModule", $"建房成功, 本地装配完毕，准备发送就绪握手。房间: {msg.RoomId}");

                // 本地准备好后，再向服务端发送 RoomSetupReady 完成握手。
                var readyMsg = new C2S_RoomSetupReady { RoomId = msg.RoomId };
                _app.SendMessage(readyMsg);
            }
            else
            {
                NetLogger.LogError("ClientRoomModule", $"建房失败: {msg.Reason}");
            }

            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_JoinRoomResult(S2C_JoinRoomResult msg)
        {
            if (msg == null)
            {
                NetLogger.LogError("ClientRoomModule", "收到非法同步包: Msg 为空");
                return;
            }

            if (_app.State == ClientAppState.ReplayRoom)
            {
                NetLogger.LogWarning("ClientRoomModule", "拦截: 当前处于回放模式，忽略加房结果");
                return;
            }

            // 加房成功与建房成功的本地处理链完全一致。
            if (msg.Success)
            {
                _app.EnterOnlineRoom(msg.RoomId);
                bool buildSuccess = ClientRoomFactory.BuildComponents(_app.CurrentRoom, msg.ComponentIds);
                if (!buildSuccess)
                {
                    NetLogger.LogError("ClientRoomModule", $"房间 {msg.RoomId} 本地装配失败，已强制销毁本地实例并终止握手");
                    _app.LeaveRoom();
                    return;
                }

                GlobalTypeNetEvent.Broadcast(new Local_RoomEntered { Room = _app.CurrentRoom });
                NetLogger.LogInfo("ClientRoomModule", $"加房成功, 本地装配完毕，准备发送就绪握手。房间: {msg.RoomId}");

                var readyMsg = new C2S_RoomSetupReady { RoomId = msg.RoomId };
                _app.SendMessage(readyMsg);
            }
            else
            {
                NetLogger.LogError("ClientRoomModule", $"加房失败: {msg.Reason}");
            }

            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_LeaveRoomResult(S2C_LeaveRoomResult msg)
        {
            if (msg == null)
            {
                NetLogger.LogError("ClientRoomModule", "收到非法同步包: Msg 为空");
                return;
            }

            if (_app.State == ClientAppState.ReplayRoom)
            {
                NetLogger.LogWarning("ClientRoomModule", "拦截: 当前处于回放模式，忽略离房结果");
                return;
            }

            // 收到离房确认后统一走 ClientApp.LeaveRoom 收尾。
            _app.LeaveRoom();
            NetLogger.LogInfo("ClientRoomModule", "已离开房间");
            GlobalTypeNetEvent.Broadcast(msg);
        }
    }
}