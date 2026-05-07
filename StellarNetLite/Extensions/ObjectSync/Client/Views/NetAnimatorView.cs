using System;
using System.Collections.Generic;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.ObjectSync;
using UnityEngine;
using UnityEngine.Serialization;

namespace StellarNet.Lite.Client.Components.Views
{
    /// <summary>
    /// One visual mapping entry from a server-side logical state name to an Animator state.
    /// </summary>
    [Serializable]
    public sealed class AnimatorStateSyncEntry
    {
        public bool Enabled = true;
        public string LogicStateName = string.Empty;
        public string AnimatorStateName = string.Empty;
    }

    /// <summary>
    /// One visual mapping entry from a server-side logical float parameter name to an Animator float parameter.
    /// </summary>
    [Serializable]
    public sealed class AnimatorParamSyncEntry
    {
        public bool Enabled = true;
        public string LogicParamName = string.Empty;
        public string AnimatorParamName = string.Empty;
    }

    /// <summary>
    /// Client-side network animator view.
    /// It keeps the server authoritative over animation state and parameters while smoothing local playback.
    /// </summary>
    [RequireComponent(typeof(NetIdentity))]
    [DisallowMultipleComponent]
    public sealed class NetAnimatorView : MonoBehaviour
    {
        [Header("运行时引用")]
        public Animator TargetAnimator;

        [Tooltip("可选的 Animator Controller 来源，供编辑器扫描状态和参数使用。留空时默认使用 TargetAnimator.runtimeAnimatorController。")]
        public RuntimeAnimatorController SourceAnimatorController;

        [Header("播放设置")]
        [Min(0f)]
        public float ParamSmoothTime = 0.05f;

        [Min(0f)]
        public float AnimCrossFadeTime = 0.15f;

        [Header("防滑步")]
        [Tooltip("当位移预测仍显示物体还在明显移动时，阻止动画过早切进 Idle 一类的停步状态。")]
        public bool EnableAntiIceSkating = true;

        [Tooltip("哪些状态应被视为停步目标状态，例如 Idle。这里既可以填 Animator 状态名，也可以填逻辑状态名。")]
        public List<string> AntiIceTargetStates = new List<string> { "Idle" };

        [Tooltip("哪些状态应被视为移动来源状态，例如 Walk 或 Run。这里既可以填 Animator 状态名，也可以填逻辑状态名。")]
        public List<string> AntiIceSourceStates = new List<string> { "Walk" };

        [Header("状态同步映射")]
        [Tooltip("把服务端逻辑状态名映射到 Animator 里的真实状态名。")]
        public List<AnimatorStateSyncEntry> SyncedStates = new List<AnimatorStateSyncEntry>
        {
            new AnimatorStateSyncEntry { LogicStateName = "Idle", AnimatorStateName = "Idle" },
            new AnimatorStateSyncEntry { LogicStateName = "Walk", AnimatorStateName = "Walk" },
            new AnimatorStateSyncEntry { LogicStateName = "Wave", AnimatorStateName = "Wave" },
            new AnimatorStateSyncEntry { LogicStateName = "Dance", AnimatorStateName = "Dance" }
        };

        [Header("Float 参数同步映射")]
        [Tooltip("把服务端逻辑 Float 参数名映射到 Animator 里的真实 Float 参数名。")]
        public List<AnimatorParamSyncEntry> SyncedFloatParams = new List<AnimatorParamSyncEntry>();

        /// <summary>
        /// When true, local online-room ownership skips remote overwrite so local control remains responsive.
        /// </summary>
        public bool IsLocalPlayer { get; set; }

        [FormerlySerializedAs("SyncedStateNames")]
        [SerializeField, HideInInspector]
        private List<string> _legacySyncedStateNames = new List<string>();

        [FormerlySerializedAs("FloatParam1Name")]
        [SerializeField, HideInInspector]
        private string _legacyFloatParam1Name = string.Empty;

        [FormerlySerializedAs("FloatParam2Name")]
        [SerializeField, HideInInspector]
        private string _legacyFloatParam2Name = string.Empty;

        [FormerlySerializedAs("FloatParam3Name")]
        [SerializeField, HideInInspector]
        private string _legacyFloatParam3Name = string.Empty;

        private NetIdentity _identity;
        private int _lastAnimStateHash;
        private bool _isRuntimeInitialized;

        private readonly HashSet<int> _antiIceTargetHashes = new HashSet<int>();
        private readonly HashSet<int> _antiIceSourceHashes = new HashSet<int>();
        private readonly Dictionary<int, int> _logicToAnimatorStateHashes = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _logicToAnimatorParamHashes = new Dictionary<int, int>();
        private readonly HashSet<int> _animatorFloatParamHashes = new HashSet<int>();
        private readonly HashSet<int> _unknownSyncedStateHashes = new HashSet<int>();
        private readonly HashSet<int> _unknownSyncedParamHashes = new HashSet<int>();
        private readonly HashSet<int> _receivedAnimatorParamHashes = new HashSet<int>();
        private readonly Dictionary<int, float> _initialAnimatorParamValues = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _currentParamValues = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _paramVelocities = new Dictionary<int, float>();

        private void Awake()
        {
            _identity = GetComponent<NetIdentity>();
            AutoAssignAnimatorReferences();
            MigrateLegacyConfigIfNeeded();
            EnsureRuntimeInitialized();
        }

        private void Start()
        {
            EnsureRuntimeInitialized();
        }

        private void OnValidate()
        {
            AutoAssignAnimatorReferences();
            MigrateLegacyConfigIfNeeded();
            ResetRuntimeCache();
        }

        /// <summary>
        /// Applies the initial state when the entity is first spawned on the client.
        /// </summary>
        public void HardSetInitialState(int animHash, float normalizedTime, AnimatorParamValue[] animParams, int animParamCount)
        {
            EnsureRuntimeInitialized();
            int resolvedAnimHash = ResolveAnimatorStateHash(animHash);
            _lastAnimStateHash = resolvedAnimHash;

            if (TargetAnimator == null)
            {
                return;
            }

            if (resolvedAnimHash != 0)
            {
                TargetAnimator.Play(resolvedAnimHash, 0, normalizedTime);
            }

            ApplyAnimatorParams(animParams, animParamCount, true, 1f);
        }

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

        private void ProcessAnimatorSync(ref PredictedAnimatorData syncData)
        {
            EnsureRuntimeInitialized();

            int targetHash = ResolveAnimatorStateHash(syncData.AnimStateHash);
            if (EnableAntiIceSkating && ShouldBlockTransitionIntoStopState(targetHash))
            {
                targetHash = _lastAnimStateHash;
            }

            if (ShouldTransitionTo(targetHash))
            {
                _lastAnimStateHash = targetHash;
                float compensatedTime = _antiIceTargetHashes.Contains(targetHash)
                    ? 0f
                    : Mathf.Max(0f, syncData.AnimNormalizedTime + syncData.ServerTimeDelta);
                TargetAnimator.CrossFadeInFixedTime(targetHash, AnimCrossFadeTime, 0, compensatedTime);
            }

            ApplyAnimatorParams(syncData.AnimParams, syncData.AnimParamCount, syncData.PlaybackSpeed > 5f, syncData.PlaybackSpeed);
            ApplyAnimatorPlaybackSpeed(syncData.PlaybackSpeed);
        }

        private void ApplyAnimatorPlaybackSpeed(float playbackSpeed)
        {
            float baseSpeed = playbackSpeed > 0f ? playbackSpeed : 1f;

            if (_identity != null &&
                _identity.SyncService != null &&
                _identity.SyncService.TryGetTransformData(_identity.NetId, out var transformData))
            {
                float distanceToTarget = Vector3.Distance(transform.position, transformData.Position);
                if (distanceToTarget > 1.5f && distanceToTarget <= 3f && baseSpeed > 0f)
                {
                    baseSpeed *= 1.2f;
                }
            }

            TargetAnimator.speed = Mathf.Clamp(baseSpeed, 0f, 3f);
        }

        private bool ShouldBlockTransitionIntoStopState(int targetHash)
        {
            if (targetHash == 0)
            {
                return false;
            }

            if (!_antiIceTargetHashes.Contains(targetHash) || !_antiIceSourceHashes.Contains(_lastAnimStateHash))
            {
                return false;
            }

            if (_identity == null || _identity.SyncService == null)
            {
                return false;
            }

            if (!_identity.SyncService.TryGetTransformData(_identity.NetId, out var transformData))
            {
                return false;
            }

            return transformData.Velocity.sqrMagnitude > 0.02f ||
                   Vector3.Distance(transform.position, transformData.Position) > 0.05f;
        }

        private bool ShouldTransitionTo(int targetHash)
        {
            if (TargetAnimator == null || targetHash == 0)
            {
                return false;
            }

            if (targetHash != _lastAnimStateHash)
            {
                return true;
            }

            if (TargetAnimator.IsInTransition(0))
            {
                return false;
            }

            AnimatorStateInfo currentStateInfo = TargetAnimator.GetCurrentAnimatorStateInfo(0);
            return currentStateInfo.shortNameHash != targetHash && currentStateInfo.fullPathHash != targetHash;
        }

        private void ApplyAnimatorParams(AnimatorParamValue[] animParams, int animParamCount, bool instantApply, float playbackSpeed)
        {
            if (TargetAnimator == null)
            {
                return;
            }

            float effectivePlaybackSpeed = playbackSpeed > 0f ? playbackSpeed : 1f;
            float effectiveSmoothTime = effectivePlaybackSpeed > 0f ? ParamSmoothTime / effectivePlaybackSpeed : ParamSmoothTime;
            _receivedAnimatorParamHashes.Clear();

            if (animParams != null)
            {
                for (int i = 0; i < animParamCount; i++)
                {
                    AnimatorParamValue paramValue = animParams[i];
                    if (!ResolveAnimatorParamHash(paramValue.ParamHash, out int animatorParamHash))
                    {
                        continue;
                    }

                    _receivedAnimatorParamHashes.Add(animatorParamHash);

                    if (!_currentParamValues.TryGetValue(animatorParamHash, out float currentValue))
                    {
                        currentValue = TargetAnimator.GetFloat(animatorParamHash);
                    }

                    if (instantApply || effectiveSmoothTime <= 0f)
                    {
                        TargetAnimator.SetFloat(animatorParamHash, paramValue.Value);
                        _currentParamValues[animatorParamHash] = paramValue.Value;
                        _paramVelocities[animatorParamHash] = 0f;
                        continue;
                    }

                    float velocity = _paramVelocities.TryGetValue(animatorParamHash, out float existingVelocity)
                        ? existingVelocity
                        : 0f;
                    float smoothedValue = Mathf.SmoothDamp(currentValue, paramValue.Value, ref velocity, effectiveSmoothTime);
                    TargetAnimator.SetFloat(animatorParamHash, smoothedValue);
                    _currentParamValues[animatorParamHash] = smoothedValue;
                    _paramVelocities[animatorParamHash] = velocity;
                }
            }

            ResetMissingAnimatorParams(instantApply, effectiveSmoothTime);
        }

        private void ResetMissingAnimatorParams(bool instantApply, float effectiveSmoothTime)
        {
            foreach (KeyValuePair<int, int> mapping in _logicToAnimatorParamHashes)
            {
                int animatorParamHash = mapping.Value;
                if (_receivedAnimatorParamHashes.Contains(animatorParamHash))
                {
                    continue;
                }

                float targetValue = _initialAnimatorParamValues.TryGetValue(animatorParamHash, out float initialValue)
                    ? initialValue
                    : 0f;
                if (!_currentParamValues.TryGetValue(animatorParamHash, out float currentValue))
                {
                    currentValue = TargetAnimator.GetFloat(animatorParamHash);
                }

                if (instantApply || effectiveSmoothTime <= 0f)
                {
                    TargetAnimator.SetFloat(animatorParamHash, targetValue);
                    _currentParamValues[animatorParamHash] = targetValue;
                    _paramVelocities[animatorParamHash] = 0f;
                    continue;
                }

                float velocity = _paramVelocities.TryGetValue(animatorParamHash, out float existingVelocity)
                    ? existingVelocity
                    : 0f;
                float smoothedValue = Mathf.SmoothDamp(currentValue, targetValue, ref velocity, effectiveSmoothTime);
                TargetAnimator.SetFloat(animatorParamHash, smoothedValue);
                _currentParamValues[animatorParamHash] = smoothedValue;
                _paramVelocities[animatorParamHash] = velocity;
            }
        }

        private void EnsureRuntimeInitialized()
        {
            if (_isRuntimeInitialized)
            {
                return;
            }

            _logicToAnimatorStateHashes.Clear();
            _logicToAnimatorParamHashes.Clear();
            _animatorFloatParamHashes.Clear();
            _antiIceTargetHashes.Clear();
            _antiIceSourceHashes.Clear();

            BuildStateMappings();
            BuildAnimatorFloatParamSet();
            BuildParamMappings();
            BuildAntiIceHashes();

            _isRuntimeInitialized = true;
        }

        private void BuildStateMappings()
        {
            if (SyncedStates == null)
            {
                return;
            }

            for (int i = 0; i < SyncedStates.Count; i++)
            {
                AnimatorStateSyncEntry entry = SyncedStates[i];
                if (entry == null || !entry.Enabled)
                {
                    continue;
                }

                string logicStateName = SafeValueOrFallback(entry.LogicStateName, entry.AnimatorStateName);
                string animatorStateName = SafeValueOrFallback(entry.AnimatorStateName, entry.LogicStateName);
                if (string.IsNullOrEmpty(logicStateName) || string.IsNullOrEmpty(animatorStateName))
                {
                    continue;
                }

                int logicHash = ObjectSyncAnimHashUtility.GetStableStringHash(logicStateName);
                int animatorHash = Animator.StringToHash(animatorStateName);
                if (logicHash == 0 || animatorHash == 0)
                {
                    continue;
                }

                _logicToAnimatorStateHashes[logicHash] = animatorHash;
            }
        }

        private void BuildAnimatorFloatParamSet()
        {
            if (TargetAnimator == null)
            {
                return;
            }

            AnimatorControllerParameter[] parameters = TargetAnimator.parameters;
            if (parameters == null)
            {
                return;
            }

            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];
                if (parameter.type == AnimatorControllerParameterType.Float)
                {
                    _animatorFloatParamHashes.Add(parameter.nameHash);
                    if (!_initialAnimatorParamValues.ContainsKey(parameter.nameHash))
                    {
                        _initialAnimatorParamValues[parameter.nameHash] = TargetAnimator.GetFloat(parameter.nameHash);
                    }
                }
            }
        }

        private void BuildParamMappings()
        {
            if (SyncedFloatParams == null)
            {
                return;
            }

            for (int i = 0; i < SyncedFloatParams.Count; i++)
            {
                AnimatorParamSyncEntry entry = SyncedFloatParams[i];
                if (entry == null || !entry.Enabled)
                {
                    continue;
                }

                string logicParamName = SafeValueOrFallback(entry.LogicParamName, entry.AnimatorParamName);
                string animatorParamName = SafeValueOrFallback(entry.AnimatorParamName, entry.LogicParamName);
                if (string.IsNullOrEmpty(logicParamName) || string.IsNullOrEmpty(animatorParamName))
                {
                    continue;
                }

                int logicHash = ObjectSyncAnimHashUtility.GetStableStringHash(logicParamName);
                int animatorHash = Animator.StringToHash(animatorParamName);
                if (logicHash == 0 || animatorHash == 0)
                {
                    continue;
                }

                _logicToAnimatorParamHashes[logicHash] = animatorHash;
            }
        }

        private void BuildAntiIceHashes()
        {
            RegisterAntiIceStates(AntiIceTargetStates, _antiIceTargetHashes);
            RegisterAntiIceStates(AntiIceSourceStates, _antiIceSourceHashes);
        }

        private void RegisterAntiIceStates(List<string> stateNames, HashSet<int> hashSet)
        {
            if (stateNames == null || hashSet == null)
            {
                return;
            }

            for (int i = 0; i < stateNames.Count; i++)
            {
                int resolvedHash = ResolveConfiguredAnimatorStateName(stateNames[i]);
                if (resolvedHash != 0)
                {
                    hashSet.Add(resolvedHash);
                }
            }
        }

        private int ResolveConfiguredAnimatorStateName(string stateName)
        {
            string safeStateName = stateName == null ? string.Empty : stateName.Trim();
            if (string.IsNullOrEmpty(safeStateName))
            {
                return 0;
            }

            int directAnimatorHash = Animator.StringToHash(safeStateName);
            if (TargetAnimator == null || HasAnimatorState(directAnimatorHash))
            {
                return directAnimatorHash;
            }

            int logicHash = ObjectSyncAnimHashUtility.GetStableStringHash(safeStateName);
            if (_logicToAnimatorStateHashes.TryGetValue(logicHash, out int mappedAnimatorHash))
            {
                return mappedAnimatorHash;
            }

            return directAnimatorHash;
        }

        private int ResolveAnimatorStateHash(int serverOrAnimatorHash)
        {
            if (serverOrAnimatorHash == 0)
            {
                return 0;
            }

            if (HasAnimatorState(serverOrAnimatorHash))
            {
                return serverOrAnimatorHash;
            }

            if (_logicToAnimatorStateHashes.TryGetValue(serverOrAnimatorHash, out int animatorHash))
            {
                return animatorHash;
            }

            if (_unknownSyncedStateHashes.Add(serverOrAnimatorHash))
            {
                Debug.LogWarning($"[NetAnimatorView] Unmapped animation state hash received. AnimStateHash:{serverOrAnimatorHash}, Object:{name}");
            }

            return 0;
        }

        private bool ResolveAnimatorParamHash(int logicParamHash, out int animatorParamHash)
        {
            if (logicParamHash != 0 && _logicToAnimatorParamHashes.TryGetValue(logicParamHash, out animatorParamHash))
            {
                if (_animatorFloatParamHashes.Count == 0 || _animatorFloatParamHashes.Contains(animatorParamHash))
                {
                    return true;
                }
            }

            if (_unknownSyncedParamHashes.Add(logicParamHash))
            {
                Debug.LogWarning($"[NetAnimatorView] Unmapped animation float param hash received. ParamHash:{logicParamHash}, Object:{name}");
            }

            animatorParamHash = 0;
            return false;
        }

        private bool HasAnimatorState(int stateHash)
        {
            if (TargetAnimator == null || stateHash == 0)
            {
                return false;
            }

            int layerCount = TargetAnimator.layerCount;
            for (int i = 0; i < layerCount; i++)
            {
                if (TargetAnimator.HasState(i, stateHash))
                {
                    return true;
                }
            }

            return false;
        }

        private void AutoAssignAnimatorReferences()
        {
            if (TargetAnimator == null)
            {
                TargetAnimator = GetComponentInChildren<Animator>();
            }

            if (SourceAnimatorController == null && TargetAnimator != null)
            {
                SourceAnimatorController = TargetAnimator.runtimeAnimatorController;
            }
        }

        private void MigrateLegacyConfigIfNeeded()
        {
            bool hasStateMappings = SyncedStates != null && SyncedStates.Count > 0;
            bool hasParamMappings = SyncedFloatParams != null && SyncedFloatParams.Count > 0;

            if (!hasStateMappings && _legacySyncedStateNames != null && _legacySyncedStateNames.Count > 0)
            {
                SyncedStates = new List<AnimatorStateSyncEntry>(_legacySyncedStateNames.Count);
                for (int i = 0; i < _legacySyncedStateNames.Count; i++)
                {
                    string stateName = _legacySyncedStateNames[i];
                    if (string.IsNullOrWhiteSpace(stateName))
                    {
                        continue;
                    }

                    string safeStateName = stateName.Trim();
                    SyncedStates.Add(new AnimatorStateSyncEntry
                    {
                        Enabled = true,
                        LogicStateName = safeStateName,
                        AnimatorStateName = safeStateName
                    });
                }
            }

            if (!hasParamMappings)
            {
                TryAddLegacyParamMapping(_legacyFloatParam1Name);
                TryAddLegacyParamMapping(_legacyFloatParam2Name);
                TryAddLegacyParamMapping(_legacyFloatParam3Name);
            }
        }

        private void TryAddLegacyParamMapping(string legacyParamName)
        {
            string safeParamName = legacyParamName == null ? string.Empty : legacyParamName.Trim();
            if (string.IsNullOrEmpty(safeParamName))
            {
                return;
            }

            if (SyncedFloatParams == null)
            {
                SyncedFloatParams = new List<AnimatorParamSyncEntry>();
            }

            for (int i = 0; i < SyncedFloatParams.Count; i++)
            {
                AnimatorParamSyncEntry entry = SyncedFloatParams[i];
                if (entry == null)
                {
                    continue;
                }

                if (string.Equals(entry.AnimatorParamName, safeParamName, StringComparison.Ordinal) ||
                    string.Equals(entry.LogicParamName, safeParamName, StringComparison.Ordinal))
                {
                    return;
                }
            }

            SyncedFloatParams.Add(new AnimatorParamSyncEntry
            {
                Enabled = true,
                LogicParamName = safeParamName,
                AnimatorParamName = safeParamName
            });
        }

        private void ResetRuntimeCache()
        {
            _isRuntimeInitialized = false;
            _logicToAnimatorStateHashes.Clear();
            _logicToAnimatorParamHashes.Clear();
            _animatorFloatParamHashes.Clear();
            _antiIceTargetHashes.Clear();
            _antiIceSourceHashes.Clear();
            _unknownSyncedStateHashes.Clear();
            _unknownSyncedParamHashes.Clear();
            _receivedAnimatorParamHashes.Clear();
            _initialAnimatorParamValues.Clear();
            _currentParamValues.Clear();
            _paramVelocities.Clear();
        }

        private static string SafeValueOrFallback(string primary, string fallback)
        {
            string safePrimary = primary == null ? string.Empty : primary.Trim();
            if (!string.IsNullOrEmpty(safePrimary))
            {
                return safePrimary;
            }

            return fallback == null ? string.Empty : fallback.Trim();
        }
    }
}
