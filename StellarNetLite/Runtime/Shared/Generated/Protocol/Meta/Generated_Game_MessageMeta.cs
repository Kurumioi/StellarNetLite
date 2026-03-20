// ========================================================
// 自动生成的分片协议元数据注册表。
// 请勿手动修改！由 LiteProtocolScanner 自动生成。
// ========================================================
using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Generated.Protocol.Meta
{
    public static class Generated_Game_MessageMeta
    {
        public static void AppendTypeToMeta(Dictionary<Type, NetMessageMeta> target)
        {
            if (target == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Game_MessageMeta", "AppendTypeToMeta failed");
                return;
            }
            target[typeof(StellarNet.Lite.Game.Shared.Protocol.C2S_SocialMoveReq)] = new NetMessageMeta(1301, NetScope.Room, NetDir.C2S);
            target[typeof(StellarNet.Lite.Game.Shared.Protocol.C2S_SocialActionReq)] = new NetMessageMeta(1302, NetScope.Room, NetDir.C2S);
            target[typeof(StellarNet.Lite.Game.Shared.Protocol.C2S_SocialBubbleReq)] = new NetMessageMeta(1303, NetScope.Room, NetDir.C2S);
            target[typeof(StellarNet.Lite.Game.Shared.Protocol.S2C_SocialBubbleSync)] = new NetMessageMeta(1304, NetScope.Room, NetDir.S2C);
        }

        public static void AppendMsgIdToType(Dictionary<int, Type> target)
        {
            if (target == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Game_MessageMeta", "AppendMsgIdToType failed");
                return;
            }
            target[1301] = typeof(StellarNet.Lite.Game.Shared.Protocol.C2S_SocialMoveReq);
            target[1302] = typeof(StellarNet.Lite.Game.Shared.Protocol.C2S_SocialActionReq);
            target[1303] = typeof(StellarNet.Lite.Game.Shared.Protocol.C2S_SocialBubbleReq);
            target[1304] = typeof(StellarNet.Lite.Game.Shared.Protocol.S2C_SocialBubbleSync);
        }
    }
}
