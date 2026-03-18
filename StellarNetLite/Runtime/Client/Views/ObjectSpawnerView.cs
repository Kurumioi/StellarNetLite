using System.Collections.Generic;
using UnityEngine;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Client.Components.Views
{
    /// <summary>
    /// 全局网络实体生成视图 (View 层)
    /// 核心重构：引入组件化装配逻辑。根据服务端下发的 Mask，按需挂载 NetTransformView 和 NetAnimatorView。
    /// </summary>
    public class ObjectSpawnerView : MonoBehaviour
    {
        private ClientRoom _room;
        private ClientObjectSyncComponent _syncService;

        private readonly Dictionary<int, GameObject> _prefabMap = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _spawnedObjects = new Dictionary<int, GameObject>();

        public void Init(ClientRoom room)
        {
            if (room == null)
            {
                NetLogger.LogError("ObjectSpawnerView", "初始化失败: 传入的 ClientRoom 为空，无法建立事件监听。");
                return;
            }

            _room = room;
            _syncService = _room.GetComponent<ClientObjectSyncComponent>();

            if (_syncService == null)
            {
                NetLogger.LogError("ObjectSpawnerView", "初始化失败: 无法在 Room 中找到 ClientObjectSyncComponent 服务。");
                return;
            }

            _room.NetEventSystem.Register<Local_ObjectSpawned>(OnLocalObjectSpawned);
            _room.NetEventSystem.Register<Local_ObjectDestroyed>(OnLocalObjectDestroyed);

            NetLogger.LogInfo("ObjectSpawnerView", "网络实体生成视图初始化完毕，开始监听实体生命周期。");
        }

        public void Clear()
        {
            if (_room != null && _room.NetEventSystem != null)
            {
                _room.NetEventSystem.UnRegister<Local_ObjectSpawned>(OnLocalObjectSpawned);
                _room.NetEventSystem.UnRegister<Local_ObjectDestroyed>(OnLocalObjectDestroyed);
            }

            _room = null;
            _syncService = null;

            foreach (var kvp in _spawnedObjects)
            {
                if (kvp.Value != null) Destroy(kvp.Value);
            }

            _spawnedObjects.Clear();
        }

        private void OnDestroy()
        {
            Clear();
            _prefabMap.Clear();
        }

        private GameObject GetOrLoadPrefab(int prefabHash)
        {
            if (_prefabMap.TryGetValue(prefabHash, out GameObject cachedPrefab))
            {
                return cachedPrefab;
            }

            if (NetPrefabConsts.HashToPathMap.TryGetValue(prefabHash, out string resPath))
            {
                GameObject loadedPrefab = Resources.Load<GameObject>(resPath);
                if (loadedPrefab != null)
                {
                    _prefabMap.Add(prefabHash, loadedPrefab);
                    return loadedPrefab;
                }
                else
                {
                    NetLogger.LogError("ObjectSpawnerView", $"加载失败: 无法在 Resources/{resPath} 找到预制体。");
                }
            }
            else
            {
                NetLogger.LogError("ObjectSpawnerView", $"加载失败: 未知的 PrefabHash {prefabHash}，请检查是否已重新生成常量表。");
            }

            return null;
        }

        private void OnLocalObjectSpawned(Local_ObjectSpawned evt)
        {
            if (_spawnedObjects.ContainsKey(evt.NetId))
            {
                NetLogger.LogError("ObjectSpawnerView", $"生成失败: NetId {evt.NetId} 的实体已存在，拒绝重复生成，防止内存泄漏。");
                return;
            }

            GameObject prefab = GetOrLoadPrefab(evt.PrefabHash);
            if (prefab == null) return;

            Vector3 spawnPos = new Vector3(evt.PosX, evt.PosY, evt.PosZ);
            Quaternion spawnRot = Quaternion.Euler(evt.RotX, evt.RotY, evt.RotZ);
            Vector3 spawnScale = new Vector3(evt.ScaleX, evt.ScaleY, evt.ScaleZ);

            GameObject instance = Instantiate(prefab, spawnPos, spawnRot);
            instance.transform.localScale = spawnScale;

            // 1. 挂载并初始化基础身份标识
            var identity = instance.GetComponent<NetIdentity>();
            if (identity == null) identity = instance.AddComponent<NetIdentity>();
            identity.Init(evt.NetId, _syncService);

            // 2. 根据 Mask 按需挂载并初始化表现组件
            if ((evt.Mask & (byte)EntitySyncMask.Transform) != 0)
            {
                var transView = instance.GetComponent<NetTransformView>();
                if (transView == null) transView = instance.AddComponent<NetTransformView>();
                transView.HardSetInitialState(spawnPos, spawnRot, spawnScale);
            }

            if ((evt.Mask & (byte)EntitySyncMask.Animator) != 0)
            {
                var animView = instance.GetComponent<NetAnimatorView>();
                if (animView == null) animView = instance.AddComponent<NetAnimatorView>();
                animView.HardSetInitialState(evt.AnimStateHash, evt.AnimNormalizedTime, evt.FloatParam1, evt.FloatParam2, evt.FloatParam3);
            }

            _spawnedObjects.Add(evt.NetId, instance);
        }

        private void OnLocalObjectDestroyed(Local_ObjectDestroyed evt)
        {
            if (!_spawnedObjects.TryGetValue(evt.NetId, out GameObject instance))
            {
                NetLogger.LogWarning("ObjectSpawnerView", $"销毁失败: 找不到 NetId {evt.NetId} 对应的表现层实例。可能已被意外销毁。");
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