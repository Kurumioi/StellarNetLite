using System.Collections.Generic;
using UnityEngine;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Components.Views
{
    /// <summary>
    /// 全局网络实体生成视图 (View 层)
    /// 职责：监听本地生成/销毁事件，根据 Hash 实例化预制体，并注入 NetTransformPresenter。
    /// 架构意图：将“资源加载与实例化”这一纯表现层逻辑与网络核心逻辑彻底物理隔离。
    /// </summary>
    public class ObjectSpawnerView : MonoBehaviour
    {
        private ClientRoom _room;
        private ClientObjectSyncComponent _syncService;

        // 预制体缓存池：PrefabHash -> GameObject
        private readonly Dictionary<int, GameObject> _prefabMap = new Dictionary<int, GameObject>();

        // 已生成的实例字典：NetId -> GameObject
        private readonly Dictionary<int, GameObject> _spawnedObjects = new Dictionary<int, GameObject>();

        /// <summary>
        /// 由外部启动流程 (如 RoomEntry) 调用并注入当前房间上下文
        /// </summary>
        public void Init(ClientRoom room)
        {
            _room = room;
            if (_room == null)
            {
                NetLogger.LogError("[ObjectSpawnerView]", "初始化失败: 传入的 ClientRoom 为空，无法建立事件监听。");
                return;
            }

            _syncService = _room.GetComponent<ClientObjectSyncComponent>();
            if (_syncService == null)
            {
                NetLogger.LogError("[ObjectSpawnerView]", "初始化失败: 无法在 Room 中找到 ClientObjectSyncComponent 服务。");
                return;
            }

            BuildPrefabMap();

            // 订阅底层抛出的本地表现事件，绝不直接监听网络协议
            _room.NetEventSystem.Register<Local_ObjectSpawned>(OnLocalObjectSpawned);
            _room.NetEventSystem.Register<Local_ObjectDestroyed>(OnLocalObjectDestroyed);

            NetLogger.LogInfo("[ObjectSpawnerView]", "网络实体生成视图初始化完毕，开始监听实体生命周期。");
        }

        /// <summary>
        /// 核心修复：清理方法。在房间切换或回放 Seek 时，必须彻底销毁所有已生成的实体，防止 NetId 冲突。
        /// </summary>
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

        private void BuildPrefabMap()
        {
            _prefabMap.Clear();
            GameObject[] prefabs = Resources.LoadAll<GameObject>("NetPrefabs");
            if (prefabs == null || prefabs.Length == 0)
            {
                NetLogger.LogWarning("[ObjectSpawnerView]", "未在 Resources/NetPrefabs 目录下找到任何预制体，生成器将无法工作。");
                return;
            }

            foreach (var prefab in prefabs)
            {
                string resPath = "NetPrefabs/" + prefab.name;
                int hash = GetStableHash(resPath);
                if (!_prefabMap.ContainsKey(hash))
                {
                    _prefabMap.Add(hash, prefab);
                }
                else
                {
                    NetLogger.LogError("[ObjectSpawnerView]", $"预制体 Hash 冲突: {resPath}，将跳过加载。请检查命名。");
                }
            }
        }

        private int GetStableHash(string input)
        {
            if (string.IsNullOrEmpty(input)) return 0;
            uint hash = 2166136261;
            foreach (char c in input)
            {
                hash ^= c;
                hash *= 16777619;
            }

            return (int)hash;
        }

        private void OnLocalObjectSpawned(Local_ObjectSpawned evt)
        {
            if (_spawnedObjects.ContainsKey(evt.NetId))
            {
                NetLogger.LogError("[ObjectSpawnerView]", $"生成失败: NetId {evt.NetId} 的实体已存在，拒绝重复生成，防止内存泄漏。");
                return;
            }

            if (!_prefabMap.TryGetValue(evt.PrefabHash, out GameObject prefab))
            {
                NetLogger.LogError("[ObjectSpawnerView]", $"生成失败: 无法找到 Hash 为 {evt.PrefabHash} 的预制体。请检查是否已放入 Resources/NetPrefabs 目录并重新生成常量表。");
                return;
            }

            Vector3 spawnPos = new Vector3(evt.PosX, evt.PosY, evt.PosZ);
            Quaternion spawnRot = Quaternion.identity;
            Vector3 dir = new Vector3(evt.DirX, evt.DirY, evt.DirZ);
            if (dir.sqrMagnitude > 0.01f)
            {
                spawnRot = Quaternion.LookRotation(dir);
            }

            GameObject instance = Instantiate(prefab, spawnPos, spawnRot);
            instance.transform.localScale = new Vector3(evt.ScaleX, evt.ScaleY, evt.ScaleZ);

            var presenter = instance.GetComponent<NetTransformPresenter>();
            if (presenter == null)
            {
                presenter = instance.AddComponent<NetTransformPresenter>();
            }

            presenter.Init(evt.NetId, _syncService);
            _spawnedObjects.Add(evt.NetId, instance);
        }

        private void OnLocalObjectDestroyed(Local_ObjectDestroyed evt)
        {
            if (!_spawnedObjects.TryGetValue(evt.NetId, out GameObject instance))
            {
                NetLogger.LogWarning("[ObjectSpawnerView]", $"销毁失败: 找不到 NetId {evt.NetId} 对应的表现层实例。可能已被意外销毁。");
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