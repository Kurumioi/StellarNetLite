using System;
using System.Collections.Generic;
using System.Linq;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Shared.Core
{
    /// <summary>
    /// 运行时协议元数据查询中心。
    /// 只读取 Editor 生成的静态表，不做 Runtime 反射扫描。
    /// </summary>
    public static class NetMessageMapper
    {
        // Type -> 元数据，用于发送时查 MsgId/Scope/Dir。
        private static readonly Dictionary<Type, NetMessageMeta> TypeToMetaCache = new Dictionary<Type, NetMessageMeta>();
        // MsgId -> Type，用于收包时反查协议类型。
        private static readonly Dictionary<int, Type> MsgIdToTypeCache = new Dictionary<int, Type>();
        private static bool _isInitialized;

        public static void Initialize()
        {
            if (_isInitialized)
            {
                return;
            }

            TypeToMetaCache.Clear();
            MsgIdToTypeCache.Clear();

            if (AutoMessageMetaRegistry.TypeToMeta == null || AutoMessageMetaRegistry.MsgIdToType == null)
            {
                throw new Exception("[NetMessageMapper] 致命阻断: 自动生成的协议元数据注册表为空，请先重新生成协议常量表。");
            }

            bool hasFatalError = false;

            // 先导入 TypeToMeta，再校验 MsgIdToType 是否一致。
            foreach (KeyValuePair<Type, NetMessageMeta> kv in AutoMessageMetaRegistry.TypeToMeta)
            {
                Type msgType = kv.Key;
                NetMessageMeta meta = kv.Value;

                if (msgType == null)
                {
                    NetLogger.LogError("NetMessageMapper", "静态注册表存在空 Type，已阻断初始化");
                    hasFatalError = true;
                    continue;
                }

                if (TypeToMetaCache.ContainsKey(msgType))
                {
                    NetLogger.LogError("NetMessageMapper", $"协议静态注册冲突: 重复类型注册, Type:{msgType.FullName}");
                    hasFatalError = true;
                    continue;
                }

                if (MsgIdToTypeCache.TryGetValue(meta.Id, out Type existingType))
                {
                    NetLogger.LogError(
                        "NetMessageMapper",
                        $"协议静态注册冲突: MsgId 重复, MsgId:{meta.Id}, TypeA:{existingType.FullName}, TypeB:{msgType.FullName}");
                    hasFatalError = true;
                    continue;
                }

                TypeToMetaCache.Add(msgType, meta);
                MsgIdToTypeCache.Add(meta.Id, msgType);
            }

            // 二次校验生成表的双向映射一致性。
            foreach (KeyValuePair<int, Type> kv in AutoMessageMetaRegistry.MsgIdToType)
            {
                int msgId = kv.Key;
                Type msgType = kv.Value;

                if (msgType == null)
                {
                    NetLogger.LogError("NetMessageMapper", $"静态注册表存在空 Type, MsgId:{msgId}");
                    hasFatalError = true;
                    continue;
                }

                if (!MsgIdToTypeCache.TryGetValue(msgId, out Type cachedType))
                {
                    NetLogger.LogError("NetMessageMapper", $"静态注册表不一致: MsgIdToType 存在孤立项, MsgId:{msgId}, Type:{msgType.FullName}");
                    hasFatalError = true;
                    continue;
                }

                if (cachedType != msgType)
                {
                    NetLogger.LogError(
                        "NetMessageMapper",
                        $"静态注册表不一致: MsgId 映射不一致, MsgId:{msgId}, TypeA:{cachedType.FullName}, TypeB:{msgType.FullName}");
                    hasFatalError = true;
                }
            }

            if (hasFatalError)
            {
                throw new Exception("[NetMessageMapper] 静态协议注册表存在致命冲突，已强制阻断启动，请查看 Error 日志。");
            }

            _isInitialized = true;
            GenerateIntegrityReport();
        }

        private static void GenerateIntegrityReport()
        {
            int c2sCount = TypeToMetaCache.Values.Count(m => m.Dir == NetDir.C2S);
            int s2cCount = TypeToMetaCache.Values.Count(m => m.Dir == NetDir.S2C);
            int globalCount = TypeToMetaCache.Values.Count(m => m.Scope == NetScope.Global);
            int roomCount = TypeToMetaCache.Values.Count(m => m.Scope == NetScope.Room);

            NetLogger.LogInfo(
                "NetMessageMapper",
                $"协议静态注册完成: Total:{TypeToMetaCache.Count}, C2S:{c2sCount}, S2C:{s2cCount}, Global:{globalCount}, Room:{roomCount}");

            // 核心协议缺失直接阻断启动，避免框架带病运行。
            bool hasLogin = TypeToMetaCache.Values.Any(m => m.Id == 100);
            bool hasCreateRoom = TypeToMetaCache.Values.Any(m => m.Id == 200);
            bool hasJoinRoom = TypeToMetaCache.Values.Any(m => m.Id == 202);

            if (!hasLogin || !hasCreateRoom || !hasJoinRoom)
            {
                throw new Exception("[NetMessageMapper] 致命阻断: 核心协议(Login/CreateRoom/JoinRoom)缺失。");
            }
        }

        public static bool TryGetMeta(Type msgType, out NetMessageMeta meta)
        {
            if (!_isInitialized)
            {
                NetLogger.LogError("NetMessageMapper", $"查询失败: 系统尚未初始化, MsgType:{msgType?.FullName ?? "null"}");
                meta = default;
                return false;
            }

            if (msgType == null)
            {
                NetLogger.LogError("NetMessageMapper", "查询失败: 传入的 msgType 为空");
                meta = default;
                return false;
            }

            // 发送链常用查询：协议类型 -> 静态元数据。
            if (!TypeToMetaCache.TryGetValue(msgType, out meta))
            {
                NetLogger.LogError("NetMessageMapper", $"查询失败: 类型未注册到静态协议表, MsgType:{msgType.FullName}");
                return false;
            }

            return true;
        }

        public static bool TryGetMessageType(int msgId, out Type msgType)
        {
            if (!_isInitialized)
            {
                NetLogger.LogError("NetMessageMapper", $"查询失败: 系统尚未初始化, MsgId:{msgId}");
                msgType = null;
                return false;
            }

            // 收包链常用查询：MsgId -> 协议类型。
            if (!MsgIdToTypeCache.TryGetValue(msgId, out msgType))
            {
                NetLogger.LogError("NetMessageMapper", $"查询失败: MsgId 未注册到静态协议表, MsgId:{msgId}");
                return false;
            }

            return true;
        }
    }
}
