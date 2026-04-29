using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Client.Core;

namespace StellarNet.Lite.Shared.Binders
{
    /// <summary>
    /// 编辑器扫描后生成的客户端/服务端绑定入口。
    /// 当前文件会在重新生成协议表时被覆盖，不要手工维护业务逻辑。
    /// </summary>
    public static class AutoRegistry
    {
        /// <summary>
        /// 已扫描到的房间组件元数据列表。
        /// </summary>
        public static readonly List<RoomComponentMeta> RoomComponentMetaList = new List<RoomComponentMeta>();

        /// <summary>
        /// 把扫描到的服务端模块注册到 ServerApp。
        /// </summary>
        public static void RegisterServer(ServerApp serverApp, Func<byte[], int, int, Type, object> deserializeFunc)
        {
        }

        /// <summary>
        /// 把扫描到的服务端房间组件处理器绑定到分发器。
        /// </summary>
        public static void BindServerComponent(ServerRoomComponent comp, RoomDispatcher dispatcher, Func<byte[], int, int, Type, object> deserializeFunc)
        {
        }

        /// <summary>
        /// 把扫描到的客户端模块注册到 ClientApp。
        /// </summary>
        public static void RegisterClient(ClientApp clientApp, Func<byte[], int, int, Type, object> deserializeFunc)
        {
        }

        /// <summary>
        /// 把扫描到的客户端房间组件处理器绑定到分发器。
        /// </summary>
        public static void BindClientComponent(ClientRoomComponent comp, ClientRoomDispatcher dispatcher, Func<byte[], int, int, Type, object> deserializeFunc)
        {
        }
    }
}
