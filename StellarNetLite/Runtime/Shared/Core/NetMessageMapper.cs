using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace StellarNet.Lite.Shared.Core
{
    /// <summary>
    /// 网络消息元数据映射器。
    /// 职责：在程序启动时扫描并缓存所有携带 [NetMsg] 特性的类型。
    /// 架构意图：为“强类型统一发送器”提供支撑，使业务层发包时只需传入对象，由底层自动解析 MsgId 和 Scope，彻底隔离底层路由字段。
    /// </summary>
    public static class NetMessageMapper
    {
        private static readonly Dictionary<Type, NetMsgAttribute> _typeToMetaCache =
            new Dictionary<Type, NetMsgAttribute>();

        private static bool _isInitialized = false;

        public static void Initialize()
        {
            if (_isInitialized) return;

            _typeToMetaCache.Clear();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                if (assembly.FullName.StartsWith("System") || assembly.FullName.StartsWith("UnityEngine") ||
                    assembly.FullName.StartsWith("UnityEditor"))
                {
                    continue;
                }

                try
                {
                    Type[] types = assembly.GetTypes();
                    foreach (var type in types)
                    {
                        var attr = type.GetCustomAttribute<NetMsgAttribute>();
                        if (attr != null)
                        {
                            if (_typeToMetaCache.ContainsKey(type))
                            {
                                Debug.LogError($"[NetMessageMapper] 致命错误: 发现重复的协议类型 {type.Name}，请检查代码！");
                                continue;
                            }

                            _typeToMetaCache[type] = attr;
                        }
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                }
            }

            _isInitialized = true;
            Debug.Log($"[NetMessageMapper] 协议元数据扫描完毕，共缓存 {_typeToMetaCache.Count} 个协议。");
        }

        public static bool TryGetMeta(Type msgType, out NetMsgAttribute meta)
        {
            if (!_isInitialized)
            {
                Debug.LogError("[NetMessageMapper] 尚未初始化，请先调用 Initialize()！");
                meta = null;
                return false;
            }

            if (msgType == null)
            {
                Debug.LogError("[NetMessageMapper] 查询失败: 传入的消息类型为空。");
                meta = null;
                return false;
            }

            if (!_typeToMetaCache.TryGetValue(msgType, out meta))
            {
                Debug.LogError($"[NetMessageMapper] 查询失败: 类型 {msgType.Name} 缺失 [NetMsg] 特性，无法进行强类型发包。");
                return false;
            }

            return true;
        }
    }
}