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
        /// 本次房间是否启用录像录制。
        /// 默认 false，仅在客户端明确开启时才录制。
        /// </summary>
        public bool EnableReplayRecording;

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

    /// <summary>
    /// 创建房间请求。
    /// </summary>
    [NetMsg(200, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_CreateRoom
    {
        /// <summary>
        /// 建房配置。
        /// </summary>
        public RoomDTO RoomConfig;
    }

    /// <summary>
    /// 创建房间结果。
    /// </summary>
    [NetMsg(201, NetScope.Global, NetDir.S2C)]
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

    /// <summary>
    /// 加入房间请求。
    /// </summary>
    [NetMsg(202, NetScope.Global, NetDir.C2S)]
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

    /// <summary>
    /// 加入房间结果。
    /// </summary>
    [NetMsg(203, NetScope.Global, NetDir.S2C)]
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

    /// <summary>
    /// 离开房间请求。
    /// </summary>
    [NetMsg(204, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_LeaveRoom
    {
    }

    /// <summary>
    /// 离开房间结果。
    /// </summary>
    [NetMsg(205, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_LeaveRoomResult
    {
        /// <summary>
        /// 是否离开成功。
        /// </summary>
        public bool Success;
    }

    /// <summary>
    /// 挂起当前房间请求。
    /// 当前会话回到大厅，但服务端保留该房间的可恢复状态。
    /// </summary>
    [NetMsg(209, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_DisconnectRoom
    {
    }

    /// <summary>
    /// 挂起当前房间结果。
    /// </summary>
    [NetMsg(217, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_DisconnectRoomResult
    {
        /// <summary>
        /// 是否挂起成功。
        /// </summary>
        public bool Success;

        /// <summary>
        /// 被挂起的房间 Id。
        /// </summary>
        public string RoomId;

        /// <summary>
        /// 失败原因。
        /// </summary>
        public string Reason;
    }

    /// <summary>
    /// 房间装配完成确认。
    /// </summary>
    [NetMsg(206, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_RoomSetupReady
    {
        /// <summary>
        /// 已完成装配的房间 Id。
        /// </summary>
        public string RoomId;
    }

    /// <summary>
    /// 指定房间 Id 加入或创建请求。
    /// </summary>
    [NetMsg(207, NetScope.Global, NetDir.C2S)]
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

    /// <summary>
    /// 房间装配最终确认结果。
    /// </summary>
    [NetMsg(208, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_RoomSetupResult
    {
        /// <summary>
        /// 是否确认成功。
        /// </summary>
        public bool Success;

        /// <summary>
        /// 当前确认对应的房间 Id。
        /// </summary>
        public string RoomId;

        /// <summary>
        /// 失败原因。
        /// </summary>
        public string Reason;
    }
}
