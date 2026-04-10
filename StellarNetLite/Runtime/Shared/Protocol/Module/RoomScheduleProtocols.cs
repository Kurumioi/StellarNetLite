using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    /// <summary>
    /// 建房配置数据。
    /// </summary>
    public sealed class RoomDTO
    {
        /// <summary>
        /// 房间展示名。
        /// </summary>
        public string RoomName;

        /// <summary>
        /// 房间组件清单。
        /// </summary>
        public int[] ComponentIds;

        /// <summary>
        /// 房间最大人数。
        /// </summary>
        public int MaxMembers;

        /// <summary>
        /// 房间密码。
        /// </summary>
        public string Password;

        /// <summary>
        /// 自定义扩展参数。
        /// key: 参数名。
        /// value: 参数值。
        /// </summary>
        public Dictionary<string, string> CustomProperties;
    }

    [NetMsg(200, NetScope.Global, NetDir.C2S)]
    /// <summary>
    /// 创建房间请求。
    /// </summary>
    public sealed class C2S_CreateRoom
    {
        /// <summary>
        /// 建房配置。
        /// </summary>
        public RoomDTO RoomConfig;
    }

    [NetMsg(201, NetScope.Global, NetDir.S2C)]
    /// <summary>
    /// 创建房间结果。
    /// </summary>
    public sealed class S2C_CreateRoomResult
    {
        /// <summary>
        /// 是否创建成功。
        /// </summary>
        public bool Success;

        /// <summary>
        /// 创建后的房间 Id。
        /// </summary>
        public string RoomId;

        /// <summary>
        /// 房间组件清单。
        /// </summary>
        public int[] ComponentIds;

        /// <summary>
        /// 失败原因。
        /// </summary>
        public string Reason;
    }

    [NetMsg(202, NetScope.Global, NetDir.C2S)]
    /// <summary>
    /// 加入房间请求。
    /// </summary>
    public sealed class C2S_JoinRoom
    {
        /// <summary>
        /// 目标房间 Id。
        /// </summary>
        public string RoomId;

        /// <summary>
        /// 房间密码。
        /// </summary>
        public string Password;
    }

    [NetMsg(203, NetScope.Global, NetDir.S2C)]
    /// <summary>
    /// 加入房间结果。
    /// </summary>
    public sealed class S2C_JoinRoomResult
    {
        /// <summary>
        /// 是否加入成功。
        /// </summary>
        public bool Success;

        /// <summary>
        /// 已加入的房间 Id。
        /// </summary>
        public string RoomId;

        /// <summary>
        /// 房间组件清单。
        /// </summary>
        public int[] ComponentIds;

        /// <summary>
        /// 失败原因。
        /// </summary>
        public string Reason;
    }

    [NetMsg(204, NetScope.Global, NetDir.C2S)]
    /// <summary>
    /// 离开房间请求。
    /// </summary>
    public sealed class C2S_LeaveRoom
    {
    }

    [NetMsg(205, NetScope.Global, NetDir.S2C)]
    /// <summary>
    /// 离开房间结果。
    /// </summary>
    public sealed class S2C_LeaveRoomResult
    {
        /// <summary>
        /// 是否离开成功。
        /// </summary>
        public bool Success;
    }

    [NetMsg(206, NetScope.Global, NetDir.C2S)]
    /// <summary>
    /// 房间装配完成确认。
    /// </summary>
    public sealed class C2S_RoomSetupReady
    {
        /// <summary>
        /// 已完成装配的房间 Id。
        /// </summary>
        public string RoomId;
    }

    [NetMsg(207, NetScope.Global, NetDir.C2S)]
    /// <summary>
    /// 指定房间 Id 加入或创建请求。
    /// </summary>
    public sealed class C2S_JoinOrCreateRoom
    {
        /// <summary>
        /// 指定房间 Id。
        /// </summary>
        public string RoomId;

        /// <summary>
        /// 当房间不存在时使用的降级建房配置。
        /// </summary>
        public RoomDTO RoomConfig;
    }
}
