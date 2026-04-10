using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    public static class AutoMessageMetaRegistry
    {
        public static readonly Dictionary<Type, NetMessageMeta> TypeToMeta = new Dictionary<Type, NetMessageMeta>();
        public static readonly Dictionary<int, Type> MsgIdToType = new Dictionary<int, Type>();
    }
}