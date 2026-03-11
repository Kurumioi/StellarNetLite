using System;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;

namespace StellarNet.Lite.Client.Modules
{
    [GlobalModule("ClientLobbyModule", "客户端大厅模块")]
    public sealed class ClientLobbyModule
    {
        private readonly ClientApp _app;

        public ClientLobbyModule(ClientApp app)
        {
            _app = app;
        }

        [NetHandler]
        public void OnS2C_RoomListResponse(S2C_RoomListResponse msg)
        {
            if (msg == null) return;
            GlobalTypeNetEvent.Broadcast(msg);
        }
    }
}