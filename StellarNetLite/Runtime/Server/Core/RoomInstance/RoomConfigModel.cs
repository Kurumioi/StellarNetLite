namespace StellarNet.Lite.Server.Core
{
    /// <summary>
    /// 服务端房间纯实例配置模型 (Domain Model)
    /// 职责：仅存在于服务端内存中，用于存储当前房间的各项规则与配置。
    /// 架构约束：绝对禁止将其直接作为网络协议传输，必须通过 Module 进行 DTO 映射。
    /// </summary>
    public sealed class RoomConfigModel
    {
        public string RoomName { get; set; } = "未命名房间";

        public int MaxMembers { get; set; } = 4;

        public string Password { get; set; } = string.Empty;

        public bool IsPrivate => !string.IsNullOrEmpty(Password);

        // 后续开发者可以自由在此扩展服务端专属配置，例如：
        // public int TickRate { get; set; } = 60;
        // public bool AllowSpectators { get; set; } = false;
    }
}