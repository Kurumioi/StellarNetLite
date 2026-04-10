using System.Collections.Generic;

namespace StellarNet.Lite.Server.Core
{
    /// <summary>
    /// 服务端房间配置数据模型。
    /// 在房间生命周期内存储静态配置数据，供各业务组件读取。
    /// </summary>
    public sealed class RoomConfigModel
    {
        /// <summary>
        /// 房间展示名称
        /// </summary>
        public string RoomName { get; set; } = "未命名房间";

        /// <summary>
        /// 房间最大人数限制
        /// </summary>
        public int MaxMembers { get; set; } = 4;

        /// <summary>
        /// 房间密码（为空则表示公开房间）
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// 是否为私密房间
        /// </summary>
        public bool IsPrivate => !string.IsNullOrEmpty(Password);

        /// <summary>
        /// 自定义业务属性字典。
        /// 用于承接客户端建房时通过 RoomDTO 传入的透传参数（如 taskId, mapId, difficulty 等）。
        /// 业务组件可直接读取此字典获取玩法配置，实现底层框架与上层玩法的彻底解耦。
        /// </summary>
        public Dictionary<string, string> CustomProperties { get; set; } = new Dictionary<string, string>();
    }
}