using System.Collections.Generic;
using UnityEngine;
using StellarNet.Lite.Client.Components;

namespace StellarNet.Lite.Client.Components.Views
{
    /// <summary>
    /// 纯粹的动画同步表现组件。
    /// 职责：向底层索要 PredictedAnimatorData，并执行状态机融合与参数插值。
    /// </summary>
    [RequireComponent(typeof(NetIdentity))]
    public class NetAnimatorView : MonoBehaviour
    {
        [Header("动画同步配置")] public Animator TargetAnimator;
        public float ParamSmoothTime = 0.05f;
        public float AnimCrossFadeTime = 0.15f;

        [Header("防滑冰补偿 (通用状态机矩阵)")] public bool EnableAntiIceSkating = true;
        public List<string> AntiIceTargetStates = new List<string> { "Idle" };
        public List<string> AntiIceSourceStates = new List<string> { "Walk" };

        [Header("BlendTree 参数映射矩阵")] public string FloatParam1Name = "";
        public string FloatParam2Name = "";
        public string FloatParam3Name = "";

        private NetIdentity _identity;
        private int _lastAnimStateHash;
        private readonly HashSet<int> _antiIceTargetHashes = new HashSet<int>();
        private readonly HashSet<int> _antiIceSourceHashes = new HashSet<int>();

        private int _param1Hash, _param2Hash, _param3Hash;
        private float _currentParam1, _currentParam2, _currentParam3;
        private float _param1Vel, _param2Vel, _param3Vel;

        private void Awake()
        {
            _identity = GetComponent<NetIdentity>();
            if (TargetAnimator == null) TargetAnimator = GetComponentInChildren<Animator>();
        }

        private void Start()
        {
            _antiIceTargetHashes.Clear();
            foreach (var state in AntiIceTargetStates)
            {
                if (!string.IsNullOrEmpty(state)) _antiIceTargetHashes.Add(Animator.StringToHash(state));
            }

            _antiIceSourceHashes.Clear();
            foreach (var state in AntiIceSourceStates)
            {
                if (!string.IsNullOrEmpty(state)) _antiIceSourceHashes.Add(Animator.StringToHash(state));
            }

            _param1Hash = string.IsNullOrEmpty(FloatParam1Name) ? 0 : Animator.StringToHash(FloatParam1Name);
            _param2Hash = string.IsNullOrEmpty(FloatParam2Name) ? 0 : Animator.StringToHash(FloatParam2Name);
            _param3Hash = string.IsNullOrEmpty(FloatParam3Name) ? 0 : Animator.StringToHash(FloatParam3Name);
        }

        public void HardSetInitialState(int animHash, float normalizedTime, float p1, float p2, float p3)
        {
            _lastAnimStateHash = animHash;
            _currentParam1 = p1;
            _currentParam2 = p2;
            _currentParam3 = p3;

            if (TargetAnimator != null)
            {
                if (animHash != 0) TargetAnimator.Play(animHash, 0, normalizedTime);
                if (_param1Hash != 0) TargetAnimator.SetFloat(_param1Hash, p1);
                if (_param2Hash != 0) TargetAnimator.SetFloat(_param2Hash, p2);
                if (_param3Hash != 0) TargetAnimator.SetFloat(_param3Hash, p3);
            }
        }

        private void Update()
        {
            if (_identity == null || _identity.SyncService == null || TargetAnimator == null) return;

            if (!_identity.SyncService.TryGetAnimatorData(_identity.NetId, out var syncData)) return;

            ProcessAnimatorSync(ref syncData);
        }

        private void ProcessAnimatorSync(ref PredictedAnimatorData syncData)
        {
            int targetHash = syncData.AnimStateHash;

            if (EnableAntiIceSkating)
            {
                if (_antiIceTargetHashes.Contains(targetHash) && _antiIceSourceHashes.Contains(_lastAnimStateHash))
                {
                    // 依赖于 TransformView 的状态，如果 TransformView 还在移动，则暂缓切入 Idle
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
            else if (targetHash != 0)
            {
                if (!TargetAnimator.IsInTransition(0))
                {
                    var stateInfo = TargetAnimator.GetCurrentAnimatorStateInfo(0);
                    if (stateInfo.shortNameHash != targetHash)
                    {
                        needTransition = true;
                    }
                }
            }

            if (needTransition)
            {
                _lastAnimStateHash = targetHash;
                float compensatedTime = _antiIceTargetHashes.Contains(targetHash) ? 0f : (syncData.AnimNormalizedTime + syncData.ServerTimeDelta);
                TargetAnimator.CrossFadeInFixedTime(targetHash, AnimCrossFadeTime, 0, compensatedTime);
            }

            if (syncData.PlaybackSpeed > 0f)
            {
                if (syncData.PlaybackSpeed > 5f)
                {
                    if (_param1Hash != 0) TargetAnimator.SetFloat(_param1Hash, syncData.FloatParam1);
                    if (_param2Hash != 0) TargetAnimator.SetFloat(_param2Hash, syncData.FloatParam2);
                    if (_param3Hash != 0) TargetAnimator.SetFloat(_param3Hash, syncData.FloatParam3);
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

            // 如果有 Transform 同步，根据距离调整动画播放速度以追赶
            if (_identity.SyncService.TryGetTransformData(_identity.NetId, out var tData))
            {
                float distanceToTarget = Vector3.Distance(transform.position, tData.Position);
                // 这里的 CatchUpThreshold 和 SnapThreshold 最好能从 TransformView 获取，这里暂用硬编码默认值近似
                if (distanceToTarget > 1.5f && distanceToTarget <= 3.0f && baseSpeed > 0f)
                {
                    baseSpeed *= 1.2f;
                }
            }

            TargetAnimator.speed = Mathf.Clamp(baseSpeed, 0f, 3f);
        }
    }
}