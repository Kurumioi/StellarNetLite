using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Client.Components.Views;
using StellarNet.Lite.Game.Shared.Protocol;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;
using UnityEngine.EventSystems;

namespace StellarNet.Lite.Game.Client.Views
{
    public class SocialRoomInputController : MonoBehaviour
    {
        private ClientRoom _boundRoom;
        private bool _isInputBlockedByWeakNet;
        private Vector2 _lastInput = Vector2.zero;
        private bool _isInitialized;
        private IUnRegister _networkQualityToken;

        private Transform _localPlayerTransform;
        private CharacterController _characterController;
        private float _lastSendTime;

        private const float SendInterval = 0.05f;
        private const float MoveSpeed = 4.0f;
        private const float RotationSmoothSpeed = 15f;
        private float _targetRotationY;

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
            _lastSendTime = 0f;
            _targetRotationY = 0f;

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
            if (string.IsNullOrWhiteSpace(content)) return;
            if (_boundRoom == null) return;

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
            if (_boundRoom == null) return;

            ClientRoomSettingsComponent settingsComp = _boundRoom.GetComponent<ClientRoomSettingsComponent>();
            if (settingsComp != null && settingsComp.IsGameStarted && NetClient.State == ClientAppState.OnlineRoom)
            {
                ProcessInput();
            }
        }

        private void ProcessInput()
        {
            if (_isInputBlockedByWeakNet) return;

            if (_localPlayerTransform == null)
            {
                BindLocalPlayer();
            }

            if (_localPlayerTransform == null || _characterController == null) return;

            // 拦截 UI 输入焦点
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            {
                var tmpInput = EventSystem.current.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>();
                if (tmpInput != null && tmpInput.isFocused)
                {
                    if (_lastInput != Vector2.zero)
                    {
                        _lastInput = Vector2.zero;
                        SendMovementSync(Vector3.zero);
                    }

                    ApplySmoothRotation();
                    return;
                }
            }

            // 采集纯粹的本地输入并驱动本地物理胶囊体
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Vector3 inputDir = new Vector3(h, 0f, v);

            if (inputDir.sqrMagnitude > 1f) inputDir.Normalize();

            Vector3 intendedVelocity = inputDir * MoveSpeed;
            _characterController.Move(intendedVelocity * Time.deltaTime);
            Vector3 actualVelocity = _characterController.velocity;

            if (inputDir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(new Vector3(inputDir.x, 0f, inputDir.z));
                _targetRotationY = targetRotation.eulerAngles.y;
            }

            ApplySmoothRotation();

            // 仅负责将当前状态发送给服务端，不关心网络对账
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

            if (Input.GetKeyDown(KeyCode.Alpha1)) NetClient.Send(new C2S_SocialActionReq { ActionId = 1 });
            else if (Input.GetKeyDown(KeyCode.Alpha2)) NetClient.Send(new C2S_SocialActionReq { ActionId = 2 });
            if (Input.GetKeyDown(KeyCode.F12)) RequestEndGame();
        }

        private void ApplySmoothRotation()
        {
            Quaternion actualTargetRot = Quaternion.Euler(0f, _targetRotationY, 0f);
            _localPlayerTransform.rotation = Quaternion.Slerp(_localPlayerTransform.rotation, actualTargetRot, Time.deltaTime * RotationSmoothSpeed);
        }

        private void BindLocalPlayer()
        {
            var syncComp = _boundRoom.GetComponent<ClientObjectSyncComponent>();
            if (syncComp == null || NetClient.Session == null) return;

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

            if (localNetId == -1) return;

            var spawner = GetComponent<ObjectSpawnerView>();
            if (spawner == null) return;

            GameObject playerObj = spawner.GetSpawnedObject(localNetId);
            if (playerObj != null)
            {
                _localPlayerTransform = playerObj.transform;
                _targetRotationY = _localPlayerTransform.eulerAngles.y;
                _characterController = playerObj.GetComponent<CharacterController>();

                if (_characterController == null)
                {
                    _characterController = playerObj.AddComponent<CharacterController>();
                    _characterController.center = new Vector3(0f, 1f, 0f);
                    _characterController.height = 2f;
                    _characterController.radius = 0.5f;
                }
            }
        }

        private void SendMovementSync(Vector3 velocity)
        {
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
    }
}