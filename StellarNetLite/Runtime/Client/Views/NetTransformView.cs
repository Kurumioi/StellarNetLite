using UnityEngine;
using StellarNet.Lite.Client.Components;

namespace StellarNet.Lite.Client.Components.Views
{
    // 网络实体位姿表现层。
    [RequireComponent(typeof(NetIdentity))]
    public class NetTransformView : MonoBehaviour
    {
        // 位置平滑时间。
        [Header("空间同步配置")] public float PosSmoothTime = 0.1f;
        // 旋转平滑速度。
        public float RotSmoothSpeed = 15f;
        // 超过该距离直接瞬移。
        public float SnapThreshold = 3.0f;
        // 追赶阈值，超过后加快收敛。
        public float CatchUpThreshold = 1.5f;
        // 静止阈值，避免临近目标点抖动。
        public float StopThreshold = 0.05f;

        private NetIdentity _identity;
        private Vector3 _currentVelocity;

        private void Awake()
        {
            _identity = GetComponent<NetIdentity>();
        }

        // 生成或重建时直接写入一份初始状态。
        public void HardSetInitialState(Vector3 pos, Quaternion rot, Vector3 scale)
        {
            transform.position = pos;
            transform.rotation = rot;
            transform.localScale = scale;
            _currentVelocity = Vector3.zero;
        }

        private void Update()
        {
            if (_identity == null || _identity.SyncService == null) return;
            if (!_identity.SyncService.TryGetTransformData(_identity.NetId, out var syncData)) return;

            // 每帧从同步服务读取预测结果并驱动表现层。
            ProcessTransformSync(ref syncData);
        }

        private void ProcessTransformSync(ref PredictedTransformData syncData)
        {
            Vector3 currentPos = transform.position;
            float distanceToTarget = Vector3.Distance(currentPos, syncData.Position);

            if (syncData.PlaybackSpeed > 5f || distanceToTarget > SnapThreshold)
            {
                // 回放快进或误差过大时直接硬校正。
                transform.position = syncData.Position;
                _currentVelocity = Vector3.zero;
            }
            else if (syncData.Velocity.sqrMagnitude < 0.01f && distanceToTarget < StopThreshold)
            {
                // 已基本停稳时直接收敛到目标点。
                transform.position = syncData.Position;
                _currentVelocity = Vector3.zero;
            }
            else if (syncData.PlaybackSpeed > 0f)
            {
                float effectiveSmoothTime = PosSmoothTime;
                if (distanceToTarget > CatchUpThreshold) effectiveSmoothTime *= 0.5f;
                effectiveSmoothTime /= syncData.PlaybackSpeed;

                // 核心优化 P1-2：确保 SmoothDamp 内部正确使用 Time.deltaTime
                transform.position = Vector3.SmoothDamp(currentPos, syncData.Position, ref _currentVelocity, effectiveSmoothTime, Mathf.Infinity, Time.deltaTime);
            }

            if (syncData.PlaybackSpeed > 0f)
            {
                if (syncData.PlaybackSpeed > 5f)
                {
                    // 极高速播放时直接应用目标旋转和缩放。
                    transform.rotation = Quaternion.Euler(syncData.Rotation);
                    transform.localScale = syncData.Scale;
                }
                else
                {
                    Quaternion targetRot = Quaternion.Euler(syncData.Rotation);
                    float rotSpeed = RotSmoothSpeed * syncData.PlaybackSpeed;

                    // 核心优化 P1-2：确保 Slerp 使用 Time.deltaTime
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotSpeed);

                    float scaleSpeed = 10f * syncData.PlaybackSpeed;
                    transform.localScale = Vector3.Lerp(transform.localScale, syncData.Scale, Time.deltaTime * scaleSpeed);
                }
            }
        }
    }
}
