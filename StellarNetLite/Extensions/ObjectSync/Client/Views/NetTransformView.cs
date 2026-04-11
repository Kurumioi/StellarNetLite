using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Client.Core;
using UnityEngine;

namespace StellarNet.Lite.Client.Components.Views
{
    /// <summary>
    /// Transform 同步表现组件。
    /// </summary>
    [RequireComponent(typeof(NetIdentity))]
    public class NetTransformView : MonoBehaviour
    {
        /// <summary>
        /// 远端位置平滑时间。
        /// </summary>
        [Header("远端平滑配置 (Dead Reckoning)")]
        public float PosSmoothTime = 0.1f;

        /// <summary>
        /// 远端旋转平滑速度。
        /// </summary>
        public float RotSmoothSpeed = 15f;

        /// <summary>
        /// 超过该距离时直接瞬移。
        /// </summary>
        public float SnapThreshold = 3.0f;

        /// <summary>
        /// 超过该距离时加快追赶。
        /// </summary>
        public float CatchUpThreshold = 1.5f;

        /// <summary>
        /// 小于该距离且速度接近零时直接落点。
        /// </summary>
        public float StopThreshold = 0.05f;

        /// <summary>
        /// 本地玩家超过该距离时直接和解。
        /// </summary>
        [Header("本地柔性和解配置 (Soft Reconciliation)")]
        public float LocalSnapThreshold = 2.0f;

        /// <summary>
        /// 本地玩家柔性和解速度。
        /// </summary>
        public float LocalSoftCorrectionSpeed = 5f;

        /// <summary>
        /// 本地玩家忽略微小误差的阈值。
        /// </summary>
        public float LocalIgnoreThreshold = 0.05f;

        /// <summary>
        /// 当前对象是否为本地玩家。
        /// </summary>
        public bool IsLocalPlayer { get; set; }

        // 当前实体身份组件。
        private NetIdentity _identity;

        // SmoothDamp 使用的速度缓存。
        private Vector3 _currentVelocity;

        // 当前对象上的 CharacterController。
        private CharacterController _characterController;

        /// <summary>
        /// 初始化引用。
        /// </summary>
        private void Awake()
        {
            _identity = GetComponent<NetIdentity>();
            _characterController = GetComponent<CharacterController>();
        }

        /// <summary>
        /// 立即应用初始 Transform。
        /// </summary>
        public void HardSetInitialState(Vector3 pos, Quaternion rot, Vector3 scale)
        {
            ApplyPosition(pos);
            transform.rotation = rot;
            transform.localScale = scale;
            _currentVelocity = Vector3.zero;
        }

        /// <summary>
        /// 按帧刷新 Transform 同步。
        /// </summary>
        private void Update()
        {
            if (_identity == null || _identity.SyncService == null)
            {
                return;
            }

            if (!_identity.SyncService.TryGetTransformData(_identity.NetId, out var syncData))
            {
                return;
            }

            // 在线房间中的本地玩家走柔性和解，其余情况统一按远端对象处理。
            if (IsLocalPlayer && NetClient.State == ClientAppState.OnlineRoom)
            {
                ProcessLocalReconciliation(ref syncData);
            }
            else
            {
                ProcessRemoteTransformSync(ref syncData);
            }
        }

        /// <summary>
        /// 处理本地玩家的柔性和解。
        /// </summary>
        private void ProcessLocalReconciliation(ref PredictedTransformData syncData)
        {
            Vector3 currentPos = transform.position;
            float distanceToServer = Vector3.Distance(currentPos, syncData.Position);
            if (distanceToServer <= LocalIgnoreThreshold)
            {
                return;
            }

            if (distanceToServer > LocalSnapThreshold)
            {
                ApplyPosition(syncData.Position);
                return;
            }

            Vector3 targetPos = Vector3.Lerp(currentPos, syncData.Position, Time.deltaTime * LocalSoftCorrectionSpeed);
            ApplyPosition(targetPos);
        }

        /// <summary>
        /// 处理远端对象的插值同步。
        /// </summary>
        private void ProcessRemoteTransformSync(ref PredictedTransformData syncData)
        {
            Vector3 currentPos = transform.position;
            float distanceToTarget = Vector3.Distance(currentPos, syncData.Position);

            if (syncData.PlaybackSpeed > 5f || distanceToTarget > SnapThreshold)
            {
                ApplyPosition(syncData.Position);
                _currentVelocity = Vector3.zero;
            }
            else if (syncData.Velocity.sqrMagnitude < 0.01f && distanceToTarget < StopThreshold)
            {
                ApplyPosition(syncData.Position);
                _currentVelocity = Vector3.zero;
            }
            else if (syncData.PlaybackSpeed > 0f)
            {
                float effectiveSmoothTime = PosSmoothTime;
                if (distanceToTarget > CatchUpThreshold)
                {
                    effectiveSmoothTime *= 0.5f;
                }

                effectiveSmoothTime /= syncData.PlaybackSpeed;
                Vector3 newPos = Vector3.SmoothDamp(
                    currentPos,
                    syncData.Position,
                    ref _currentVelocity,
                    effectiveSmoothTime,
                    Mathf.Infinity,
                    Time.deltaTime);
                ApplyPosition(newPos);
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

        /// <summary>
        /// 应用位置并兼容 CharacterController。
        /// </summary>
        private void ApplyPosition(Vector3 pos)
        {
            if (_characterController != null)
            {
                _characterController.enabled = false;
                transform.position = pos;
                _characterController.enabled = true;
            }
            else
            {
                transform.position = pos;
            }
        }
    }
}
