// ========================================================
// 自动生成的静态装配聚合器。
// 请勿手动修改！由 LiteProtocolScanner 自动生成。
// ========================================================
using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Generated.Binders.RoomComponents;
using StellarNet.Lite.Shared.Generated.Binders.GlobalModules;

namespace StellarNet.Lite.Shared.Binders
{
    public static class AutoRegistry
    {
        public static readonly List<RoomComponentMeta> RoomComponentMetaList = BuildRoomComponentMetaList();

        private static List<RoomComponentMeta> BuildRoomComponentMetaList()
        {
            var result = new List<RoomComponentMeta>();
            Generated_Game_RoomBinder.AppendRoomComponentMeta(result);
            Generated_Lite_RoomBinder.AppendRoomComponentMeta(result);
            return result;
        }

        public static void RegisterServer(ServerApp serverApp, Func<byte[], int, int, Type, object> deserializeFunc)
        {
            if (serverApp == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", "RegisterServer failed");
                return;
            }
            if (deserializeFunc == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", "RegisterServer failed");
                return;
            }
            ServerRoomFactory.Clear();
            Generated_Lite_ModuleBinder.RegisterServer(serverApp, deserializeFunc);
            Generated_Game_RoomBinder.RegisterServerFactory(serverApp);
            Generated_Lite_RoomBinder.RegisterServerFactory(serverApp);
        }

        public static void BindServerComponent(StellarNet.Lite.Server.Core.RoomComponent comp, StellarNet.Lite.Server.Core.RoomDispatcher dispatcher, Func<byte[], int, int, Type, object> deserializeFunc)
        {
            if (comp == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", "BindServerComponent failed");
                return;
            }
            if (Generated_Game_RoomBinder.TryBindServer(comp, dispatcher, deserializeFunc))
            {
                return;
            }
            if (Generated_Lite_RoomBinder.TryBindServer(comp, dispatcher, deserializeFunc))
            {
                return;
            }
        }

        public static void RegisterClient(ClientApp clientApp, Func<byte[], int, int, Type, object> deserializeFunc)
        {
            if (clientApp == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", "RegisterClient failed");
                return;
            }
            if (deserializeFunc == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", "RegisterClient failed");
                return;
            }
            ClientRoomFactory.Clear();
            Generated_Lite_ModuleBinder.RegisterClient(clientApp, deserializeFunc);
            Generated_Game_RoomBinder.RegisterClientFactory(clientApp);
            Generated_Lite_RoomBinder.RegisterClientFactory(clientApp);
        }

        public static void BindClientComponent(StellarNet.Lite.Client.Core.ClientRoomComponent comp, StellarNet.Lite.Client.Core.ClientRoomDispatcher dispatcher, Func<byte[], int, int, Type, object> deserializeFunc)
        {
            if (comp == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", "BindClientComponent failed");
                return;
            }
            if (Generated_Game_RoomBinder.TryBindClient(comp, dispatcher, deserializeFunc))
            {
                return;
            }
            if (Generated_Lite_RoomBinder.TryBindClient(comp, dispatcher, deserializeFunc))
            {
                return;
            }
        }
    }
}
