using UnityEngine;
using StellarNet.Lite.Client.Components;

namespace StellarNet.Lite.Client.Components.Views
{
    [RequireComponent(typeof(NetIdentity))]
    public class NetTransformView : MonoBehaviour
    {
        [Header("远端平滑配置 (Dead Reckoning)")] public float PosSmoothTime = 0.1f;
        public float RotSmoothSpeed = 15f;
        public float SnapThreshold = 3.0f;
        public float CatchUpThreshold = 1.5f;
        public float StopThreshold = 0.05f;

        [Header("本地柔性和解配置 (Soft Reconciliation)")]
        public float LocalSnapThreshold = 2.0f;

        public float LocalSoftCorrectionSpeed = 5f;
        public float LocalIgnoreThreshold = 0.05f;

        public bool IsLocalPlayer { get; set; }

        private NetIdentity _identity;
        private Vector3 _currentVelocity;
        private CharacterController _characterController;

        private void Awake()
        {
            _identity = GetComponent<NetIdentity>();
            _characterController = GetComponent<CharacterController>();
        }

        public void HardSetInitialState(Vector3 pos, Quaternion rot, Vector3 scale)
        {
            ApplyPosition(pos);
            transform.rotation = rot;
            transform.localScale = scale;
            _currentVelocity = Vector3.zero;
        }

        private void Update()
        {
            if (_identity == null || _identity.SyncService == null) return;

            if (!_identity.SyncService.TryGetTransformData(_identity.NetId, out var syncData)) return;

            if (IsLocalPlayer)
            {
                ProcessLocalReconciliation(ref syncData);
            }
            else
            {
                ProcessRemoteTransformSync(ref syncData);
            }
        }

        private void ProcessLocalReconciliation(ref PredictedTransformData syncData)
        {
            Vector3 currentPos = transform.position;
            float distanceToServer = Vector3.Distance(currentPos, syncData.Position);

            // 误差极小，完全信任本地输入，静默忽略服务端坐标
            if (distanceToServer <= LocalIgnoreThreshold) return;

            // 误差极大（如撞墙预测失败、被服务端技能强制位移），执行硬拉扯
            if (distanceToServer > LocalSnapThreshold)
            {
                ApplyPosition(syncData.Position);
                return;
            }

            // 柔性和解：在后台静默将本地坐标平滑纠正到服务端坐标
            // 肉眼表现为轻微的滑步，而不是卡顿闪烁
            Vector3 targetPos = Vector3.Lerp(currentPos, syncData.Position, Time.deltaTime * LocalSoftCorrectionSpeed);
            ApplyPosition(targetPos);
        }

        private void ProcessRemoteTransformSync(ref PredictedTransformData syncData)
        {
            Vector3 currentPos = transform.position;
            float distanceToTarget = Vector3.Distance(currentPos, syncData.Position);

            // 距离过大或处于快进回放状态，直接硬切
            if (syncData.PlaybackSpeed > 5f || distanceToTarget > SnapThreshold)
            {
                ApplyPosition(syncData.Position);
                _currentVelocity = Vector3.zero;
            }
            // 目标已停止且距离极近，直接对齐防止微小抖动
            else if (syncData.Velocity.sqrMagnitude < 0.01f && distanceToTarget < StopThreshold)
            {
                ApplyPosition(syncData.Position);
                _currentVelocity = Vector3.zero;
            }
            // 正常平滑追赶（结合航位推测）
            else if (syncData.PlaybackSpeed > 0f)
            {
                float effectiveSmoothTime = PosSmoothTime;
                if (distanceToTarget > CatchUpThreshold) effectiveSmoothTime *= 0.5f;
                effectiveSmoothTime /= syncData.PlaybackSpeed;

                Vector3 newPos = Vector3.SmoothDamp(currentPos, syncData.Position, ref _currentVelocity, effectiveSmoothTime, Mathf.Infinity,
                    Time.deltaTime);
                ApplyPosition(newPos);
            }

            // 旋转与缩放同步
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

        private void ApplyPosition(Vector3 pos)
        {
            // 如果挂载了 CharacterController，直接修改 transform.position 会被物理引擎覆盖，必须先禁用
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