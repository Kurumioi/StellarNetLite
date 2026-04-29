using System.Collections.Generic;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Client.Core;
using UnityEngine;

namespace StellarNet.Lite.Client.Components.Views
{
    /// <summary>
    /// 动画同步表现组件。
    /// </summary>
    [RequireComponent(typeof(NetIdentity))]
    public class NetAnimatorView : MonoBehaviour
    {
        /// <summary>
        /// 当前对象使用的 Animator。
        /// </summary>
        [Header("动画同步配置")]
        public Animator TargetAnimator;

        /// <summary>
        /// 浮点参数平滑时间。
        /// </summary>
        public float ParamSmoothTime = 0.05f;

        /// <summary>
        /// 动画切换淡入时间。
        /// </summary>
        public float AnimCrossFadeTime = 0.15f;

        /// <summary>
        /// 是否启用防滑冰补偿。
        /// </summary>
        [Header("防滑冰补偿 (通用状态机矩阵)")]
        public bool EnableAntiIceSkating = true;

        /// <summary>
        /// 防滑冰目标状态列表。
        /// </summary>
        public List<string> AntiIceTargetStates = new List<string> { "Idle" };

        /// <summary>
        /// 防滑冰来源状态列表。
        /// </summary>
        public List<string> AntiIceSourceStates = new List<string> { "Walk" };

        /// <summary>
        /// 第一个浮点参数名称。
        /// </summary>
        [Header("BlendTree 参数映射矩阵")]
        public string FloatParam1Name = "";

        /// <summary>
        /// 第二个浮点参数名称。
        /// </summary>
        public string FloatParam2Name = "";

        /// <summary>
        /// 第三个浮点参数名称。
        /// </summary>
        public string FloatParam3Name = "";

        /// <summary>
        /// 服务端逻辑动画状态名列表。
        /// 服务端使用纯 C# 稳定哈希，客户端在这里把逻辑哈希映射回 Animator 真正的状态 Hash。
        /// </summary>
        [Header("服务端逻辑状态映射")]
        public List<string> SyncedStateNames = new List<string> { "Idle", "Walk", "Wave", "Dance" };

        /// <summary>
        /// 当前对象是否为本地玩家。
        /// 本地玩家在线态下使用本地动画驱动，不再依赖网络回写。
        /// </summary>
        public bool IsLocalPlayer { get; set; }

        /// <summary>
        /// 当前实体身份组件。
        /// </summary>
        private NetIdentity _identity;

        /// <summary>
        /// 上一次应用的动画状态 Hash。
        /// </summary>
        private int _lastAnimStateHash;

        /// <summary>
        /// 防滑冰目标状态 Hash 集合。
        /// </summary>
        private readonly HashSet<int> _antiIceTargetHashes = new HashSet<int>();

        /// <summary>
        /// 防滑冰来源状态 Hash 集合。
        /// </summary>
        private readonly HashSet<int> _antiIceSourceHashes = new HashSet<int>();

        /// <summary>
        /// 服务端逻辑状态 Hash 到 Animator 状态 Hash 的映射表。
        /// </summary>
        private readonly Dictionary<int, int> _serverToAnimatorStateHashes = new Dictionary<int, int>();

        /// <summary>
        /// 已经提示过的未知逻辑状态 Hash 集合。
        /// </summary>
        private readonly HashSet<int> _unknownSyncedStateHashes = new HashSet<int>();

        /// <summary>
        /// 映射表和参数 Hash 是否已经完成初始化。
        /// </summary>
        private bool _isRuntimeInitialized;

        /// <summary>
        /// 三个浮点参数对应的 Animator Hash。
        /// </summary>
        private int _param1Hash;
        private int _param2Hash;
        private int _param3Hash;

        /// <summary>
        /// 当前本地缓存的浮点参数值。
        /// </summary>
        private float _currentParam1;
        private float _currentParam2;
        private float _currentParam3;

        /// <summary>
        /// 三个浮点参数的平滑速度缓存。
        /// </summary>
        private float _param1Vel;
        private float _param2Vel;
        private float _param3Vel;

        /// <summary>
        /// 初始化引用。
        /// </summary>
        private void Awake()
        {
            _identity = GetComponent<NetIdentity>();
            if (TargetAnimator == null)
            {
                TargetAnimator = GetComponentInChildren<Animator>();
            }

            EnsureRuntimeInitialized();
        }

        /// <summary>
        /// 预计算状态和参数 Hash。
        /// </summary>
        private void Start()
        {
            EnsureRuntimeInitialized();
        }

        /// <summary>
        /// 立即应用初始动画状态。
        /// </summary>
        public void HardSetInitialState(int animHash, float normalizedTime, float p1, float p2, float p3)
        {
            EnsureRuntimeInitialized();
            int resolvedAnimHash = ResolveAnimatorStateHash(animHash);
            _lastAnimStateHash = resolvedAnimHash;
            _currentParam1 = p1;
            _currentParam2 = p2;
            _currentParam3 = p3;

            if (TargetAnimator != null)
            {
                if (resolvedAnimHash != 0)
                {
                    TargetAnimator.Play(resolvedAnimHash, 0, normalizedTime);
                }

                if (_param1Hash != 0)
                {
                    TargetAnimator.SetFloat(_param1Hash, p1);
                }

                if (_param2Hash != 0)
                {
                    TargetAnimator.SetFloat(_param2Hash, p2);
                }

                if (_param3Hash != 0)
                {
                    TargetAnimator.SetFloat(_param3Hash, p3);
                }
            }
        }

        /// <summary>
        /// 按帧刷新动画同步。
        /// </summary>
        private void Update()
        {
            if (_identity == null || _identity.SyncService == null || TargetAnimator == null)
            {
                return;
            }

            if (IsLocalPlayer && NetClient.State == ClientAppState.OnlineRoom)
            {
                return;
            }

            if (!_identity.SyncService.TryGetAnimatorData(_identity.NetId, out var syncData))
            {
                return;
            }

            ProcessAnimatorSync(ref syncData);
        }

        /// <summary>
        /// 处理一帧动画同步数据。
        /// </summary>
        private void ProcessAnimatorSync(ref PredictedAnimatorData syncData)
        {
            EnsureRuntimeInitialized();
            int targetHash = ResolveAnimatorStateHash(syncData.AnimStateHash);

            if (EnableAntiIceSkating)
            {
                // 目标状态满足条件时，按位移结果决定是否暂缓切入 Idle。
                if (_antiIceTargetHashes.Contains(targetHash) && _antiIceSourceHashes.Contains(_lastAnimStateHash))
                {
                    if (_identity.SyncService.TryGetTransformData(_identity.NetId, out var transData))
                    {
                        if (transData.Velocity.sqrMagnitude > 0.02f || Vector3.Distance(transform.position, transData.Position) > 0.05f)
                        {
                            targetHash = _lastAnimStateHash;
                        }
                    }
                }
            }

            bool needTransition = false;
            if (targetHash != 0 && targetHash != _lastAnimStateHash)
            {
                needTransition = true;
            }
            else if (targetHash != 0 && !TargetAnimator.IsInTransition(0))
            {
                var stateInfo = TargetAnimator.GetCurrentAnimatorStateInfo(0);
                if (stateInfo.shortNameHash != targetHash)
                {
                    needTransition = true;
                }
            }

            if (needTransition)
            {
                _lastAnimStateHash = targetHash;
                float compensatedTime = _antiIceTargetHashes.Contains(targetHash) ? 0f : syncData.AnimNormalizedTime + syncData.ServerTimeDelta;
                TargetAnimator.CrossFadeInFixedTime(targetHash, AnimCrossFadeTime, 0, compensatedTime);
            }

            if (syncData.PlaybackSpeed > 0f)
            {
                if (syncData.PlaybackSpeed > 5f)
                {
                    if (_param1Hash != 0)
                    {
                        TargetAnimator.SetFloat(_param1Hash, syncData.FloatParam1);
                    }

                    if (_param2Hash != 0)
                    {
                        TargetAnimator.SetFloat(_param2Hash, syncData.FloatParam2);
                    }

                    if (_param3Hash != 0)
                    {
                        TargetAnimator.SetFloat(_param3Hash, syncData.FloatParam3);
                    }

                    _currentParam1 = syncData.FloatParam1;
                    _currentParam2 = syncData.FloatParam2;
                    _currentParam3 = syncData.FloatParam3;
                }
                else
                {
                    float effectiveParamSmooth = ParamSmoothTime / syncData.PlaybackSpeed;
                    if (_param1Hash != 0)
                    {
                        _currentParam1 = Mathf.SmoothDamp(_currentParam1, syncData.FloatParam1, ref _param1Vel, effectiveParamSmooth);
                        TargetAnimator.SetFloat(_param1Hash, _currentParam1);
                    }

                    if (_param2Hash != 0)
                    {
                        _currentParam2 = Mathf.SmoothDamp(_currentParam2, syncData.FloatParam2, ref _param2Vel, effectiveParamSmooth);
                        TargetAnimator.SetFloat(_param2Hash, _currentParam2);
                    }

                    if (_param3Hash != 0)
                    {
                        _currentParam3 = Mathf.SmoothDamp(_currentParam3, syncData.FloatParam3, ref _param3Vel, effectiveParamSmooth);
                        TargetAnimator.SetFloat(_param3Hash, _currentParam3);
                    }
                }
            }

            float baseSpeed = syncData.PlaybackSpeed;
            if (_identity.SyncService.TryGetTransformData(_identity.NetId, out var tData))
            {
                // 位移落后较大时，适度提高动画播放速度追赶视觉节奏。
                float distanceToTarget = Vector3.Distance(transform.position, tData.Position);
                if (distanceToTarget > 1.5f && distanceToTarget <= 3.0f && baseSpeed > 0f)
                {
                    baseSpeed *= 1.2f;
                }
            }

            TargetAnimator.speed = Mathf.Clamp(baseSpeed, 0f, 3f);
        }

        private void BuildSyncedStateHashMap()
        {
            _serverToAnimatorStateHashes.Clear();
            RegisterStateNames(SyncedStateNames);
            RegisterStateNames(AntiIceTargetStates);
            RegisterStateNames(AntiIceSourceStates);
        }

        private void EnsureRuntimeInitialized()
        {
            if (_isRuntimeInitialized)
            {
                return;
            }

            _antiIceTargetHashes.Clear();
            foreach (var state in AntiIceTargetStates)
            {
                if (!string.IsNullOrEmpty(state))
                {
                    _antiIceTargetHashes.Add(Animator.StringToHash(state));
                }
            }

            _antiIceSourceHashes.Clear();
            foreach (var state in AntiIceSourceStates)
            {
                if (!string.IsNullOrEmpty(state))
                {
                    _antiIceSourceHashes.Add(Animator.StringToHash(state));
                }
            }

            BuildSyncedStateHashMap();

            _param1Hash = string.IsNullOrEmpty(FloatParam1Name) ? 0 : Animator.StringToHash(FloatParam1Name);
            _param2Hash = string.IsNullOrEmpty(FloatParam2Name) ? 0 : Animator.StringToHash(FloatParam2Name);
            _param3Hash = string.IsNullOrEmpty(FloatParam3Name) ? 0 : Animator.StringToHash(FloatParam3Name);
            _isRuntimeInitialized = true;
        }

        private void RegisterStateNames(List<string> stateNames)
        {
            if (stateNames == null)
            {
                return;
            }

            for (int i = 0; i < stateNames.Count; i++)
            {
                string stateName = stateNames[i];
                if (string.IsNullOrWhiteSpace(stateName))
                {
                    continue;
                }

                string safeStateName = stateName.Trim();
                int serverHash = GetStableStringHash(safeStateName);
                int animatorHash = Animator.StringToHash(safeStateName);
                _serverToAnimatorStateHashes[serverHash] = animatorHash;
            }
        }

        private int ResolveAnimatorStateHash(int serverOrAnimatorHash)
        {
            if (serverOrAnimatorHash == 0)
            {
                return 0;
            }

            if (TargetAnimator != null && TargetAnimator.HasState(0, serverOrAnimatorHash))
            {
                return serverOrAnimatorHash;
            }

            if (_serverToAnimatorStateHashes.TryGetValue(serverOrAnimatorHash, out int animatorHash))
            {
                return animatorHash;
            }

            if (_unknownSyncedStateHashes.Add(serverOrAnimatorHash))
            {
                Debug.LogWarning($"[NetAnimatorView] 未找到服务端逻辑状态映射，AnimStateHash:{serverOrAnimatorHash}，Object:{name}");
            }

            return 0;
        }

        private static int GetStableStringHash(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }

            unchecked
            {
                int hash = 23;
                for (int i = 0; i < value.Length; i++)
                {
                    hash = hash * 31 + value[i];
                }

                return hash;
            }
        }
    }
}
