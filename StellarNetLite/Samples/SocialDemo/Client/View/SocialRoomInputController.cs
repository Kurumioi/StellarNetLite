using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Client.Components.Views;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Game.Shared.Protocol;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;
using UnityEngine.EventSystems;

namespace StellarNet.Lite.Game.Client.Views
{
    /// <summary>
    /// 社交房间输入控制器。
    /// 当前采用客户端本地权威移动：
    /// - 本地先做真实移动和碰撞
    /// - 本地立刻更新自己的动画表现
    /// - 再把真实结果上报给服务端验证与转发
    /// </summary>
    public class SocialRoomInputController : MonoBehaviour
    {
        private enum LocalVisualState : byte
        {
            Idle = 0,
            Walk = 1,
            Wave = 2,
            Dance = 3
        }

        private ClientRoom _boundRoom;
        private bool _isInputBlockedByWeakNet;
        private Vector2 _lastInput = Vector2.zero;
        private bool _isInitialized;
        private IUnRegister _networkQualityToken;

        private Transform _localPlayerTransform;
        private CharacterController _characterController;
        private Animator _localAnimator;
        private float _lastSendTime;

        private const float SendInterval = 1f / 30f;
        private const float MoveSpeed = 4.0f;
        private const float RotationSmoothSpeed = 15f;
        private const float WaveDurationSeconds = 2.5f;
        private const float DanceDurationSeconds = 4.0f;
        private static readonly int AnimatorHash_Idle = Animator.StringToHash("Idle");
        private static readonly int AnimatorHash_Walk = Animator.StringToHash("Walk");
        private static readonly int AnimatorHash_Wave = Animator.StringToHash("Wave");
        private static readonly int AnimatorHash_Dance = Animator.StringToHash("Dance");

        private float _targetRotationY;
        private float _localActionLockUntil;
        private LocalVisualState _localVisualState = LocalVisualState.Idle;

        public void Init(ClientRoom room)
        {
            if (room == null)
            {
                NetLogger.LogError("SocialRoomInputController", $"初始化失败: room 为空, Object:{name}");
                return;
            }

            if (_isInitialized)
            {
                if (_boundRoom == room) return;
                Clear();
            }

            _boundRoom = room;
            _lastInput = Vector2.zero;
            _isInputBlockedByWeakNet = false;
            _localPlayerTransform = null;
            _characterController = null;
            _localAnimator = null;
            _lastSendTime = 0f;
            _targetRotationY = 0f;
            _localActionLockUntil = 0f;
            _localVisualState = LocalVisualState.Idle;

            _networkQualityToken = GlobalTypeNetEvent.Register<Local_NetworkQualityChanged>(HandleNetworkQualityChanged);
            _isInitialized = true;
        }

        public void Clear()
        {
            _networkQualityToken?.UnRegister();
            _networkQualityToken = null;
            _boundRoom = null;
            _lastInput = Vector2.zero;
            _isInputBlockedByWeakNet = false;
            _localPlayerTransform = null;
            _characterController = null;
            _localAnimator = null;
            _localActionLockUntil = 0f;
            _localVisualState = LocalVisualState.Idle;
            _isInitialized = false;
        }

        private void OnDestroy()
        {
            Clear();
        }

        private void HandleNetworkQualityChanged(Local_NetworkQualityChanged evt)
        {
            _isInputBlockedByWeakNet = evt.IsWeakNetBlock;
        }

        public void SendChatBubble(string content)
        {
            if (string.IsNullOrWhiteSpace(content) || _boundRoom == null)
            {
                return;
            }

            NetClient.Send(new C2S_SocialBubbleReq { Content = content });
        }

        public void RequestEndGame()
        {
            if (_boundRoom == null) return;

            ClientRoomSettingsComponent settingsComp = _boundRoom.GetComponent<ClientRoomSettingsComponent>();
            if (settingsComp == null || NetClient.Session == null) return;

            if (!settingsComp.Members.TryGetValue(NetClient.Session.SessionId, out var myInfo) || !myInfo.IsOwner)
            {
                return;
            }

            NetClient.Send(new C2S_EndGame());
        }

        private void Update()
        {
            if (_boundRoom == null)
            {
                return;
            }

            ClientRoomSettingsComponent settingsComp = _boundRoom.GetComponent<ClientRoomSettingsComponent>();
            if (settingsComp != null && settingsComp.IsGameStarted && NetClient.State == ClientAppState.OnlineRoom)
            {
                ProcessInput();
            }
        }

        private void ProcessInput()
        {
            if (_isInputBlockedByWeakNet)
            {
                return;
            }

            if (_localPlayerTransform == null)
            {
                BindLocalPlayer();
            }

            if (_localPlayerTransform == null || _characterController == null)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.F12))
            {
                RequestEndGame();
            }

            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            {
                var tmpInput = EventSystem.current.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>();
                if (tmpInput != null && tmpInput.isFocused)
                {
                    StopLocalMovementAndSync();
                    ApplySmoothRotation();
                    return;
                }
            }

            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Vector3 inputDir = new Vector3(h, 0f, v);
            if (inputDir.sqrMagnitude > 1f)
            {
                inputDir.Normalize();
            }

            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                StartLocalAction(1);
                return;
            }

            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                StartLocalAction(2);
                return;
            }

            if (IsActionLocked() && inputDir.sqrMagnitude <= 0.01f)
            {
                StopLocalMovementAndSync();
                ApplySmoothRotation();
                return;
            }

            if (IsActionLocked() && inputDir.sqrMagnitude > 0.01f)
            {
                CancelLocalAction();
            }

            Vector3 intendedVelocity = inputDir * MoveSpeed;
            _characterController.Move(intendedVelocity * Time.deltaTime);
            Vector3 actualVelocity = _characterController.velocity;

            if (inputDir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(new Vector3(inputDir.x, 0f, inputDir.z));
                _targetRotationY = targetRotation.eulerAngles.y;
            }

            ApplySmoothRotation();
            UpdateLocalLocomotion(actualVelocity);

            Vector2 currentInput = new Vector2(h, v);
            bool inputChanged = currentInput != _lastInput;
            bool timeToSync = Time.realtimeSinceStartup - _lastSendTime >= SendInterval;
            bool hasInput = currentInput.sqrMagnitude > 0.01f;

            if (inputChanged || (timeToSync && hasInput))
            {
                _lastInput = currentInput;
                _lastSendTime = Time.realtimeSinceStartup;
                SendMovementSync(actualVelocity);
            }
        }

        private void ApplySmoothRotation()
        {
            if (_localPlayerTransform == null)
            {
                return;
            }

            Quaternion actualTargetRot = Quaternion.Euler(0f, _targetRotationY, 0f);
            _localPlayerTransform.rotation =
                Quaternion.Slerp(_localPlayerTransform.rotation, actualTargetRot, Time.deltaTime * RotationSmoothSpeed);
        }

        private void BindLocalPlayer()
        {
            ClientObjectSyncComponent syncComp = _boundRoom.GetComponent<ClientObjectSyncComponent>();
            if (syncComp == null || NetClient.Session == null)
            {
                return;
            }

            int localNetId = -1;
            var states = syncComp.GetAllSpawnStates();
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i].OwnerSessionId == NetClient.Session.SessionId)
                {
                    localNetId = states[i].NetId;
                    break;
                }
            }

            if (localNetId == -1)
            {
                return;
            }

            ObjectSpawnerView spawner = GetComponent<ObjectSpawnerView>();
            if (spawner == null)
            {
                return;
            }

            GameObject playerObj = spawner.GetSpawnedObject(localNetId);
            if (playerObj == null)
            {
                return;
            }

            _localPlayerTransform = playerObj.transform;
            _targetRotationY = _localPlayerTransform.eulerAngles.y;
            _characterController = playerObj.GetComponent<CharacterController>();
            _localAnimator = playerObj.GetComponentInChildren<Animator>();

            if (_characterController == null)
            {
                _characterController = playerObj.AddComponent<CharacterController>();
                _characterController.center = new Vector3(0f, 1f, 0f);
                _characterController.height = 2f;
                _characterController.radius = 0.5f;
            }
        }

        private void SendMovementSync(Vector3 velocity)
        {
            if (_localPlayerTransform == null)
            {
                return;
            }

            NetClient.Send(new C2S_SocialMoveReq
            {
                PosX = _localPlayerTransform.position.x,
                PosY = _localPlayerTransform.position.y,
                PosZ = _localPlayerTransform.position.z,
                VelX = velocity.x,
                VelY = velocity.y,
                VelZ = velocity.z,
                RotY = _targetRotationY
            });
        }

        private void StopLocalMovementAndSync()
        {
            if (_lastInput != Vector2.zero)
            {
                _lastInput = Vector2.zero;
                _lastSendTime = Time.realtimeSinceStartup;
                SendMovementSync(Vector3.zero);
            }

            if (!IsActionLocked())
            {
                SetLocalVisualState(LocalVisualState.Idle, 0f);
            }
        }

        private void UpdateLocalLocomotion(Vector3 actualVelocity)
        {
            if (IsActionLocked())
            {
                return;
            }

            if (actualVelocity.sqrMagnitude > 0.01f)
            {
                SetLocalVisualState(LocalVisualState.Walk, 0f);
            }
            else
            {
                SetLocalVisualState(LocalVisualState.Idle, 0f);
            }
        }

        private void StartLocalAction(int actionId)
        {
            if (_boundRoom == null)
            {
                return;
            }

            if (actionId == 1)
            {
                _localActionLockUntil = Time.realtimeSinceStartup + WaveDurationSeconds;
                SetLocalVisualState(LocalVisualState.Wave, 0f);
            }
            else if (actionId == 2)
            {
                _localActionLockUntil = Time.realtimeSinceStartup + DanceDurationSeconds;
                SetLocalVisualState(LocalVisualState.Dance, 0f);
            }
            else
            {
                return;
            }

            StopLocalMovementAndSync();
            NetClient.Send(new C2S_SocialActionReq { ActionId = actionId });
        }

        private void CancelLocalAction()
        {
            if (_localActionLockUntil <= 0f)
            {
                return;
            }

            _localActionLockUntil = 0f;
        }

        private bool IsActionLocked()
        {
            if (_localActionLockUntil <= 0f)
            {
                return false;
            }

            if (Time.realtimeSinceStartup < _localActionLockUntil)
            {
                return true;
            }

            _localActionLockUntil = 0f;
            return false;
        }

        private void SetLocalVisualState(LocalVisualState state, float normalizedTime)
        {
            if (_localAnimator == null || _localVisualState == state)
            {
                return;
            }

            _localVisualState = state;
            int targetHash = ResolveAnimatorHash(state);
            if (targetHash == 0)
            {
                return;
            }

            _localAnimator.CrossFadeInFixedTime(targetHash, 0.1f, 0, normalizedTime);
            _localAnimator.speed = 1f;
        }

        private static int ResolveAnimatorHash(LocalVisualState state)
        {
            switch (state)
            {
                case LocalVisualState.Idle:
                    return AnimatorHash_Idle;
                case LocalVisualState.Walk:
                    return AnimatorHash_Walk;
                case LocalVisualState.Wave:
                    return AnimatorHash_Wave;
                case LocalVisualState.Dance:
                    return AnimatorHash_Dance;
                default:
                    return 0;
            }
        }
    }
}
