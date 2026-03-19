using System;
using System.Collections.Generic;

namespace StellarNet.Lite.Shared.Core
{
    public enum NetScope : byte
    {
        Global = 0,
        Room = 1
    }

    public enum NetDir : byte
    {
        C2S = 0,
        S2C = 1
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class NetMsgAttribute : Attribute
    {
        public int Id { get; }
        public NetScope Scope { get; }
        public NetDir Dir { get; }

        public NetMsgAttribute(int id, NetScope scope, NetDir dir)
        {
            Id = id;
            Scope = scope;
            Dir = dir;
        }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class NetHandlerAttribute : Attribute
    {
        public NetHandlerAttribute()
        {
        }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class RoomComponentAttribute : Attribute
    {
        public int Id { get; }
        public string Name { get; }
        public string DisplayName { get; }

        public RoomComponentAttribute(int id, string name, string displayName = "")
        {
            Id = id;
            Name = name;
            DisplayName = string.IsNullOrEmpty(displayName) ? name : displayName;
        }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class ServerModuleAttribute : Attribute
    {
        public string Name { get; }
        public string DisplayName { get; }

        public ServerModuleAttribute(string name, string displayName = "")
        {
            Name = name;
            DisplayName = string.IsNullOrEmpty(displayName) ? name : displayName;
        }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class ClientModuleAttribute : Attribute
    {
        public string Name { get; }
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
        public int Id;
        public string Name;
        public string DisplayName;
    }

    public struct GlobalModuleMeta
    {
        public string Name;
        public string DisplayName;
    }
}