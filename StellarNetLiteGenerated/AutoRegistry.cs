using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Client.Core;

namespace StellarNet.Lite.Shared.Binders
{
    public static class AutoRegistry
    {
        public static readonly List<RoomComponentMeta> RoomComponentMetaList = new List<RoomComponentMeta>();

        public static void RegisterServer(ServerApp serverApp, Func<byte[], int, int, Type, object> deserializeFunc)
        {
        }

        public static void BindServerComponent(ServerRoomComponent comp, RoomDispatcher dispatcher, Func<byte[], int, int, Type, object> deserializeFunc)
        {
        }

        public static void RegisterClient(ClientApp clientApp, Func<byte[], int, int, Type, object> deserializeFunc)
        {
        }

        public static void BindClientComponent(ClientRoomComponent comp, ClientRoomDispatcher dispatcher, Func<byte[], int, int, Type, object> deserializeFunc)
        {
        }
    }
}