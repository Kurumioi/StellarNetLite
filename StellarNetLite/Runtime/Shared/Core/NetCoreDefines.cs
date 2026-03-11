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

    // 房间业务组件元数据特性，增加 DisplayName 用于客户端展示
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

    // 全局业务模块元数据特性，无须 ID，仅用于自动装配与展示
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class GlobalModuleAttribute : Attribute
    {
        public string Name { get; }
        public string DisplayName { get; }

        public GlobalModuleAttribute(string name, string displayName = "")
        {
            Name = name;
            DisplayName = string.IsNullOrEmpty(displayName) ? name : displayName;
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