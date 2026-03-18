using System.Collections.Generic;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Game.Shared.Protocol;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Client.Components;
using StellarFramework.UI;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Game.Client.Views
{
    /// <summary>
    /// 交友房间局内表现桥接器 (View层)
    /// 职责：采集玩家移动与动作输入、发送聊天文本、在屏幕上渲染跟随角色的聊天气泡。
    /// </summary>
    public class SocialRoomView : MonoBehaviour
    {
        private ClientRoom _boundRoom;
        private ClientObjectSyncComponent _syncService;
        private readonly List<IUnRegister> _roomEventTokens = new List<IUnRegister>();

        private bool _isInputBlockedByWeakNet = false;
        private bool _isSuspended = false;
        private Vector2 _lastInput = Vector2.zero;
        private string _chatInputText = string.Empty;

        private class BubbleData
        {
            public string Content;
            public float ExpireTime;
        }

        private readonly Dictionary<int, BubbleData> _activeBubbles = new Dictionary<int, BubbleData>();
        private readonly List<int> _expiredBubbleKeys = new List<int>();
        private Camera _mainCamera;

        private void Start()
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                _mainCamera = FindObjectOfType<Camera>();
            }

            GlobalTypeNetEvent.Register<Local_NetworkQualityChanged>(HandleNetworkQualityChanged)
                .UnRegisterWhenGameObjectDestroyed(gameObject);
        }

        public void Init(ClientRoom room)
        {
            _boundRoom = room;
            _syncService = _boundRoom.GetComponent<ClientObjectSyncComponent>();
            _isSuspended = false;
            _activeBubbles.Clear();
            BindEvents();
        }

        public void Clear(bool isSuspended)
        {
            UnbindEvents();
            _boundRoom = null;
            _syncService = null;
            _isSuspended = isSuspended;
            if (!isSuspended)
            {
                _activeBubbles.Clear();
            }
        }

        private void HandleNetworkQualityChanged(Local_NetworkQualityChanged evt)
        {
            _isInputBlockedByWeakNet = evt.IsWeakNetBlock;
        }

        private void Update()
        {
            if (_boundRoom == null || _isSuspended) return;

            CleanExpiredBubbles();

            var settingsComp = _boundRoom.GetComponent<ClientRoomSettingsComponent>();
            bool isGameStarted = settingsComp != null && settingsComp.IsGameStarted;

            if (NetClient.State == ClientAppState.OnlineRoom && isGameStarted)
            {
                ProcessInput();
            }
        }

        private void BindEvents()
        {
            _roomEventTokens.Add(_boundRoom.NetEventSystem.Register<S2C_SocialBubbleSync>(HandleBubbleSync));
            _roomEventTokens.Add(_boundRoom.NetEventSystem.Register<S2C_GameEnded>(HandleGameEnded));
        }

        private void UnbindEvents()
        {
            foreach (var token in _roomEventTokens)
            {
                token?.UnRegister();
            }

            _roomEventTokens.Clear();
        }

        private void CleanExpiredBubbles()
        {
            _expiredBubbleKeys.Clear();
            float currentTime = Time.time;
            foreach (var kvp in _activeBubbles)
            {
                if (currentTime > kvp.Value.ExpireTime)
                {
                    _expiredBubbleKeys.Add(kvp.Key);
                }
            }

            for (int i = 0; i < _expiredBubbleKeys.Count; i++)
            {
                _activeBubbles.Remove(_expiredBubbleKeys[i]);
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
                var moveReq = new C2S_SocialMoveReq { DirX = currentInput.x, DirZ = currentInput.y };
                NetClient.Send(moveReq);
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
                var settingsComp = _boundRoom.GetComponent<ClientRoomSettingsComponent>();
                if (settingsComp != null && settingsComp.Members.TryGetValue(NetClient.Session.SessionId, out var myInfo) && myInfo.IsOwner)
                {
                    NetLogger.LogInfo("[SocialRoomView]", "房主按下 F12，请求强制结束交友对局");
                    NetClient.Send(new C2S_EndGame());
                }
            }
        }

        private void HandleBubbleSync(S2C_SocialBubbleSync evt)
        {
            _activeBubbles[evt.NetId] = new BubbleData
            {
                Content = evt.Content,
                ExpireTime = Time.time + 4f
            };
        }

        private void HandleGameEnded(S2C_GameEnded evt)
        {
            _activeBubbles.Clear();
            _chatInputText = string.Empty;
            _lastInput = Vector2.zero;

            if (NetClient.State == ClientAppState.OnlineRoom)
            {
                NetLogger.LogInfo("[SocialRoomView]", "交友对局结束，自动请求离开房间");
                NetClient.Send(new C2S_LeaveRoom());
            }
        }

        private void OnGUI()
        {
            if (_boundRoom == null) return;

            var settingsComp = _boundRoom.GetComponent<ClientRoomSettingsComponent>();
            bool isPlaying = NetClient.State == ClientAppState.ReplayRoom || (settingsComp != null && settingsComp.IsGameStarted);

            if (!isPlaying) return;

            if (_syncService != null && _mainCamera != null)
            {
                GUIStyle bubbleStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 14,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true
                };

                foreach (var kvp in _activeBubbles)
                {
                    if (_syncService.TryGetPredictedData(kvp.Key, out var syncData))
                    {
                        Vector3 worldPos = syncData.Position + Vector3.up * 2.2f;
                        Vector3 screenPos = _mainCamera.WorldToScreenPoint(worldPos);

                        if (screenPos.z > 0)
                        {
                            float guiY = Screen.height - screenPos.y;
                            GUIContent content = new GUIContent(kvp.Value.Content);
                            Vector2 size = bubbleStyle.CalcSize(content);
                            float width = Mathf.Clamp(size.x + 20f, 60f, 200f);
                            float height = bubbleStyle.CalcHeight(content, width) + 10f;

                            Rect bubbleRect = new Rect(screenPos.x - width / 2f, guiY - height, width, height);
                            GUI.Box(bubbleRect, kvp.Value.Content, bubbleStyle);
                        }
                    }
                }
            }

            if (NetClient.State == ClientAppState.OnlineRoom)
            {
                if (settingsComp != null && settingsComp.Members.TryGetValue(NetClient.Session.SessionId, out var myInfo) && myInfo.IsOwner)
                {
                    GUI.color = Color.red;
                    if (GUI.Button(new Rect(Screen.width - 120, 20, 100, 40), "结束交友 (F12)"))
                    {
                        NetClient.Send(new C2S_EndGame());
                    }

                    GUI.color = Color.white;
                }

                GUILayout.BeginArea(new Rect(Screen.width / 2 - 250, Screen.height - 60, 500, 50), GUI.skin.box);
                GUILayout.BeginHorizontal();

                GUI.SetNextControlName("ChatInputField");
                _chatInputText = GUILayout.TextField(_chatInputText, GUILayout.Width(380), GUILayout.Height(30));

                if (GUILayout.Button("发送", GUILayout.Width(80), GUILayout.Height(30)))
                {
                    if (!string.IsNullOrEmpty(_chatInputText))
                    {
                        NetClient.Send(new C2S_SocialBubbleReq { Content = _chatInputText });
                        _chatInputText = string.Empty;
                        GUI.FocusControl(null);
                    }
                }

                GUILayout.EndHorizontal();
                GUILayout.EndArea();

                GUI.Label(new Rect(10, Screen.height - 80, 300, 70),
                    "<b>操作指南:</b>\n[W A S D] 移动\n[1] 挥手动作  [2] 跳舞动作\n点击输入框可发送聊天气泡",
                    new GUIStyle(GUI.skin.label) { richText = true });
            }
        }
    }
}