using System.Collections.Generic;
using UnityEngine;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Components.Views
{
    /// <summary>
    /// 网络实体表现层核心控制器 (View 层)
    /// 核心修复：引入 Animator 真实状态校验与 CrossFadeInFixedTime 强制重试机制，彻底解决非循环动画末尾融合卡死的问题。
    /// </summary>
    public class NetTransformPresenter : MonoBehaviour
    {
        [Header("空间同步配置")] public float PosSmoothTime = 0.1f;
        public float RotSmoothSpeed = 15f;
        public float SnapThreshold = 3.0f;
        public float CatchUpThreshold = 1.5f;
        public float StopThreshold = 0.05f;

        [Header("动画同步配置")] public Animator TargetAnimator;
        public float ParamSmoothTime = 0.05f;

        [Tooltip("状态切换时的平滑融合时间(秒)。值越大越平滑(解决刹车僵硬)，但起步会有延迟感；值越小越干脆。推荐值: 0.1 ~ 0.2")]
        public float AnimCrossFadeTime = 0.15f;

        [Header("防滑冰补偿 (通用状态机矩阵)")] public bool EnableAntiIceSkating = true;
        public List<string> AntiIceTargetStates = new List<string> { "Idle" };
        public List<string> AntiIceSourceStates = new List<string> { "Walk" };

        [Header("BlendTree 参数映射矩阵")] public string FloatParam1Name = "";
        public string FloatParam2Name = "";
        public string FloatParam3Name = "";

        private int _netId;
        private ClientObjectSyncComponent _syncService;
        private Vector3 _currentVelocity;
        private int _lastAnimStateHash;

        private readonly HashSet<int> _antiIceTargetHashes = new HashSet<int>();
        private readonly HashSet<int> _antiIceSourceHashes = new HashSet<int>();

        private int _param1Hash, _param2Hash, _param3Hash;
        private float _currentParam1, _currentParam2, _currentParam3;
        private float _param1Vel, _param2Vel, _param3Vel;

        public void Init(int netId, ClientObjectSyncComponent syncService)
        {
            _netId = netId;
            _syncService = syncService;

            if (TargetAnimator == null) TargetAnimator = GetComponentInChildren<Animator>();

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

        public void HardSetInitialState(Vector3 pos, Quaternion rot, Vector3 scale, int animHash, float normalizedTime, float p1, float p2, float p3)
        {
            transform.position = pos;
            transform.rotation = rot;
            transform.localScale = scale;
            _currentVelocity = Vector3.zero;
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
            if (_syncService == null) return;
            if (!_syncService.TryGetPredictedData(_netId, out var syncData)) return;

            ProcessTransformSync(ref syncData);
            ProcessAnimatorSync(ref syncData);
        }

        private void ProcessTransformSync(ref PredictedSyncData syncData)
        {
            Vector3 currentPos = transform.position;
            float distanceToTarget = Vector3.Distance(currentPos, syncData.Position);

            if (syncData.PlaybackSpeed > 5f || distanceToTarget > SnapThreshold)
            {
                transform.position = syncData.Position;
                _currentVelocity = Vector3.zero;
            }
            else if (syncData.Velocity.sqrMagnitude < 0.01f && distanceToTarget < StopThreshold)
            {
                transform.position = syncData.Position;
                _currentVelocity = Vector3.zero;
            }
            else if (syncData.PlaybackSpeed > 0f)
            {
                float effectiveSmoothTime = PosSmoothTime;
                if (distanceToTarget > CatchUpThreshold) effectiveSmoothTime *= 0.5f;
                effectiveSmoothTime /= syncData.PlaybackSpeed;

                transform.position = Vector3.SmoothDamp(currentPos, syncData.Position, ref _currentVelocity, effectiveSmoothTime);
            }

            if (syncData.PlaybackSpeed > 0f)
            {
                if (syncData.PlaybackSpeed > 5f)
                {
                    transform.rotation = Quaternion.Euler(syncData.Rotation);
                    transform.localScale = syncData.Scale;
                }
                else
                {
                    Quaternion targetRot = Quaternion.Euler(syncData.Rotation);
                    float rotSpeed = RotSmoothSpeed * syncData.PlaybackSpeed;
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotSpeed);

                    float scaleSpeed = 10f * syncData.PlaybackSpeed;
                    transform.localScale = Vector3.Lerp(transform.localScale, syncData.Scale, Time.deltaTime * scaleSpeed);
                }
            }
        }

        private void ProcessAnimatorSync(ref PredictedSyncData syncData)
        {
            if (TargetAnimator == null) return;

            int targetHash = syncData.AnimStateHash;

            if (EnableAntiIceSkating)
            {
                if (_antiIceTargetHashes.Contains(targetHash) && _antiIceSourceHashes.Contains(_lastAnimStateHash))
                {
                    if (_currentVelocity.sqrMagnitude > 0.02f || Vector3.Distance(transform.position, syncData.Position) > 0.05f)
                    {
                        targetHash = _lastAnimStateHash;
                    }
                }
            }

            // 核心修复：状态机真实校验与强制重试机制
            bool needTransition = false;

            // 1. 逻辑状态发生变更，需要触发融合
            if (targetHash != 0 && targetHash != _lastAnimStateHash)
            {
                needTransition = true;
            }
            else if (targetHash != 0)
            {
                // 2. 逻辑状态没变，但 Animator 实际状态不对！(说明上一次 CrossFade 失败了)
                // 必须确保当前不在 Transition 过渡中，防止重复触发打断正常融合
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

                // 核心修复：对于待机类状态(通常是循环的)，直接从 0 开始融合，不要加时间补偿。
                // 加上时间补偿反而容易导致非循环动画末尾卡死。
                float compensatedTime = _antiIceTargetHashes.Contains(targetHash) ? 0f : (syncData.AnimNormalizedTime + syncData.ServerTimeDelta);

                // 核心修复：使用 CrossFadeInFixedTime 替代 CrossFade，强行剥夺 Animator 对非循环动画末尾的锁定权
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
            Vector3 currentPos = transform.position;
            float distanceToTarget = Vector3.Distance(currentPos, syncData.Position);
            if (distanceToTarget > CatchUpThreshold && distanceToTarget <= SnapThreshold && baseSpeed > 0f)
            {
                baseSpeed *= 1.2f;
            }

            TargetAnimator.speed = Mathf.Clamp(baseSpeed, 0f, 3f);
        }
    }
}