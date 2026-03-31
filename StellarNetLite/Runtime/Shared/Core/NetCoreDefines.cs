using System;
using System.Collections.Generic;

namespace StellarNet.Lite.Shared.Core
{
    // 网络消息作用域。
    public enum NetScope : byte
    {
        // 全局消息，不依赖房间上下文。
        Global = 0,
        // 房间消息，必须依附某个 RoomId。
        Room = 1
    }

    // 网络消息方向。
    public enum NetDir : byte
    {
        // 客户端发往服务端。
        C2S = 0,
        // 服务端发往客户端。
        S2C = 1
    }

    // 标记协议消息的元信息特性。
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class NetMsgAttribute : Attribute
    {
        // 协议唯一 Id。
        public int Id { get; }
        // 作用域。
        public NetScope Scope { get; }
        // 发送方向。
        public NetDir Dir { get; }

        public NetMsgAttribute(int id, NetScope scope, NetDir dir)
        {
            Id = id;
            Scope = scope;
            Dir = dir;
        }
    }

    // 标记消息处理函数。
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class NetHandlerAttribute : Attribute
    {
        public NetHandlerAttribute()
        {
        }
    }

    // 标记房间组件。
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class RoomComponentAttribute : Attribute
    {
        // 组件 Id。
        public int Id { get; }
        // 代码名。
        public string Name { get; }
        // 展示名。
        public string DisplayName { get; }

        public RoomComponentAttribute(int id, string name, string displayName = "")
        {
            Id = id;
            Name = name;
            DisplayName = string.IsNullOrEmpty(displayName) ? name : displayName;
        }
    }

    // 标记服务端全局模块。
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class ServerModuleAttribute : Attribute
    {
        // 模块代码名。
        public string Name { get; }
        // 展示名。
        public string DisplayName { get; }

        public ServerModuleAttribute(string name, string displayName = "")
        {
            Name = name;
            DisplayName = string.IsNullOrEmpty(displayName) ? name : displayName;
        }
    }

    // 标记客户端全局模块。
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class ClientModuleAttribute : Attribute
    {
        // 模块代码名。
        public string Name { get; }
        // 展示名。
        public string DisplayName { get; }

        public ClientModuleAttribute(string name, string displayName = "")
        {
            Name = name;
            DisplayName = string.IsNullOrEmpty(displayName) ? name : displayName;
        }
    }

    /// <summary>
    /// 运行时静态协议元数据。
    /// 我在 Editor 阶段扫描特性后，将协议信息固化为纯值类型结构，避免 Runtime 反射读取 Attribute。
    /// </summary>
    public readonly struct NetMessageMeta
    {
        public readonly int Id;
        public readonly NetScope Scope;
        public readonly NetDir Dir;

        public NetMessageMeta(int id, NetScope scope, NetDir dir)
        {
            Id = id;
            Scope = scope;
            Dir = dir;
        }
    }

    // ========================================================
    // 运行时元数据结构 (用于客户端 UI 展示)
    // ========================================================
    public struct RoomComponentMeta
    {
        // 组件 Id。
        public int Id;
        // 组件代码名。
        public string Name;
        // 组件展示名。
        public string DisplayName;
    }

    public struct GlobalModuleMeta
    {
        // 模块代码名。
        public string Name;
        // 模块展示名。
        public string DisplayName;
    }
}
