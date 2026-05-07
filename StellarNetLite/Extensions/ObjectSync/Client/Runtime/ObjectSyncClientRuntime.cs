using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Client.Components.Views;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.ObjectSync;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;

namespace StellarNet.Lite.Client.Components.Runtime
{
    /// <summary>
    /// 客户端实体视图当前加载状态。
    /// </summary>
    public enum NetEntityLoadState
    {
        None,
        LoadingAsset,
        SpawningView,
        Ready,
        Failed,
        Destroyed
    }

    /// <summary>
    /// 预制体解析结果。
    /// </summary>
    public readonly struct NetPrefabResolveResult
    {
        public bool Success { get; }
        public GameObject Prefab { get; }
        public string ErrorMessage { get; }

        private NetPrefabResolveResult(bool success, GameObject prefab, string errorMessage)
        {
            Success = success;
            Prefab = prefab;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public static NetPrefabResolveResult CreateSuccess(GameObject prefab)
        {
            return new NetPrefabResolveResult(prefab != null, prefab, prefab == null ? "Prefab 为空" : string.Empty);
        }

        public static NetPrefabResolveResult CreateFailure(string errorMessage)
        {
            return new NetPrefabResolveResult(false, null, errorMessage ?? "未知错误");
        }
    }

    /// <summary>
    /// 客户端实体生成上下文。
    /// </summary>
    public readonly struct NetEntitySpawnContext
    {
        public ClientRoom Room { get; }
        public ClientObjectSyncComponent SyncService { get; }
        public ObjectSpawnState State { get; }
        public Transform RootTransform { get; }

        public NetEntitySpawnContext(ClientRoom room, ClientObjectSyncComponent syncService, ObjectSpawnState state, Transform rootTransform)
        {
            Room = room;
            SyncService = syncService;
            State = state;
            RootTransform = rootTransform;
        }
    }

    /// <summary>
    /// 客户端网络预制体解析器。
    /// 负责把 PrefabHash 解析成实际预制体资源。
    /// </summary>
    public interface INetPrefabResolver
    {
        Task<NetPrefabResolveResult> ResolveAsync(int prefabHash, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 客户端实体生成/回收策略。
    /// 负责实例化、对象池接入和销毁行为。
    /// </summary>
    public interface INetSpawnStrategy
    {
        GameObject Spawn(GameObject prefab, in NetEntitySpawnContext context);
        void Despawn(GameObject instance, in NetEntitySpawnContext context);
    }

    /// <summary>
    /// 客户端实体视图绑定策略。
    /// 负责给实例挂接 NetIdentity / NetTransformView / NetAnimatorView 等表现层组件。
    /// </summary>
    public interface INetViewBinder
    {
        void Bind(GameObject instance, in NetEntitySpawnContext context);
    }

    /// <summary>
    /// ObjectSync 客户端运行时策略入口。
    /// 默认仍然使用 Resources 解析 + Instantiate/Destroy。
    /// </summary>
    public static class ObjectSyncClientRuntime
    {
        private static INetPrefabResolver _prefabResolver;
        private static INetSpawnStrategy _spawnStrategy;
        private static INetViewBinder _viewBinder;

        public static INetPrefabResolver PrefabResolver => _prefabResolver ?? (_prefabResolver = new ResourcesNetPrefabResolver());
        public static INetSpawnStrategy SpawnStrategy => _spawnStrategy ?? (_spawnStrategy = new DefaultNetSpawnStrategy());
        public static INetViewBinder ViewBinder => _viewBinder ?? (_viewBinder = new DefaultNetViewBinder());

        public static void Configure(
            INetPrefabResolver prefabResolver = null,
            INetSpawnStrategy spawnStrategy = null,
            INetViewBinder viewBinder = null)
        {
            if (prefabResolver != null)
            {
                _prefabResolver = prefabResolver;
            }

            if (spawnStrategy != null)
            {
                _spawnStrategy = spawnStrategy;
            }

            if (viewBinder != null)
            {
                _viewBinder = viewBinder;
            }
        }

        public static void ResetToDefault()
        {
            _prefabResolver = null;
            _spawnStrategy = null;
            _viewBinder = null;
        }
    }

    /// <summary>
    /// 默认的 Resources 预制体解析器。
    /// 继续兼容 NetPrefabConsts.HashToPathMap。
    /// </summary>
    public sealed class ResourcesNetPrefabResolver : INetPrefabResolver
    {
        private readonly Dictionary<int, GameObject> _cache = new Dictionary<int, GameObject>();

        public async Task<NetPrefabResolveResult> ResolveAsync(int prefabHash, CancellationToken cancellationToken)
        {
            if (prefabHash == 0)
            {
                return NetPrefabResolveResult.CreateFailure("PrefabHash 非法");
            }

            if (_cache.TryGetValue(prefabHash, out GameObject cachedPrefab) && cachedPrefab != null)
            {
                return NetPrefabResolveResult.CreateSuccess(cachedPrefab);
            }

            if (!NetPrefabConsts.HashToPathMap.TryGetValue(prefabHash, out string resPath) || string.IsNullOrEmpty(resPath))
            {
                return NetPrefabResolveResult.CreateFailure($"默认 Resources 解析器未找到 PrefabHash:{prefabHash} 的路径映射");
            }

            ResourceRequest request = Resources.LoadAsync<GameObject>(resPath);
            while (!request.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            cancellationToken.ThrowIfCancellationRequested();

            GameObject loadedPrefab = request.asset as GameObject;
            if (loadedPrefab == null)
            {
                return NetPrefabResolveResult.CreateFailure($"Resources/{resPath} 不存在或不是 GameObject");
            }

            _cache[prefabHash] = loadedPrefab;
            return NetPrefabResolveResult.CreateSuccess(loadedPrefab);
        }
    }

    /// <summary>
    /// 默认的实体实例化策略。
    /// </summary>
    public sealed class DefaultNetSpawnStrategy : INetSpawnStrategy
    {
        public GameObject Spawn(GameObject prefab, in NetEntitySpawnContext context)
        {
            if (prefab == null)
            {
                return null;
            }

            ObjectSpawnState state = context.State;
            Vector3 spawnPos = new Vector3(state.PosX, state.PosY, state.PosZ);
            Quaternion spawnRot = Quaternion.Euler(state.RotX, state.RotY, state.RotZ);
            GameObject instance = UnityEngine.Object.Instantiate(prefab, spawnPos, spawnRot);
            instance.transform.localScale = new Vector3(
                Mathf.Approximately(state.ScaleX, 0f) ? 1f : state.ScaleX,
                Mathf.Approximately(state.ScaleY, 0f) ? 1f : state.ScaleY,
                Mathf.Approximately(state.ScaleZ, 0f) ? 1f : state.ScaleZ);
            return instance;
        }

        public void Despawn(GameObject instance, in NetEntitySpawnContext context)
        {
            if (instance != null)
            {
                UnityEngine.Object.Destroy(instance);
            }
        }
    }

    /// <summary>
    /// 默认的实体视图绑定策略。
    /// </summary>
    public sealed class DefaultNetViewBinder : INetViewBinder
    {
        public void Bind(GameObject instance, in NetEntitySpawnContext context)
        {
            if (instance == null || context.SyncService == null)
            {
                return;
            }

            ObjectSpawnState state = context.State;

            NetIdentity identity = instance.GetComponent<NetIdentity>();
            if (identity == null)
            {
                identity = instance.AddComponent<NetIdentity>();
            }

            identity.Init(state.NetId, context.SyncService);

            if ((state.Mask & (byte)EntitySyncMask.Transform) != 0)
            {
                NetTransformView transView = instance.GetComponent<NetTransformView>();
                if (transView == null)
                {
                    transView = instance.AddComponent<NetTransformView>();
                }

                transView.HardSetInitialState(
                    new Vector3(state.PosX, state.PosY, state.PosZ),
                    Quaternion.Euler(state.RotX, state.RotY, state.RotZ),
                    new Vector3(
                        Mathf.Approximately(state.ScaleX, 0f) ? 1f : state.ScaleX,
                        Mathf.Approximately(state.ScaleY, 0f) ? 1f : state.ScaleY,
                        Mathf.Approximately(state.ScaleZ, 0f) ? 1f : state.ScaleZ));
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
                    state.AnimParams,
                    state.AnimParamCount);
                animView.IsLocalPlayer = NetClient.Session != null && state.OwnerSessionId == NetClient.Session.SessionId;
            }
        }
    }
}
