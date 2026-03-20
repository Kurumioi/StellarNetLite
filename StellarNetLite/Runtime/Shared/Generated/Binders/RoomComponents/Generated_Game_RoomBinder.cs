// ========================================================
// 自动生成的房间组件绑定分片。
// 请勿手动修改！由 LiteProtocolScanner 自动生成。
// ========================================================
using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Client.Core;

namespace StellarNet.Lite.Shared.Generated.Binders.RoomComponents
{
    public static class Generated_Game_RoomBinder
    {
        public static void AppendRoomComponentMeta(List<RoomComponentMeta> target)
        {
            if (target == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Game_RoomBinder", "AppendRoomComponentMeta failed");
                return;
            }
            target.Add(new RoomComponentMeta { Id = 102, Name = "SocialRoom", DisplayName = "简易交友房间" });
        }

        public static void RegisterServerFactory(ServerApp serverApp)
        {
            if (serverApp == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Game_RoomBinder", "RegisterServerFactory failed");
                return;
            }
            ServerRoomFactory.Register(102, () => new StellarNet.Lite.Game.Server.Components.ServerSocialRoomComponent(serverApp));
        }

        public static void RegisterClientFactory(ClientApp clientApp)
        {
            if (clientApp == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Game_RoomBinder", "RegisterClientFactory failed");
                return;
            }
            ClientRoomFactory.Register(102, () => new StellarNet.Lite.Game.Client.Components.ClientSocialRoomComponent(clientApp));
        }

        public static bool TryBindServer(StellarNet.Lite.Server.Core.RoomComponent comp, StellarNet.Lite.Server.Core.RoomDispatcher dispatcher, Func<byte[], int, int, Type, object> deserializeFunc)
        {
            if (comp == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Game_RoomBinder", "TryBindServer failed");
                return false;
            }
            if (dispatcher == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Game_RoomBinder", "TryBindServer failed");
                return false;
            }
            if (deserializeFunc == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Game_RoomBinder", "TryBindServer failed");
                return false;
            }
            switch (comp)
            {
                case StellarNet.Lite.Game.Server.Components.ServerSocialRoomComponent c_102:
                    dispatcher.Register(1301, (session, packet) => {
                        if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Game.Shared.Protocol.C2S_SocialMoveReq)) is StellarNet.Lite.Game.Shared.Protocol.C2S_SocialMoveReq msg) {
                            c_102.OnC2S_SocialMoveReq(session, msg);
                        }
                        else
                        {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Game_RoomBinder", "Deserialize failed");
                        }
                    });
                    dispatcher.Register(1302, (session, packet) => {
                        if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Game.Shared.Protocol.C2S_SocialActionReq)) is StellarNet.Lite.Game.Shared.Protocol.C2S_SocialActionReq msg) {
                            c_102.OnC2S_SocialActionReq(session, msg);
                        }
                        else
                        {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Game_RoomBinder", "Deserialize failed");
                        }
                    });
                    dispatcher.Register(1303, (session, packet) => {
                        if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Game.Shared.Protocol.C2S_SocialBubbleReq)) is StellarNet.Lite.Game.Shared.Protocol.C2S_SocialBubbleReq msg) {
                            c_102.OnC2S_SocialBubbleReq(session, msg);
                        }
                        else
                        {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Game_RoomBinder", "Deserialize failed");
                        }
                    });
                    return true;
            }
            return false;
        }

        public static bool TryBindClient(StellarNet.Lite.Client.Core.ClientRoomComponent comp, StellarNet.Lite.Client.Core.ClientRoomDispatcher dispatcher, Func<byte[], int, int, Type, object> deserializeFunc)
        {
            if (comp == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Game_RoomBinder", "TryBindClient failed");
                return false;
            }
            if (dispatcher == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Game_RoomBinder", "TryBindClient failed");
                return false;
            }
            if (deserializeFunc == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Game_RoomBinder", "TryBindClient failed");
                return false;
            }
            switch (comp)
            {
                case StellarNet.Lite.Game.Client.Components.ClientSocialRoomComponent c_102:
                    dispatcher.Register(1304, (packet) => {
                        if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Game.Shared.Protocol.S2C_SocialBubbleSync)) is StellarNet.Lite.Game.Shared.Protocol.S2C_SocialBubbleSync msg) {
                            c_102.OnS2C_SocialBubbleSync(msg);
                        }
                        else
                        {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Game_RoomBinder", "Deserialize failed");
                        }
                    });
                    return true;
            }
            return false;
        }
    }
}
