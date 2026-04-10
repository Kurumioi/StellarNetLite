using System;
using System.Collections.Generic;

namespace StellarNet.Lite.Shared.Core
{
    /// <summary>
    /// 网络消息作用域。
    /// </summary>
    public enum NetScope : byte
    {
        // 全局消息，不依赖房间上下文。
        Global = 0,
        // 房间消息，必须依附某个 RoomId。
        Room = 1
    }

    /// <summary>
    /// 网络消息方向。
    /// </summary>
    public enum NetDir : byte
    {
        // 客户端发往服务端。
        C2S = 0,
        // 服务端发往客户端。
        S2C = 1
    }

    /// <summary>
    /// 协议消息元信息特性。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class NetMsgAttribute : Attribute
    {
        // 协议唯一 Id。
        public int Id { get; }
        // 作用域。
        public NetScope Scope { get; }
        // 发送方向。
        public NetDir Dir { get; }

        /// <summary>
        /// 创建协议消息元信息特性。
        /// </summary>
        public NetMsgAttribute(int id, NetScope scope, NetDir dir)
        {
            Id = id;
            Scope = scope;
            Dir = dir;
        }
    }

    /// <summary>
    /// 消息处理函数特性。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class NetHandlerAttribute : Attribute
    {
        public NetHandlerAttribute()
        {
        }
    }

    /// <summary>
    /// 房间组件特性。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class RoomComponentAttribute : Attribute
    {
        // 组件 Id。
        public int Id { get; }
        // 代码名。
        public string Name { get; }
        // 展示名。
        public string DisplayName { get; }

        /// <summary>
        /// 创建房间组件特性。
        /// </summary>
        public RoomComponentAttribute(int id, string name, string displayName = "")
        {
            Id = id;
            Name = name;
            DisplayName = string.IsNullOrEmpty(displayName) ? name : displayName;
        }
    }

    /// <summary>
    /// 服务端全局模块特性。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class ServerModuleAttribute : Attribute
    {
        // 模块代码名。
        public string Name { get; }
        // 展示名。
        public string DisplayName { get; }

        /// <summary>
        /// 创建服务端全局模块特性。
        /// </summary>
        public ServerModuleAttribute(string name, string displayName = "")
        {
            Name = name;
            DisplayName = string.IsNullOrEmpty(displayName) ? name : displayName;
        }
    }

    /// <summary>
    /// 客户端全局模块特性。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class ClientModuleAttribute : Attribute
    {
        // 模块代码名。
        public string Name { get; }
        // 展示名。
        public string DisplayName { get; }

        /// <summary>
        /// 创建客户端全局模块特性。
        /// </summary>
        public ClientModuleAttribute(string name, string displayName = "")
        {
            Name = name;
            DisplayName = string.IsNullOrEmpty(displayName) ? name : displayName;
        }
    }

    /// <summary>
    /// 运行时静态协议元数据。
    /// </summary>
    public readonly struct NetMessageMeta
    {
        /// <summary>
        /// 协议 Id。
        /// </summary>
        public readonly int Id;

        /// <summary>
        /// 协议作用域。
        /// </summary>
        public readonly NetScope Scope;

        /// <summary>
        /// 协议方向。
        /// </summary>
        public readonly NetDir Dir;

        /// <summary>
        /// 创建运行时协议元数据。
        /// </summary>
        public NetMessageMeta(int id, NetScope scope, NetDir dir)
        {
            Id = id;
            Scope = scope;
            Dir = dir;
        }
    }

    /// <summary>
    /// 房间组件展示元数据。
    /// </summary>
    public struct RoomComponentMeta
    {
        // 组件 Id。
        public int Id;
        // 组件代码名。
        public string Name;
        // 组件展示名。
        public string DisplayName;
    }

    /// <summary>
    /// 全局模块展示元数据。
    /// </summary>
    public struct GlobalModuleMeta
    {
        // 模块代码名。
        public string Name;
        // 模块展示名。
        public string DisplayName;
    }
}
