using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StellarNet.Lite.Client.Components.Runtime;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.ObjectSync;
using UnityEngine;

namespace StellarNet.Lite.Client.Components.Views
{
    /// <summary>
    /// 对象生成与销毁视图。
    /// 负责协调逻辑实体、异步资源加载和最终场景实例化。
    /// </summary>
    public class ObjectSpawnerView : MonoBehaviour
    {
        private sealed class SpawnedEntityEntry
        {
            public ObjectSpawnState LatestState;
            public GameObject Instance;
            public CancellationTokenSource LoadCts;
            public Task LoadTask;
            public NetEntityLoadState LoadState;
        }

        private ClientRoom _room;
        private ClientObjectSyncComponent _syncService;
        private readonly Dictionary<int, SpawnedEntityEntry> _spawnEntries = new Dictionary<int, SpawnedEntityEntry>();
        private bool _isInitialized;

        /// <summary>
        /// 当前是否已经完成初始化。
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// 绑定房间并开始监听对象生命周期。
        /// </summary>
        public bool Init(ClientRoom room)
        {
            if (room == null)
            {
                NetLogger.LogError("ObjectSpawnerView", $"初始化失败: Room 为空, Object:{name}");
                return false;
            }

            if (_isInitialized)
            {
                if (_room == room)
                {
                    NetLogger.LogWarning("ObjectSpawnerView", $"初始化跳过: 已绑定当前房间, Object:{name}", room.RoomId);
                    return true;
                }

                Clear();
            }

            _room = room;
            _syncService = _room.GetComponent<ClientObjectSyncComponent>();
            if (_syncService == null)
            {
                NetLogger.LogError("ObjectSpawnerView", $"初始化失败: 缺失 ClientObjectSyncComponent, Object:{name}", room.RoomId);
                _room = null;
                _isInitialized = false;
                return false;
            }

            _room.NetEventSystem.Register<Local_ObjectSpawned>(OnLocalObjectSpawned);
            _room.NetEventSystem.Register<Local_ObjectDestroyed>(OnLocalObjectDestroyed);

            _isInitialized = true;
            BootstrapExistingEntities();
            NetLogger.LogInfo("ObjectSpawnerView", "初始化完成", room.RoomId, extraContext: $"Object:{name}");
            return true;
        }

        /// <summary>
        /// 清理当前房间绑定和已生成对象。
        /// </summary>
        public void Clear()
        {
            string roomId = _room != null ? _room.RoomId : "-";
            if (_room != null)
            {
                _room.NetEventSystem.UnRegister<Local_ObjectSpawned>(OnLocalObjectSpawned);
                _room.NetEventSystem.UnRegister<Local_ObjectDestroyed>(OnLocalObjectDestroyed);
            }

            List<int> netIds = new List<int>(_spawnEntries.Keys);
            for (int i = 0; i < netIds.Count; i++)
            {
                CleanupEntity(netIds[i], true);
            }

            _spawnEntries.Clear();
            _room = null;
            _syncService = null;
            _isInitialized = false;
            NetLogger.LogInfo("ObjectSpawnerView", "清理完成", roomId, extraContext: $"Object:{name}");
        }

        private void OnDestroy()
        {
            Clear();
        }

        /// <summary>
        /// 获取指定 NetId 的已生成对象。
        /// </summary>
        public GameObject GetSpawnedObject(int netId)
        {
            if (_spawnEntries.TryGetValue(netId, out SpawnedEntityEntry entry))
            {
                return entry.Instance;
            }

            return null;
        }

        /// <summary>
        /// 查询指定实体当前加载状态。
        /// </summary>
        public bool TryGetEntityLoadState(int netId, out NetEntityLoadState loadState)
        {
            if (_spawnEntries.TryGetValue(netId, out SpawnedEntityEntry entry))
            {
                loadState = entry.LoadState;
                return true;
            }

            loadState = NetEntityLoadState.None;
            return false;
        }

        private void BootstrapExistingEntities()
        {
            if (_syncService == null)
            {
                return;
            }

            List<ObjectSpawnState> states = _syncService.GetAllSpawnStates();
            for (int i = 0; i < states.Count; i++)
            {
                TrackOrStartSpawn(states[i]);
            }
        }

        private void OnLocalObjectSpawned(Local_ObjectSpawned evt)
        {
            TrackOrStartSpawn(evt.State);
        }

        private void OnLocalObjectDestroyed(Local_ObjectDestroyed evt)
        {
            if (!_isInitialized)
            {
                return;
            }

            if (!_spawnEntries.ContainsKey(evt.NetId))
            {
                NetLogger.LogWarning("ObjectSpawnerView", $"销毁跳过: 找不到实体记录, NetId:{evt.NetId}", extraContext: $"Object:{name}");
                return;
            }

            CleanupEntity(evt.NetId, true);
        }

        private void TrackOrStartSpawn(ObjectSpawnState state)
        {
            if (!_isInitialized)
            {
                NetLogger.LogError(
                    "ObjectSpawnerView",
                    $"生成失败: 组件未初始化, NetId:{state.NetId}, PrefabHash:{state.PrefabHash}",
                    extraContext: $"Object:{name}");
                return;
            }

            if (_syncService == null)
            {
                NetLogger.LogError("ObjectSpawnerView", $"生成失败: SyncService 为空, NetId:{state.NetId}", extraContext: $"Object:{name}");
                return;
            }

            if (state.NetId <= 0)
            {
                NetLogger.LogError(
                    "ObjectSpawnerView",
                    $"生成失败: NetId 非法, NetId:{state.NetId}, PrefabHash:{state.PrefabHash}",
                    extraContext: $"Object:{name}");
                return;
            }

            if (!_spawnEntries.TryGetValue(state.NetId, out SpawnedEntityEntry entry))
            {
                entry = new SpawnedEntityEntry();
                _spawnEntries.Add(state.NetId, entry);
            }

            entry.LatestState = state;

            if (entry.Instance != null)
            {
                entry.LoadState = NetEntityLoadState.Ready;
                return;
            }

            if (entry.LoadTask != null && !entry.LoadTask.IsCompleted)
            {
                return;
            }

            StartResolveAndSpawn(state.NetId, entry);
        }

        private void StartResolveAndSpawn(int netId, SpawnedEntityEntry entry)
        {
            entry.LoadCts?.Cancel();
            entry.LoadCts?.Dispose();
            entry.LoadCts = new CancellationTokenSource();
            entry.LoadState = NetEntityLoadState.LoadingAsset;
            entry.LoadTask = ResolveAndSpawnAsync(netId, entry, entry.LoadCts.Token);
        }

        private async Task ResolveAndSpawnAsync(int netId, SpawnedEntityEntry entry, CancellationToken cancellationToken)
        {
            try
            {
                INetPrefabResolver resolver = ObjectSyncClientRuntime.PrefabResolver;
                if (resolver == null)
                {
                    entry.LoadState = NetEntityLoadState.Failed;
                    NetLogger.LogError("ObjectSpawnerView", $"生成失败: 未配置 PrefabResolver, NetId:{netId}", extraContext: $"Object:{name}");
                    return;
                }

                NetPrefabResolveResult result = await resolver.ResolveAsync(entry.LatestState.PrefabHash, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (!_isInitialized || _syncService == null)
                {
                    return;
                }

                if (!_spawnEntries.TryGetValue(netId, out SpawnedEntityEntry currentEntry) || currentEntry != entry)
                {
                    return;
                }

                if (!result.Success || result.Prefab == null)
                {
                    entry.LoadState = NetEntityLoadState.Failed;
                    NetLogger.LogError(
                        "ObjectSpawnerView",
                        $"异步加载失败: NetId:{netId}, PrefabHash:{entry.LatestState.PrefabHash}, Reason:{result.ErrorMessage}",
                        _room != null ? _room.RoomId : "-",
                        extraContext: $"Object:{name}");
                    return;
                }

                if (_syncService.TryGetSpawnState(netId, out ObjectSpawnState latestState))
                {
                    entry.LatestState = latestState;
                }

                if (!_spawnEntries.TryGetValue(netId, out currentEntry) || currentEntry != entry)
                {
                    return;
                }

                entry.LoadState = NetEntityLoadState.SpawningView;
                NetEntitySpawnContext context = new NetEntitySpawnContext(_room, _syncService, entry.LatestState, transform);

                INetSpawnStrategy spawnStrategy = ObjectSyncClientRuntime.SpawnStrategy;
                GameObject instance = spawnStrategy != null ? spawnStrategy.Spawn(result.Prefab, context) : null;
                if (instance == null)
                {
                    entry.LoadState = NetEntityLoadState.Failed;
                    NetLogger.LogError(
                        "ObjectSpawnerView",
                        $"实例化失败: NetId:{netId}, PrefabHash:{entry.LatestState.PrefabHash}",
                        _room != null ? _room.RoomId : "-",
                        extraContext: $"Object:{name}");
                    return;
                }

                entry.Instance = instance;

                INetViewBinder binder = ObjectSyncClientRuntime.ViewBinder;
                binder?.Bind(instance, context);
                entry.LoadState = NetEntityLoadState.Ready;
            }
            catch (OperationCanceledException)
            {
                if (_spawnEntries.TryGetValue(netId, out SpawnedEntityEntry currentEntry) && currentEntry == entry)
                {
                    entry.LoadState = NetEntityLoadState.Destroyed;
                }
            }
            catch (Exception ex)
            {
                if (_spawnEntries.TryGetValue(netId, out SpawnedEntityEntry currentEntry) && currentEntry == entry)
                {
                    entry.LoadState = NetEntityLoadState.Failed;
                }

                NetLogger.LogError(
                    "ObjectSpawnerView",
                    $"异步生成异常: NetId:{netId}, Exception:{ex.GetType().Name}, Message:{ex.Message}",
                    _room != null ? _room.RoomId : "-",
                    extraContext: $"Object:{name}");
            }
            finally
            {
                if (_spawnEntries.TryGetValue(netId, out SpawnedEntityEntry currentEntry) && currentEntry == entry)
                {
                    entry.LoadTask = null;
                }
            }
        }

        private void CleanupEntity(int netId, bool removeEntry)
        {
            if (!_spawnEntries.TryGetValue(netId, out SpawnedEntityEntry entry))
            {
                return;
            }

            entry.LoadCts?.Cancel();
            entry.LoadCts?.Dispose();
            entry.LoadCts = null;
            entry.LoadTask = null;

            if (entry.Instance != null)
            {
                ObjectSpawnState currentState = entry.LatestState;
                if (_syncService != null && _syncService.TryGetSpawnState(netId, out ObjectSpawnState latestState))
                {
                    currentState = latestState;
                }

                NetEntitySpawnContext context = new NetEntitySpawnContext(_room, _syncService, currentState, transform);
                INetSpawnStrategy spawnStrategy = ObjectSyncClientRuntime.SpawnStrategy;
                if (spawnStrategy != null)
                {
                    spawnStrategy.Despawn(entry.Instance, context);
                }
                else
                {
                    Destroy(entry.Instance);
                }

                entry.Instance = null;
            }

            entry.LoadState = NetEntityLoadState.Destroyed;

            if (removeEntry)
            {
                _spawnEntries.Remove(netId);
            }
        }
    }
}
