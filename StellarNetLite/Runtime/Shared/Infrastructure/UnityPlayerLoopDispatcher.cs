using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.LowLevel;

namespace StellarNet.Lite.Shared.Infrastructure
{
    /// <summary>
    /// 在 Unity PlayerLoop 早期阶段执行主线程动作与周期性回调。
    /// 用于把底层通讯桥接从 MonoBehaviour.Update 中抽离出来。
    /// </summary>
    public static class UnityPlayerLoopDispatcher
    {
        /// <summary>
        /// 周期性回调记录。
        /// </summary>
        private sealed class RecurringCallback
        {
            /// <summary>
            /// 回调唯一 Id。
            /// </summary>
            public int Id;

            /// <summary>
            /// 每轮 PlayerLoop 都要执行的回调。
            /// </summary>
            public Action Callback;
        }

        /// <summary>
        /// 插入 PlayerLoop 的标记类型。
        /// </summary>
        private struct PlayerLoopMarker
        {
        }

        /// <summary>
        /// 后台线程投递到主线程的动作队列。
        /// </summary>
        private static readonly ConcurrentQueue<Action> PendingActions = new ConcurrentQueue<Action>();

        /// <summary>
        /// 当前已注册的周期回调集合。
        /// </summary>
        private static readonly List<RecurringCallback> RecurringCallbacks = new List<RecurringCallback>();

        /// <summary>
        /// 周期回调集合的互斥锁。
        /// </summary>
        private static readonly object RecurringLock = new object();

        /// <summary>
        /// Unity 主线程 Id。
        /// </summary>
        private static int _mainThreadId = -1;

        /// <summary>
        /// 周期回调自增 Id。
        /// </summary>
        private static int _nextRecurringId = 1;

        /// <summary>
        /// 是否已安装到 PlayerLoop。
        /// </summary>
        private static bool _isInstalled;

        /// <summary>
        /// 当前线程是否为 Unity 主线程。
        /// </summary>
        public static bool IsMainThread => _mainThreadId >= 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        /// <summary>
        /// 子系统重载时重置本地静态状态。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            while (PendingActions.TryDequeue(out _))
            {
            }

            lock (RecurringLock)
            {
                RecurringCallbacks.Clear();
                _nextRecurringId = 1;
            }

            _mainThreadId = -1;
            _isInstalled = false;
        }

        /// <summary>
        /// 场景加载前自动安装到 PlayerLoop。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallBeforeSceneLoad()
        {
            EnsureInstalled();
        }

        /// <summary>
        /// 确保主线程桥接系统已安装到 PlayerLoop。
        /// </summary>
        public static void EnsureInstalled()
        {
            if (_isInstalled)
            {
                return;
            }

            // 记录 Unity 主线程，后续后台线程投递动作时靠它判断是否需要排队。
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;

            PlayerLoopSystem playerLoop = PlayerLoop.GetCurrentPlayerLoop();
            if (ContainsSystem(playerLoop, typeof(PlayerLoopMarker)))
            {
                _isInstalled = true;
                return;
            }

            var runnerSystem = new PlayerLoopSystem
            {
                type = typeof(PlayerLoopMarker),
                updateDelegate = Run
            };

            // 尽量把桥接执行点插到早期循环，保证网络回调和周期泵早于大部分表现层 Update。
            if (!TryInsertIntoPhase(ref playerLoop, typeof(UnityEngine.PlayerLoop.EarlyUpdate), runnerSystem))
            {
                AppendToRoot(ref playerLoop, runnerSystem);
            }

            PlayerLoop.SetPlayerLoop(playerLoop);
            _isInstalled = true;
        }

        /// <summary>
        /// 主线程直接执行，后台线程则排队到主线程执行。
        /// </summary>
        public static void ExecuteOrPost(Action action)
        {
            if (action == null)
            {
                return;
            }

            if (IsMainThread)
            {
                action();
                return;
            }

            // 后台线程只负责投递，不直接触碰 Unity 上层逻辑。
            PendingActions.Enqueue(action);
        }

        /// <summary>
        /// 注册一个每轮 PlayerLoop 都执行一次的回调。
        /// </summary>
        public static int RegisterRecurring(Action callback)
        {
            if (callback == null)
            {
                return 0;
            }

            // 给需要“每帧/每轮 PlayerLoop 执行一次”的宿主逻辑提供统一挂点。
            int id;
            lock (RecurringLock)
            {
                id = _nextRecurringId++;
                RecurringCallbacks.Add(new RecurringCallback
                {
                    Id = id,
                    Callback = callback
                });
            }

            return id;
        }

        /// <summary>
        /// 注销一个周期回调。
        /// </summary>
        public static void UnregisterRecurring(int id)
        {
            if (id <= 0)
            {
                return;
            }

            lock (RecurringLock)
            {
                for (int i = 0; i < RecurringCallbacks.Count; i++)
                {
                    if (RecurringCallbacks[i].Id != id)
                    {
                        continue;
                    }

                    RecurringCallbacks.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>
        /// PlayerLoop 实际执行入口。
        /// </summary>
        private static void Run()
        {
            RunRecurringCallbacks();
            DrainPendingActions();
        }

        /// <summary>
        /// 执行当前所有周期回调。
        /// </summary>
        private static void RunRecurringCallbacks()
        {
            RecurringCallback[] callbacks;
            lock (RecurringLock)
            {
                callbacks = RecurringCallbacks.ToArray();
            }

            for (int i = 0; i < callbacks.Length; i++)
            {
                Action callback = callbacks[i] != null ? callbacks[i].Callback : null;
                if (callback == null)
                {
                    continue;
                }

                try
                {
                    callback();
                }
                catch (Exception ex)
                {
                    NetLogger.LogError(
                        "UnityPlayerLoopDispatcher",
                        $"周期回调执行失败: {ex.GetType().Name}, {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 执行所有投递到主线程的动作。
        /// </summary>
        private static void DrainPendingActions()
        {
            while (PendingActions.TryDequeue(out Action action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    NetLogger.LogError(
                        "UnityPlayerLoopDispatcher",
                        $"主线程桥接动作执行失败: {ex.GetType().Name}, {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 检查指定系统是否已存在于当前 PlayerLoop 树中。
        /// </summary>
        private static bool ContainsSystem(PlayerLoopSystem system, Type targetType)
        {
            if (system.type == targetType)
            {
                return true;
            }

            PlayerLoopSystem[] subSystems = system.subSystemList;
            if (subSystems == null)
            {
                return false;
            }

            for (int i = 0; i < subSystems.Length; i++)
            {
                if (ContainsSystem(subSystems[i], targetType))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 尝试把桥接系统插入到指定 PlayerLoop 阶段。
        /// </summary>
        private static bool TryInsertIntoPhase(ref PlayerLoopSystem root, Type phaseType, PlayerLoopSystem systemToInsert)
        {
            PlayerLoopSystem[] subSystems = root.subSystemList;
            if (subSystems == null)
            {
                return false;
            }

            for (int i = 0; i < subSystems.Length; i++)
            {
                PlayerLoopSystem child = subSystems[i];
                if (child.type == phaseType)
                {
                    PlayerLoopSystem[] childSubSystems = child.subSystemList ?? Array.Empty<PlayerLoopSystem>();
                    var merged = new PlayerLoopSystem[childSubSystems.Length + 1];
                    merged[0] = systemToInsert;
                    Array.Copy(childSubSystems, 0, merged, 1, childSubSystems.Length);
                    child.subSystemList = merged;
                    subSystems[i] = child;
                    root.subSystemList = subSystems;
                    return true;
                }

                if (TryInsertIntoPhase(ref child, phaseType, systemToInsert))
                {
                    subSystems[i] = child;
                    root.subSystemList = subSystems;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 兜底把桥接系统直接追加到 PlayerLoop 根节点。
        /// </summary>
        private static void AppendToRoot(ref PlayerLoopSystem root, PlayerLoopSystem systemToInsert)
        {
            PlayerLoopSystem[] subSystems = root.subSystemList ?? Array.Empty<PlayerLoopSystem>();
            var merged = new PlayerLoopSystem[subSystems.Length + 1];
            Array.Copy(subSystems, merged, subSystems.Length);
            merged[subSystems.Length] = systemToInsert;
            root.subSystemList = merged;
        }
    }
}
