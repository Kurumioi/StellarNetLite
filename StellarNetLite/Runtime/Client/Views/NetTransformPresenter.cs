using UnityEngine;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Components.Views
{
    public class NetTransformPresenter : MonoBehaviour
    {
        [Header("同步配置")] [Tooltip("平滑追赶的时间 (秒)。值越小越跟手但容易抖，值越大越平滑但有延迟感。")]
        public float SmoothTime = 0.1f;

        [Tooltip("防拉扯阈值 (米)。当预测点与当前位置距离超过此值时，放弃平滑，直接闪现。")]
        public float SnapThreshold = 3.0f;

        [Tooltip("防滑动阈值 (米)。当速度为0且距离目标点小于此值时，强制对齐坐标，防止原地微小滑动。")]
        public float StopThreshold = 0.05f;

        [Header("组件引用")] public Animator TargetAnimator;

        private int _netId;
        private ClientObjectSyncComponent _syncService;

        private Vector3 _currentVelocity;
        private int _lastAnimStateHash;

        public void Init(int netId, ClientObjectSyncComponent syncService)
        {
            _netId = netId;
            _syncService = syncService;

            if (_syncService == null)
            {
                NetLogger.LogError("[NetTransformPresenter]", $"初始化失败: 物体 {gameObject.name} 缺失 SyncService 引用，同步逻辑将无法执行。");
                enabled = false;
                return;
            }

            if (TargetAnimator == null)
            {
                TargetAnimator = GetComponentInChildren<Animator>();
            }
        }

        private void Update()
        {
            if (_syncService == null) return;

            if (!_syncService.TryGetPredictedData(_netId, out var syncData))
            {
                return;
            }

            ProcessTransformSync(ref syncData);
            ProcessAnimatorSync(ref syncData);
        }

        private void ProcessTransformSync(ref PredictedSyncData syncData)
        {
            Vector3 currentPos = transform.position;
            float distanceToTarget = Vector3.Distance(currentPos, syncData.Position);

            // 核心优化：动态计算平滑时间。倍速越高，平滑时间越短，追赶越快，防止 4 倍速时发生严重拉扯
            float effectiveSmoothTime = syncData.PlaybackSpeed > 0 ? SmoothTime / syncData.PlaybackSpeed : SmoothTime;

            if (distanceToTarget > SnapThreshold)
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
                // 只有在非暂停状态下才进行位移插值
                transform.position = Vector3.SmoothDamp(
                    currentPos,
                    syncData.Position,
                    ref _currentVelocity,
                    effectiveSmoothTime
                );
            }

            if (syncData.Velocity.sqrMagnitude > 0.01f && syncData.PlaybackSpeed > 0f)
            {
                Quaternion targetRot = Quaternion.LookRotation(syncData.Velocity.normalized);
                float rotSpeed = syncData.PlaybackSpeed > 0 ? 10f * syncData.PlaybackSpeed : 10f;
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotSpeed);
            }

            if (syncData.PlaybackSpeed > 0f)
            {
                float scaleSpeed = syncData.PlaybackSpeed > 0 ? 10f * syncData.PlaybackSpeed : 10f;
                transform.localScale = Vector3.Lerp(transform.localScale, syncData.Scale, Time.deltaTime * scaleSpeed);
            }
        }

        private void ProcessAnimatorSync(ref PredictedSyncData syncData)
        {
            if (TargetAnimator == null || syncData.AnimStateHash == 0) return;

            if (syncData.AnimStateHash != _lastAnimStateHash)
            {
                _lastAnimStateHash = syncData.AnimStateHash;
                // 传入 NormalizedTime，保证 Seek 跳转时动画帧也能大致对齐
                TargetAnimator.CrossFade(syncData.AnimStateHash, 0.1f, 0, syncData.AnimNormalizedTime);
            }

            // 核心修复：将 Animator 的播放速度与回放倍速/暂停状态严格绑定
            // 暂停时 speed = 0 动画定格；4倍速时 speed = 4 动作鬼畜加速
            TargetAnimator.speed = syncData.PlaybackSpeed;
        }
    }
}