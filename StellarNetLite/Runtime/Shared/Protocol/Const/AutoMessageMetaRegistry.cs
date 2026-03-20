// ========================================================
// 自动生成的协议元数据静态注册聚合表。
// 请勿手动修改！由 LiteProtocolScanner 自动生成。
// ========================================================
using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Generated.Protocol.Meta;

namespace StellarNet.Lite.Shared.Protocol
{
    public static class AutoMessageMetaRegistry
    {
        public static readonly Dictionary<Type, NetMessageMeta> TypeToMeta = BuildTypeToMeta();
        public static readonly Dictionary<int, Type> MsgIdToType = BuildMsgIdToType();

        private static Dictionary<Type, NetMessageMeta> BuildTypeToMeta()
        {
            var result = new Dictionary<Type, NetMessageMeta>();
            Generated_Game_MessageMeta.AppendTypeToMeta(result);
            Generated_Lite_MessageMeta.AppendTypeToMeta(result);
            return result;
        }

        private static Dictionary<int, Type> BuildMsgIdToType()
        {
            var result = new Dictionary<int, Type>();
            Generated_Game_MessageMeta.AppendMsgIdToType(result);
            Generated_Lite_MessageMeta.AppendMsgIdToType(result);
            return result;
        }
    }
}
