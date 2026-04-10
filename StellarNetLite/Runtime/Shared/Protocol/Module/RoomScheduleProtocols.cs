using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    /// <summary>
    /// 房间配置数据传输对象。
    /// 将零散的建房参数高内聚，并提供 CustomProperties 字典供业务层透传拓展参数。
    /// </summary>
    public sealed class RoomDTO
    {
        public string RoomName;
        public int[] ComponentIds;
        public int MaxMembers;
        public string Password;
        public Dictionary<string, string> CustomProperties;
    }

    [NetMsg(200, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_CreateRoom
    {
        /// <summary>
        /// 统一使用 RoomDTO 承载建房参数，支持业务字段透传。
        /// </summary>
        public RoomDTO RoomConfig;
    }

    [NetMsg(201, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_CreateRoomResult
    {
        public bool Success;
        public string RoomId;
        public int[] ComponentIds;
        public string Reason;
    }

    [NetMsg(202, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_JoinRoom
    {
        public string RoomId;
        public string Password;
    }

    [NetMsg(203, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_JoinRoomResult
    {
        public bool Success;
        public string RoomId;
        public int[] ComponentIds;
        public string Reason;
    }

    [NetMsg(204, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_LeaveRoom
    {
    }

    [NetMsg(205, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_LeaveRoomResult
    {
        public bool Success;
    }

    [NetMsg(206, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_RoomSetupReady
    {
        public string RoomId;
    }

    [NetMsg(207, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_JoinOrCreateRoom
    {
        /// <summary>
        /// 底层物理寻址凭据。必须由客户端显式传入。
        /// </summary>
        public string RoomId;

        /// <summary>
        /// 降级建房配置。仅在房间不存在、触发创建逻辑时使用。
        /// </summary>
        public RoomDTO RoomConfig;
    }
}