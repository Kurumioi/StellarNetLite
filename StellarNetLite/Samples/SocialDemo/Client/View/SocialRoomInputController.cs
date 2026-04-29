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
        /// <summary>
        /// 本地即时表现状态。
        /// 只控制客户端自己的动画切换。
        /// </summary>
        private enum LocalVisualState : byte
        {
            Idle = 0,
            Walk = 1,
            Wave = 2,
            Dance = 3
        }

        /// <summary>
        /// 当前绑定的房间实例。
        /// </summary>
        private ClientRoom _boundRoom;

        /// <summary>
        /// 弱网阻断期间是否禁止输入上报。
        /// </summary>
        private bool _isInputBlockedByWeakNet;

        /// <summary>
        /// 上一次已同步到服务端的输入轴值。
        /// </summary>
        private Vector2 _lastInput = Vector2.zero;

        /// <summary>
        /// 是否已完成初始化。
        /// </summary>
        private bool _isInitialized;

        /// <summary>
        /// 弱网状态事件的注销句柄。
        /// </summary>
        private IUnRegister _networkQualityToken;

        /// <summary>
        /// 本地玩家实体的 Transform。
        /// </summary>
        private Transform _localPlayerTransform;

        /// <summary>
        /// 本地玩家角色控制器。
        /// </summary>
        private CharacterController _characterController;

        /// <summary>
        /// 本地玩家动画控制器。
        /// </summary>
        private Animator _localAnimator;

        /// <summary>
        /// 上一次发送移动同步的时间。
        /// </summary>
        private float _lastSendTime;

        /// <summary>
        /// 移动同步最小发送间隔。
        /// </summary>
        private const float SendInterval = 1f / 30f;

        /// <summary>
        /// 本地移动速度。
        /// </summary>
        private const float MoveSpeed = 4.0f;

        /// <summary>
        /// 朝向插值速度。
        /// </summary>
        private const float RotationSmoothSpeed = 15f;

        /// <summary>
        /// 挥手动作锁定时长。
        /// </summary>
        private const float WaveDurationSeconds = 2.5f;

        /// <summary>
        /// 跳舞动作锁定时长。
        /// </summary>
        private const float DanceDurationSeconds = 4.0f;

        /// <summary>
        /// Idle 动画状态哈希。
        /// </summary>
        private static readonly int AnimatorHash_Idle = Animator.StringToHash("Idle");

        /// <summary>
        /// Walk 动画状态哈希。
        /// </summary>
        private static readonly int AnimatorHash_Walk = Animator.StringToHash("Walk");

        /// <summary>
        /// Wave 动画状态哈希。
        /// </summary>
        private static readonly int AnimatorHash_Wave = Animator.StringToHash("Wave");

        /// <summary>
        /// Dance 动画状态哈希。
        /// </summary>
        private static readonly int AnimatorHash_Dance = Animator.StringToHash("Dance");

        /// <summary>
        /// 当前目标朝向角度。
        /// </summary>
        private float _targetRotationY;

        /// <summary>
        /// 动作锁定结束时间。
        /// 动作锁定期间禁止被 Idle 覆盖。
        /// </summary>
        private float _localActionLockUntil;

        /// <summary>
        /// 当前本地动画表现状态。
        /// </summary>
        private LocalVisualState _localVisualState = LocalVisualState.Idle;

        /// <summary>
        /// 绑定房间并重置本地输入状态。
        /// </summary>
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

        /// <summary>
        /// 清理房间绑定和本地缓存。
        /// </summary>
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

        /// <summary>
        /// 销毁时释放事件和本地状态。
        /// </summary>
        private void OnDestroy()
        {
            Clear();
        }

        /// <summary>
        /// 同步弱网阻断标记。
        /// </summary>
        private void HandleNetworkQualityChanged(Local_NetworkQualityChanged evt)
        {
            _isInputBlockedByWeakNet = evt.IsWeakNetBlock;
        }

        /// <summary>
        /// 发送聊天气泡消息。
        /// </summary>
        public void SendChatBubble(string content)
        {
            if (string.IsNullOrWhiteSpace(content) || _boundRoom == null)
            {
                return;
            }

            NetClient.Send(new C2S_SocialBubbleReq { Content = content });
        }

        /// <summary>
        /// 尝试请求结束当前对局。
        /// 仅房主可生效。
        /// </summary>
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

        /// <summary>
        /// 仅在在线房间且已开局时处理本地输入。
        /// </summary>
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

        /// <summary>
        /// 处理本地移动、动作与同步上报。
        /// </summary>
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

        /// <summary>
        /// 以平滑插值方式应用本地朝向。
        /// </summary>
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

        /// <summary>
        /// 在当前房间实体列表中找到本地玩家对象并缓存引用。
        /// </summary>
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

        /// <summary>
        /// 上报当前真实位置、速度和朝向。
        /// </summary>
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

        /// <summary>
        /// 停止本地移动并补发一次零速度同步。
        /// </summary>
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

        /// <summary>
        /// 按真实速度刷新本地移动动画。
        /// </summary>
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

        /// <summary>
        /// 启动本地动作播放并通知服务端。
        /// </summary>
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

        /// <summary>
        /// 取消当前动作锁定。
        /// </summary>
        private void CancelLocalAction()
        {
            if (_localActionLockUntil <= 0f)
            {
                return;
            }

            _localActionLockUntil = 0f;
        }

        /// <summary>
        /// 判断当前是否处于动作锁定期。
        /// </summary>
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

        /// <summary>
        /// 切换本地动画表现状态。
        /// </summary>
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

        /// <summary>
        /// 将本地表现状态映射到 Animator 状态哈希。
        /// </summary>
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
