using UnityEngine;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Game.Shared.Protocol;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Game.Client.Views
{
    /// <summary>
    /// 交友房间输入控制器 (Controller 层)
    /// 核心修复 P1-2：接管所有意图采集，包括聊天发送和结束游戏。
    /// </summary>
    public class SocialRoomInputController : MonoBehaviour
    {
        private ClientRoom _boundRoom;
        private bool _isInputBlockedByWeakNet = false;
        private Vector2 _lastInput = Vector2.zero;

        public void Init(ClientRoom room)
        {
            _boundRoom = room;
            _lastInput = Vector2.zero;
            GlobalTypeNetEvent.Register<Local_NetworkQualityChanged>(HandleNetworkQualityChanged)
                .UnRegisterWhenGameObjectDestroyed(gameObject);
        }

        public void Clear()
        {
            _boundRoom = null;
        }

        private void HandleNetworkQualityChanged(Local_NetworkQualityChanged evt)
        {
            _isInputBlockedByWeakNet = evt.IsWeakNetBlock;
        }

        // 核心修复 P1-2：提供给 View 层的公共接口，供 UI 按钮/输入框回调
        public void SendChatBubble(string content)
        {
            if (string.IsNullOrEmpty(content) || _boundRoom == null) return;
            NetClient.Send(new C2S_SocialBubbleReq { Content = content });
        }

        public void RequestEndGame()
        {
            if (_boundRoom == null) return;
            var settingsComp = _boundRoom.GetComponent<ClientRoomSettingsComponent>();
            if (settingsComp != null && settingsComp.Members.TryGetValue(NetClient.Session.SessionId, out var myInfo) && myInfo.IsOwner)
            {
                NetLogger.LogInfo("SocialRoomInputController", "房主请求强制结束交友对局");
                NetClient.Send(new C2S_EndGame());
            }
        }

        private void Update()
        {
            if (_boundRoom == null) return;

            var settingsComp = _boundRoom.GetComponent<ClientRoomSettingsComponent>();
            bool isGameStarted = settingsComp != null && settingsComp.IsGameStarted;

            if (NetClient.State == ClientAppState.OnlineRoom && isGameStarted)
            {
                ProcessInput();
            }
        }

        private void ProcessInput()
        {
            if (_isInputBlockedByWeakNet) return;

            if (GUI.GetNameOfFocusedControl() == "ChatInputField")
            {
                if (_lastInput != Vector2.zero)
                {
                    _lastInput = Vector2.zero;
                    NetClient.Send(new C2S_SocialMoveReq { DirX = 0, DirZ = 0 });
                }

                return;
            }

            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Vector2 currentInput = new Vector2(h, v);

            if (currentInput != _lastInput)
            {
                _lastInput = currentInput;
                NetClient.Send(new C2S_SocialMoveReq { DirX = currentInput.x, DirZ = currentInput.y });
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