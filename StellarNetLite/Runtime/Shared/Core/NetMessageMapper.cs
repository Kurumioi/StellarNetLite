using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

namespace StellarNet.Lite.Shared.Core
{
    public static class NetMessageMapper
    {
        private static readonly Dictionary<Type, NetMsgAttribute> _typeToMetaCache =
            new Dictionary<Type, NetMsgAttribute>();

        private static bool _isInitialized = false;

        public static void Initialize()
        {
            if (_isInitialized) return;
            _typeToMetaCache.Clear();

            bool hasFatalError = false;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                if (assembly.FullName.StartsWith("System") || assembly.FullName.StartsWith("UnityEngine") ||
                    assembly.FullName.StartsWith("UnityEditor") || assembly.FullName.StartsWith("mscorlib"))
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
                                NetLogger.LogError("NetMessageMapper", $"致命错误: 发现重复的协议类型 {type.Name}，请检查代码！");
                                hasFatalError = true;
                                continue;
                            }

                            _typeToMetaCache[type] = attr;
                        }
                    }
                }
                catch (ReflectionTypeLoadException ex)
                {
                    NetLogger.LogError("NetMessageMapper", $"扫描程序集 {assembly.FullName} 时发生 ReflectionTypeLoadException，协议扫描可能不完整！");
                    foreach (var loaderEx in ex.LoaderExceptions)
                    {
                        if (loaderEx != null) NetLogger.LogError("NetMessageMapper", $"LoaderException 明细: {loaderEx.Message}");
                    }

                    hasFatalError = true;
                }
                catch (Exception e)
                {
                    NetLogger.LogError("NetMessageMapper", $"扫描程序集 {assembly.FullName} 时发生未知异常: {e.Message}");
                    hasFatalError = true;
                }
            }

            // 核心修复 P0-8：基础设施初始化失败必须阻断，绝不允许带病启动
            if (hasFatalError)
            {
                throw new Exception("[NetMessageMapper] 协议扫描阶段发生致命异常，已强制阻断服务器/客户端启动，请查看控制台 Error 日志修复冲突！");
            }

            _isInitialized = true;
            GenerateIntegrityReport();
        }

        private static void GenerateIntegrityReport()
        {
            int c2sCount = _typeToMetaCache.Values.Count(m => m.Dir == NetDir.C2S);
            int s2cCount = _typeToMetaCache.Values.Count(m => m.Dir == NetDir.S2C);
            int globalCount = _typeToMetaCache.Values.Count(m => m.Scope == NetScope.Global);
            int roomCount = _typeToMetaCache.Values.Count(m => m.Scope == NetScope.Room);

            NetLogger.LogInfo("NetMessageMapper", $"总协议数: {_typeToMetaCache.Count} | C2S: {c2sCount} | S2C: {s2cCount} | Global: {globalCount} | Room: {roomCount}");

            bool hasLogin = _typeToMetaCache.Values.Any(m => m.Id == 100);
            bool hasCreateRoom = _typeToMetaCache.Values.Any(m => m.Id == 200);
            bool hasJoinRoom = _typeToMetaCache.Values.Any(m => m.Id == 202);

            if (!hasLogin || !hasCreateRoom || !hasJoinRoom)
            {
                // 核心修复 P0-8：核心协议缺失直接抛异常阻断
                throw new Exception("[NetMessageMapper] 致命阻断: 核心调度协议 (Login/CreateRoom/JoinRoom) 缺失！请检查 MsgIdConst 或协议定义文件是否被意外移除。");
            }
        }

        public static bool TryGetMeta(Type msgType, out NetMsgAttribute meta)
        {
            if (!_isInitialized)
            {
                NetLogger.LogError("NetMessageMapper", "尚未初始化，请先调用 Initialize()！");
                meta = null;
                return false;
            }

            if (msgType == null)
            {
                NetLogger.LogError("NetMessageMapper", "查询失败: 传入的消息类型为空。");
                meta = null;
                return false;
            }

            if (!_typeToMetaCache.TryGetValue(msgType, out meta))
            {
                NetLogger.LogError("NetMessageMapper", $"查询失败: 类型 {msgType.Name} 缺失 [NetMsg] 特性，无法进行强类型发包。");
                return false;
            }

            return true;
        }
    }
}