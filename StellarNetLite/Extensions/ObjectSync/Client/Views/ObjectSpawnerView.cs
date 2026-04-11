using System.Collections.Generic;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.ObjectSync;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;

namespace StellarNet.Lite.Client.Components.Views
{
    /// <summary>
    /// 对象生成与销毁视图。
    /// </summary>
    public class ObjectSpawnerView : MonoBehaviour
    {
        // 当前绑定的客户端房间。
        private ClientRoom _room;

        // 当前房间的对象同步组件。
        private ClientObjectSyncComponent _syncService;

        // PrefabHash -> 预制体资源。
        private readonly Dictionary<int, GameObject> _prefabMap = new Dictionary<int, GameObject>();

        // NetId -> 已生成的场景对象。
        private readonly Dictionary<int, GameObject> _spawnedObjects = new Dictionary<int, GameObject>();

        // 当前是否已完成初始化。
        private bool _isInitialized;

        /// <summary>
        /// 当前是否已完成初始化。
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
                _syncService = null;
                _isInitialized = false;
                return false;
            }

            _room.NetEventSystem.Register<Local_ObjectSpawned>(OnLocalObjectSpawned);
            _room.NetEventSystem.Register<Local_ObjectDestroyed>(OnLocalObjectDestroyed);

            _isInitialized = true;
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

            _room = null;
            _syncService = null;
            _isInitialized = false;

            foreach (KeyValuePair<int, GameObject> kvp in _spawnedObjects)
            {
                if (kvp.Value != null)
                {
                    Destroy(kvp.Value);
                }
            }

            _spawnedObjects.Clear();
            NetLogger.LogInfo("ObjectSpawnerView", "清理完成", roomId, extraContext: $"Object:{name}");
        }

        /// <summary>
        /// 销毁时清理所有缓存。
        /// </summary>
        private void OnDestroy()
        {
            Clear();
            _prefabMap.Clear();
        }

        /// <summary>
        /// 获取指定 NetId 的已生成对象。
        /// </summary>
        public GameObject GetSpawnedObject(int netId)
        {
            _spawnedObjects.TryGetValue(netId, out GameObject obj);
            return obj;
        }

        /// <summary>
        /// 获取或加载指定 PrefabHash 的预制体。
        /// </summary>
        private GameObject GetOrLoadPrefab(int prefabHash)
        {
            if (_prefabMap.TryGetValue(prefabHash, out GameObject cachedPrefab))
            {
                return cachedPrefab;
            }

            if (!NetPrefabConsts.HashToPathMap.TryGetValue(prefabHash, out string resPath))
            {
                NetLogger.LogError("ObjectSpawnerView", $"加载失败: 未知 PrefabHash:{prefabHash}", extraContext: $"Object:{name}");
                return null;
            }

            GameObject loadedPrefab = Resources.Load<GameObject>(resPath);
            if (loadedPrefab == null)
            {
                NetLogger.LogError("ObjectSpawnerView", $"加载失败: Resources/{resPath} 不存在", extraContext: $"Object:{name}");
                return null;
            }

            _prefabMap.Add(prefabHash, loadedPrefab);
            return loadedPrefab;
        }

        /// <summary>
        /// 处理本地对象生成事件。
        /// </summary>
        private void OnLocalObjectSpawned(Local_ObjectSpawned evt)
        {
            ObjectSpawnState state = evt.State;
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

            if (_spawnedObjects.ContainsKey(state.NetId))
            {
                NetLogger.LogWarning(
                    "ObjectSpawnerView",
                    $"生成跳过: NetId 已存在, NetId:{state.NetId}, PrefabHash:{state.PrefabHash}",
                    extraContext: $"Object:{name}");
                return;
            }

            GameObject prefab = GetOrLoadPrefab(state.PrefabHash);
            if (prefab == null)
            {
                return;
            }

            Vector3 spawnPos = new Vector3(state.PosX, state.PosY, state.PosZ);
            Quaternion spawnRot = Quaternion.Euler(state.RotX, state.RotY, state.RotZ);
            Vector3 spawnScale = new Vector3(
                Mathf.Approximately(state.ScaleX, 0f) ? 1f : state.ScaleX,
                Mathf.Approximately(state.ScaleY, 0f) ? 1f : state.ScaleY,
                Mathf.Approximately(state.ScaleZ, 0f) ? 1f : state.ScaleZ);

            GameObject instance = Instantiate(prefab, spawnPos, spawnRot);
            instance.transform.localScale = spawnScale;

            NetIdentity identity = instance.GetComponent<NetIdentity>();
            if (identity == null)
            {
                identity = instance.AddComponent<NetIdentity>();
            }

            identity.Init(state.NetId, _syncService);

            if ((state.Mask & (byte)EntitySyncMask.Transform) != 0)
            {
                NetTransformView transView = instance.GetComponent<NetTransformView>();
                if (transView == null)
                {
                    transView = instance.AddComponent<NetTransformView>();
                }

                transView.HardSetInitialState(spawnPos, spawnRot, spawnScale);

                // 仅在实体拥有者等于当前会话时标记为本地玩家。
                transView.IsLocalPlayer = NetClient.Session != null && state.OwnerSessionId == NetClient.Session.SessionId;
            }

            if ((state.Mask & (byte)EntitySyncMask.Animator) != 0)
            {
                NetAnimatorView animView = instance.GetComponent<NetAnimatorView>();
                if (animView == null)
                {
                    animView = instance.AddComponent<NetAnimatorView>();
                }

                animView.HardSetInitialState(
                    state.AnimStateHash,
                    state.AnimNormalizedTime,
                    state.FloatParam1,
                    state.FloatParam2,
                    state.FloatParam3);
            }

            _spawnedObjects.Add(state.NetId, instance);
        }

        /// <summary>
        /// 处理本地对象销毁事件。
        /// </summary>
        private void OnLocalObjectDestroyed(Local_ObjectDestroyed evt)
        {
            if (!_isInitialized)
            {
                return;
            }

            if (!_spawnedObjects.TryGetValue(evt.NetId, out GameObject instance))
            {
                NetLogger.LogWarning("ObjectSpawnerView", $"销毁跳过: 找不到实例, NetId:{evt.NetId}", extraContext: $"Object:{name}");
                return;
            }

            _spawnedObjects.Remove(evt.NetId);
            if (instance != null)
            {
                Destroy(instance);
            }
        }
    }
}
