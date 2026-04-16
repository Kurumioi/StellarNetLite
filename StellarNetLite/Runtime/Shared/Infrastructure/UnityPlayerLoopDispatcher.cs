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
        private sealed class RecurringCallback
        {
            public int Id;
            public Action Callback;
        }

        private struct PlayerLoopMarker
        {
        }

        private static readonly ConcurrentQueue<Action> PendingActions = new ConcurrentQueue<Action>();
        private static readonly List<RecurringCallback> RecurringCallbacks = new List<RecurringCallback>();
        private static readonly object RecurringLock = new object();

        private static int _mainThreadId = -1;
        private static int _nextRecurringId = 1;
        private static bool _isInstalled;

        public static bool IsMainThread => _mainThreadId >= 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId;

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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallBeforeSceneLoad()
        {
            EnsureInstalled();
        }

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

        private static void Run()
        {
            RunRecurringCallbacks();
            DrainPendingActions();
        }

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
