using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    /// <summary>
    /// 空间与动画同步核心状态数据结构。
    /// 设计意图：采用 struct 值类型，配合数组进行高频传输，彻底杜绝高频同步带来的 GC 压力。
    /// </summary>
    public struct ObjectSyncState
    {
        public int NetId;

        // 空间同步数据 (使用平铺的 float 替代 Vector3，确保跨端序列化的高效与纯净)
        public float PosX;
        public float PosY;
        public float PosZ;

        public float VelX;
        public float VelY;
        public float VelZ;

        // 缩放同步数据 (支持运行时的动态体型变化)
        public float ScaleX;
        public float ScaleY;
        public float ScaleZ;

        // 动画同步数据
        public int AnimStateHash;
        public float AnimNormalizedTime;

        // 服务端时间戳，用于客户端执行航位推测 (Dead Reckoning) 与动画时间补偿
        public float ServerTime;
    }

    /// <summary>
    /// 网络实体生成协议 (由服务端权威广播)
    /// </summary>
    [NetMsg(1100, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_ObjectSpawn
    {
        public int NetId;
        public int PrefabHash;

        public float PosX;
        public float PosY;
        public float PosZ;

        public float DirX;
        public float DirY;
        public float DirZ;

        public float ScaleX;
        public float ScaleY;
        public float ScaleZ;

        // 实体归属者。若为空字符串，则表示该实体由服务端 AI 权威控制；若为 SessionId，则表示由对应客户端主控输入
        public string OwnerSessionId;
    }

    /// <summary>
    /// 网络实体销毁协议 (由服务端权威广播)
    /// </summary>
    [NetMsg(1101, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_ObjectDestroy
    {
        public int NetId;
    }

    /// <summary>
    /// 网络实体高频状态同步协议 (由服务端按 TickRate 广播)
    /// </summary>
    [NetMsg(1102, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_ObjectSync
    {
        // 采用数组承载值类型，确保反序列化时的内存连续性与 0GC
        public ObjectSyncState[] States;
    }
}