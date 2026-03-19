using StellarNet.Lite.Client.Components;
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
    /// 交友房间输入控制器。
    /// </summary>
    public class SocialRoomInputController : MonoBehaviour
    {
        private ClientRoom _boundRoom;
        private bool _isInputBlockedByWeakNet;
        private Vector2 _lastInput = Vector2.zero;
        private bool _isInitialized;
        private IUnRegister _networkQualityToken;

        public void Init(ClientRoom room)
        {
            if (room == null)
            {
                NetLogger.LogError("SocialRoomInputController", $"初始化失败: room 为空, Object:{name}");
                return;
            }

            if (_isInitialized)
            {
                if (_boundRoom == room)
                {
                    NetLogger.LogWarning("SocialRoomInputController", $"重复初始化已忽略: RoomId:{room.RoomId}, Object:{name}");
                    return;
                }

                Clear();
            }

            _boundRoom = room;
            _lastInput = Vector2.zero;
            _isInputBlockedByWeakNet = false;
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
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            if (_boundRoom == null)
            {
                NetLogger.LogError("SocialRoomInputController", $"发送聊天失败: _boundRoom 为空, Content:{content}, Object:{name}");
                return;
            }

            NetClient.Send(new C2S_SocialBubbleReq { Content = content });
        }

        public void RequestEndGame()
        {
            if (_boundRoom == null)
            {
                NetLogger.LogError("SocialRoomInputController", $"结束对局失败: _boundRoom 为空, Object:{name}");
                return;
            }

            ClientRoomSettingsComponent settingsComp = _boundRoom.GetComponent<ClientRoomSettingsComponent>();
            if (settingsComp == null)
            {
                NetLogger.LogError("SocialRoomInputController", $"结束对局失败: 缺失 ClientRoomSettingsComponent, RoomId:{_boundRoom.RoomId}, Object:{name}");
                return;
            }

            if (NetClient.Session == null || string.IsNullOrEmpty(NetClient.Session.SessionId))
            {
                NetLogger.LogError("SocialRoomInputController", $"结束对局失败: Session 非法, RoomId:{_boundRoom.RoomId}, Object:{name}");
                return;
            }

            if (!settingsComp.Members.TryGetValue(NetClient.Session.SessionId, out var myInfo))
            {
                NetLogger.LogError("SocialRoomInputController", $"结束对局失败: 当前成员信息不存在, SessionId:{NetClient.Session.SessionId}, RoomId:{_boundRoom.RoomId}, Object:{name}");
                return;
            }

            if (!myInfo.IsOwner)
            {
                NetLogger.LogWarning("SocialRoomInputController", $"结束对局被拦截: 当前玩家不是房主, SessionId:{NetClient.Session.SessionId}, RoomId:{_boundRoom.RoomId}");
                return;
            }

            NetLogger.LogInfo("SocialRoomInputController", $"房主请求强制结束对局, RoomId:{_boundRoom.RoomId}, SessionId:{NetClient.Session.SessionId}");
            NetClient.Send(new C2S_EndGame());
        }

        private void Update()
        {
            if (_boundRoom == null)
            {
                return;
            }

            ClientRoomSettingsComponent settingsComp = _boundRoom.GetComponent<ClientRoomSettingsComponent>();
            bool isGameStarted = settingsComp != null && settingsComp.IsGameStarted;

            if (NetClient.State == ClientAppState.OnlineRoom && isGameStarted)
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

            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            {
                var tmpInput = EventSystem.current.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>();
                if (tmpInput != null && tmpInput.isFocused)
                {
                    if (_lastInput != Vector2.zero)
                    {
                        _lastInput = Vector2.zero;
                        NetClient.Send(new C2S_SocialMoveReq { DirX = 0f, DirZ = 0f });
                    }

                    return;
                }
            }

            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Vector2 currentInput = new Vector2(h, v);

            if (currentInput != _lastInput)
            {
                _lastInput = currentInput;
                NetClient.Send(new C2S_SocialMoveReq
                {
                    DirX = currentInput.x,
                    DirZ = currentInput.y
                });
            }

            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                NetClient.Send(new C2S_SocialActionReq { ActionId = 1 });
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                NetClient.Send(new C2S_SocialActionReq { ActionId = 2 });
            }

            if (Input.GetKeyDown(KeyCode.F12))
            {
                RequestEndGame();
            }
        }
    }
}