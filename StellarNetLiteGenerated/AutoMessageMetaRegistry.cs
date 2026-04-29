using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    /// <summary>
    /// 编辑器扫描后生成的协议元数据注册表。
    /// </summary>
    public static class AutoMessageMetaRegistry
    {
        /// <summary>
        /// 协议类型到元数据的映射表。
        /// </summary>
        public static readonly Dictionary<Type, NetMessageMeta> TypeToMeta = new Dictionary<Type, NetMessageMeta>();

        /// <summary>
        /// 协议 Id 到协议类型的映射表。
        /// </summary>
        public static readonly Dictionary<int, Type> MsgIdToType = new Dictionary<int, Type>();
    }
}
