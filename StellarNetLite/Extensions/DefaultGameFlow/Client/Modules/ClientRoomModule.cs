using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;

namespace StellarNet.Lite.Client.Modules
{
    [ClientModule("ClientRoomModule", "客户端房间生命周期模块")]
    /// <summary>
    /// 客户端房间生命周期模块。
    /// 负责处理进房、离房和房间装配完成后的进入事件。
    /// </summary>
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
            if (_app.State == ClientAppState.ReplayRoom) return;

            if (msg.Success)
            {
                _app.EnterOnlineRoom(msg.RoomId);
                if (!ClientRoomFactory.BuildComponents(_app.CurrentRoom, msg.ComponentIds))
                {
                    _app.LeaveRoom();
                    return;
                }

                GlobalTypeNetEvent.Broadcast(new Local_RoomEntered { Room = _app.CurrentRoom });
                _app.SendMessage(new C2S_RoomSetupReady { RoomId = msg.RoomId });
            }

            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_JoinRoomResult(S2C_JoinRoomResult msg)
        {
            if (_app.State == ClientAppState.ReplayRoom) return;

            if (msg.Success)
            {
                _app.EnterOnlineRoom(msg.RoomId);
                if (!ClientRoomFactory.BuildComponents(_app.CurrentRoom, msg.ComponentIds))
                {
                    _app.LeaveRoom();
                    return;
                }

                GlobalTypeNetEvent.Broadcast(new Local_RoomEntered { Room = _app.CurrentRoom });
                _app.SendMessage(new C2S_RoomSetupReady { RoomId = msg.RoomId });
            }

            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_LeaveRoomResult(S2C_LeaveRoomResult msg)
        {
            if (_app.State == ClientAppState.ReplayRoom) return;
            _app.LeaveRoom();
            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_RoomSetupResult(S2C_RoomSetupResult msg)
        {
            if (_app.State == ClientAppState.ReplayRoom || msg == null) return;

            if (msg.Success)
            {
                _app.ConfirmCurrentRoom(msg.RoomId);
            }
            else
            {
                _app.Session.ClearRecoveryContext();
                _app.LeaveRoom(true);
                GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = $"房间装配失败: {msg.Reason}" });
            }

            GlobalTypeNetEvent.Broadcast(msg);
        }
    }
}
