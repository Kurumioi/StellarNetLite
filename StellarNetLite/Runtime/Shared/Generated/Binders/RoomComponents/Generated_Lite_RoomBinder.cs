// ========================================================
// 自动生成的房间组件绑定分片。
// ========================================================
using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Client.Core;

namespace StellarNet.Lite.Shared.Generated.Binders.RoomComponents
{
    public static class Generated_Lite_RoomBinder
    {
        public static void AppendRoomComponentMeta(List<RoomComponentMeta> target)
        {
            if (target == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_RoomBinder", "AppendRoomComponentMeta failed");
                return;
            }
            target.Add(new RoomComponentMeta { Id = 1, Name = "RoomSettings", DisplayName = "基础房间设置" });
            target.Add(new RoomComponentMeta { Id = 200, Name = "ObjectSync", DisplayName = "空间与动画同步核心服务" });
        }

        public static void RegisterServerFactory(ServerApp serverApp)
        {
            if (serverApp == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_RoomBinder", "RegisterServerFactory failed");
                return;
            }
            ServerRoomFactory.Register(1, () => new StellarNet.Lite.Server.Components.ServerRoomSettingsComponent(serverApp));
            ServerRoomFactory.Register(200, () => new StellarNet.Lite.Server.Components.ServerObjectSyncComponent(serverApp));
        }

        public static void RegisterClientFactory(ClientApp clientApp)
        {
            if (clientApp == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_RoomBinder", "RegisterClientFactory failed");
                return;
            }
            ClientRoomFactory.Register(1, () => new StellarNet.Lite.Client.Components.ClientRoomSettingsComponent(clientApp));
            ClientRoomFactory.Register(200, () => new StellarNet.Lite.Client.Components.ClientObjectSyncComponent(clientApp));
        }

        public static bool TryBindServer(StellarNet.Lite.Server.Core.RoomComponent comp, StellarNet.Lite.Server.Core.RoomDispatcher dispatcher, Func<byte[], int, int, Type, object> deserializeFunc)
        {
            if (comp == null || dispatcher == null || deserializeFunc == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_RoomBinder", "TryBindServer failed");
                return false;
            }
            switch (comp)
            {
                case StellarNet.Lite.Server.Components.ServerRoomSettingsComponent c_1:
                    dispatcher.Register(303, (session, packet) => {
                        if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.C2S_SetReady)) is StellarNet.Lite.Shared.Protocol.C2S_SetReady msg) {
                            c_1.OnC2S_SetReady(session, msg);
                        }
                        else
                        {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_RoomBinder", "Deserialize failed");
                        }
                    });
                    dispatcher.Register(500, (session, packet) => {
                        if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.C2S_StartGame)) is StellarNet.Lite.Shared.Protocol.C2S_StartGame msg) {
                            c_1.OnC2S_StartGame(session, msg);
                        }
                        else
                        {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_RoomBinder", "Deserialize failed");
                        }
                    });
                    dispatcher.Register(502, (session, packet) => {
                        if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.C2S_EndGame)) is StellarNet.Lite.Shared.Protocol.C2S_EndGame msg) {
                            c_1.OnC2S_EndGame(session, msg);
                        }
                        else
                        {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_RoomBinder", "Deserialize failed");
                        }
                    });
                    return true;
                case StellarNet.Lite.Server.Components.ServerObjectSyncComponent c_200:
                    return true;
            }
            return false;
        }

        public static bool TryBindClient(StellarNet.Lite.Client.Core.ClientRoomComponent comp, StellarNet.Lite.Client.Core.ClientRoomDispatcher dispatcher, Func<byte[], int, int, Type, object> deserializeFunc)
        {
            if (comp == null || dispatcher == null || deserializeFunc == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_RoomBinder", "TryBindClient failed");
                return false;
            }
            switch (comp)
            {
                case StellarNet.Lite.Client.Components.ClientRoomSettingsComponent c_1:
                    dispatcher.Register(300, (packet) => {
                        if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.S2C_RoomSnapshot)) is StellarNet.Lite.Shared.Protocol.S2C_RoomSnapshot msg) {
                            c_1.OnS2C_RoomSnapshot(msg);
                        }
                        else
                        {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_RoomBinder", "Deserialize failed");
                        }
                    });
                    dispatcher.Register(301, (packet) => {
                        if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.S2C_MemberJoined)) is StellarNet.Lite.Shared.Protocol.S2C_MemberJoined msg) {
                            c_1.OnS2C_MemberJoined(msg);
                        }
                        else
                        {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_RoomBinder", "Deserialize failed");
                        }
                    });
                    dispatcher.Register(302, (packet) => {
                        if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.S2C_MemberLeft)) is StellarNet.Lite.Shared.Protocol.S2C_MemberLeft msg) {
                            c_1.OnS2C_MemberLeft(msg);
                        }
                        else
                        {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_RoomBinder", "Deserialize failed");
                        }
                    });
                    dispatcher.Register(304, (packet) => {
                        if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.S2C_MemberReadyChanged)) is StellarNet.Lite.Shared.Protocol.S2C_MemberReadyChanged msg) {
                            c_1.OnS2C_MemberReadyChanged(msg);
                        }
                        else
                        {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_RoomBinder", "Deserialize failed");
                        }
                    });
                    dispatcher.Register(501, (packet) => {
                        if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.S2C_GameStarted)) is StellarNet.Lite.Shared.Protocol.S2C_GameStarted msg) {
                            c_1.OnS2C_GameStarted(msg);
                        }
                        else
                        {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_RoomBinder", "Deserialize failed");
                        }
                    });
                    dispatcher.Register(503, (packet) => {
                        if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.S2C_GameEnded)) is StellarNet.Lite.Shared.Protocol.S2C_GameEnded msg) {
                            c_1.OnS2C_GameEnded(msg);
                        }
                        else
                        {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_RoomBinder", "Deserialize failed");
                        }
                    });
                    return true;
                case StellarNet.Lite.Client.Components.ClientObjectSyncComponent c_200:
                    dispatcher.Register(1100, (packet) => {
                        if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.S2C_ObjectSpawn)) is StellarNet.Lite.Shared.Protocol.S2C_ObjectSpawn msg) {
                            c_200.OnS2C_ObjectSpawn(msg);
                        }
                        else
                        {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_RoomBinder", "Deserialize failed");
                        }
                    });
                    dispatcher.Register(1101, (packet) => {
                        if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.S2C_ObjectDestroy)) is StellarNet.Lite.Shared.Protocol.S2C_ObjectDestroy msg) {
                            c_200.OnS2C_ObjectDestroy(msg);
                        }
                        else
                        {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_RoomBinder", "Deserialize failed");
                        }
                    });
                    dispatcher.Register(1102, (packet) => {
                        if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.S2C_ObjectSync)) is StellarNet.Lite.Shared.Protocol.S2C_ObjectSync msg) {
                            c_200.OnS2C_ObjectSync(msg);
                        }
                        else
                        {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_RoomBinder", "Deserialize failed");
                        }
                    });
                    return true;
            }
            return false;
        }
    }
}
