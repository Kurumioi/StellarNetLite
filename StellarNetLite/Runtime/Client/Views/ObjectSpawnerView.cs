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
    /// 核心重构：引入首帧旁路预测 (Bypass Interpolation) 机制，确保重连或中途加入时模型直接定格在正确状态，杜绝从原点飞出的穿模现象。
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

            // 1. 提取绝对空间数据
            Vector3 spawnPos = new Vector3(evt.PosX, evt.PosY, evt.PosZ);
            Quaternion spawnRot = Quaternion.Euler(evt.RotX, evt.RotY, evt.RotZ);
            Vector3 spawnScale = new Vector3(evt.ScaleX, evt.ScaleY, evt.ScaleZ);

            // 2. 实例化并直接硬设 (Hard Set) 绝对坐标与旋转，剥夺平滑组件的首帧控制权
            GameObject instance = Instantiate(prefab, spawnPos, spawnRot);
            instance.transform.localScale = spawnScale;

            var presenter = instance.GetComponent<NetTransformPresenter>();
            if (presenter == null)
            {
                presenter = instance.AddComponent<NetTransformPresenter>();
            }

            // 3. 初始化表现层组件
            presenter.Init(evt.NetId, _syncService);

            // 4. 核心防御：强制注入首帧动画与 BlendTree 参数，确保重连瞬间动作定格对齐
            presenter.HardSetInitialState(
                spawnPos, spawnRot, spawnScale,
                evt.AnimStateHash, evt.AnimNormalizedTime,
                evt.FloatParam1, evt.FloatParam2, evt.FloatParam3
            );

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