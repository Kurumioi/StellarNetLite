using System.Collections.Generic;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;

namespace StellarNet.Lite.Client.Components.Views
{
    /// <summary>
    /// 全局网络实体生成视图。
    /// </summary>
    public class ObjectSpawnerView : MonoBehaviour
    {
        private ClientRoom _room;
        private ClientObjectSyncComponent _syncService;
        private readonly Dictionary<int, GameObject> _prefabMap = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _spawnedObjects = new Dictionary<int, GameObject>();
        private bool _isInitialized;

        public void Init(ClientRoom room)
        {
            if (room == null)
            {
                NetLogger.LogError("ObjectSpawnerView", $"初始化失败: room 为空, Object:{name}");
                return;
            }

            if (_isInitialized)
            {
                if (_room == room)
                {
                    NetLogger.LogWarning("ObjectSpawnerView", $"重复初始化已忽略: RoomId:{room.RoomId}, Object:{name}");
                    return;
                }

                Clear();
            }

            _room = room;
            _syncService = _room.GetComponent<ClientObjectSyncComponent>();
            if (_syncService == null)
            {
                NetLogger.LogError("ObjectSpawnerView", $"初始化失败: 缺失 ClientObjectSyncComponent, RoomId:{room.RoomId}, Object:{name}");
                _room = null;
                return;
            }

            _room.NetEventSystem.Register<Local_ObjectSpawned>(OnLocalObjectSpawned);
            _room.NetEventSystem.Register<Local_ObjectDestroyed>(OnLocalObjectDestroyed);
            _isInitialized = true;

            NetLogger.LogInfo("ObjectSpawnerView", $"初始化完成，开始监听实体生命周期。RoomId:{room.RoomId}, Object:{name}");
        }

        public void Clear()
        {
            if (_room != null)
            {
                _room.NetEventSystem.UnRegister<Local_ObjectSpawned>(OnLocalObjectSpawned);
                _room.NetEventSystem.UnRegister<Local_ObjectDestroyed>(OnLocalObjectDestroyed);
            }

            _room = null;
            _syncService = null;
            _isInitialized = false;

            foreach (var kvp in _spawnedObjects)
            {
                if (kvp.Value != null)
                {
                    Destroy(kvp.Value);
                }
            }

            _spawnedObjects.Clear();
        }

        private void OnDestroy()
        {
            Clear();
            _prefabMap.Clear();
        }

        public GameObject GetSpawnedObject(int netId)
        {
            _spawnedObjects.TryGetValue(netId, out GameObject obj);
            return obj;
        }

        private GameObject GetOrLoadPrefab(int prefabHash)
        {
            if (_prefabMap.TryGetValue(prefabHash, out GameObject cachedPrefab))
            {
                return cachedPrefab;
            }

            if (!NetPrefabConsts.HashToPathMap.TryGetValue(prefabHash, out string resPath))
            {
                NetLogger.LogError("ObjectSpawnerView", $"加载失败: 未知 PrefabHash:{prefabHash}, Object:{name}");
                return null;
            }

            GameObject loadedPrefab = Resources.Load<GameObject>(resPath);
            if (loadedPrefab == null)
            {
                NetLogger.LogError("ObjectSpawnerView", $"加载失败: Resources/{resPath} 不存在, Object:{name}");
                return null;
            }

            _prefabMap.Add(prefabHash, loadedPrefab);
            return loadedPrefab;
        }

        private void OnLocalObjectSpawned(Local_ObjectSpawned evt)
        {
            if (_syncService == null)
            {
                NetLogger.LogError("ObjectSpawnerView", $"生成失败: _syncService 为空, NetId:{evt.NetId}, Object:{name}");
                return;
            }

            if (_spawnedObjects.ContainsKey(evt.NetId))
            {
                NetLogger.LogError("ObjectSpawnerView", $"生成失败: NetId 已存在, NetId:{evt.NetId}, Object:{name}");
                return;
            }

            GameObject prefab = GetOrLoadPrefab(evt.PrefabHash);
            if (prefab == null)
            {
                return;
            }

            Vector3 spawnPos = new Vector3(evt.PosX, evt.PosY, evt.PosZ);
            Quaternion spawnRot = Quaternion.Euler(evt.RotX, evt.RotY, evt.RotZ);
            Vector3 spawnScale = new Vector3(evt.ScaleX, evt.ScaleY, evt.ScaleZ);

            GameObject instance = Instantiate(prefab, spawnPos, spawnRot);
            instance.transform.localScale = spawnScale;

            NetIdentity identity = instance.GetComponent<NetIdentity>();
            if (identity == null)
            {
                identity = instance.AddComponent<NetIdentity>();
            }

            identity.Init(evt.NetId, _syncService);

            if ((evt.Mask & (byte)EntitySyncMask.Transform) != 0)
            {
                NetTransformView transView = instance.GetComponent<NetTransformView>();
                if (transView == null)
                {
                    transView = instance.AddComponent<NetTransformView>();
                }

                transView.HardSetInitialState(spawnPos, spawnRot, spawnScale);
            }

            if ((evt.Mask & (byte)EntitySyncMask.Animator) != 0)
            {
                NetAnimatorView animView = instance.GetComponent<NetAnimatorView>();
                if (animView == null)
                {
                    animView = instance.AddComponent<NetAnimatorView>();
                }

                animView.HardSetInitialState(evt.AnimStateHash, evt.AnimNormalizedTime, evt.FloatParam1, evt.FloatParam2, evt.FloatParam3);
            }

            _spawnedObjects.Add(evt.NetId, instance);
        }

        private void OnLocalObjectDestroyed(Local_ObjectDestroyed evt)
        {
            if (!_spawnedObjects.TryGetValue(evt.NetId, out GameObject instance))
            {
                NetLogger.LogWarning("ObjectSpawnerView", $"销毁跳过: 找不到 NetId 对应实例, NetId:{evt.NetId}, Object:{name}");
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