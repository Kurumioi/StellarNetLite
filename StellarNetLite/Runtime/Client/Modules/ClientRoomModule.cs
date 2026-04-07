using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
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
    }
}