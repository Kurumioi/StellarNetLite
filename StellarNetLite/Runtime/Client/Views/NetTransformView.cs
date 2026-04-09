using UnityEngine;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Client.Core;

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

            // 核心修复：只有在真实的在线房间中，才对本地玩家执行柔性位置和解。
            // 在回放模式下（ReplayRoom），所有对象（包括自己）都必须被视为远端对象，严格应用录像中的 Transform（包含旋转）。
            if (IsLocalPlayer && NetClient.State == ClientAppState.OnlineRoom)
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

            if (distanceToServer <= LocalIgnoreThreshold) return;

            if (distanceToServer > LocalSnapThreshold)
            {
                ApplyPosition(syncData.Position);
                return;
            }

            Vector3 targetPos = Vector3.Lerp(currentPos, syncData.Position, Time.deltaTime * LocalSoftCorrectionSpeed);
            ApplyPosition(targetPos);
        }

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
                if (distanceToTarget > CatchUpThreshold) effectiveSmoothTime *= 0.5f;
                effectiveSmoothTime /= syncData.PlaybackSpeed;

                Vector3 newPos = Vector3.SmoothDamp(currentPos, syncData.Position, ref _currentVelocity, effectiveSmoothTime, Mathf.Infinity,
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