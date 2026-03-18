using System.Collections.Generic;
using UnityEngine;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Game.Shared.Protocol;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Game.Client.Views
{
    /// <summary>
    /// 交友房间纯表现视图 (View 层)
    /// </summary>
    public class SocialRoomView : MonoBehaviour
    {
        private ClientRoom _boundRoom;
        private ClientObjectSyncComponent _syncService;
        private SocialRoomInputController _controller;
        private readonly List<IUnRegister> _roomEventTokens = new List<IUnRegister>();

        private bool _isSuspended = false;
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
            if (_mainCamera == null) _mainCamera = FindObjectOfType<Camera>();
            _controller = GetComponent<SocialRoomInputController>();
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

        private void Update()
        {
            if (_boundRoom == null || _isSuspended) return;
            CleanExpiredBubbles();
        }

        private void BindEvents()
        {
            _roomEventTokens.Add(_boundRoom.NetEventSystem.Register<S2C_SocialBubbleSync>(HandleBubbleSync));
            _roomEventTokens.Add(_boundRoom.NetEventSystem.Register<S2C_GameEnded>(HandleGameEnded));
        }

        private void UnbindEvents()
        {
            foreach (var token in _roomEventTokens) token?.UnRegister();
            _roomEventTokens.Clear();
        }

        private void CleanExpiredBubbles()
        {
            _expiredBubbleKeys.Clear();
            float currentTime = Time.time;
            foreach (var kvp in _activeBubbles)
            {
                if (currentTime > kvp.Value.ExpireTime) _expiredBubbleKeys.Add(kvp.Key);
            }

            for (int i = 0; i < _expiredBubbleKeys.Count; i++)
            {
                _activeBubbles.Remove(_expiredBubbleKeys[i]);
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
        }

        private void OnGUI()
        {
            if (_boundRoom == null) return;

            var settingsComp = _boundRoom.GetComponent<ClientRoomSettingsComponent>();
            bool isPlaying = NetClient.State == ClientAppState.ReplayRoom || (settingsComp != null && settingsComp.IsGameStarted);

            if (!isPlaying) return;

            DrawBubbles();

            if (NetClient.State == ClientAppState.OnlineRoom)
            {
                DrawChatInput();
                DrawGuide();

                if (settingsComp != null && settingsComp.Members.TryGetValue(NetClient.Session.SessionId, out var myInfo) && myInfo.IsOwner)
                {
                    DrawOwnerPanel();
                }
            }
        }

        private void DrawBubbles()
        {
            if (_syncService == null || _mainCamera == null) return;

            GUIStyle bubbleStyle = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = true };

            foreach (var kvp in _activeBubbles)
            {
                // 核心修复：适配新的 API TryGetTransformData
                if (_syncService.TryGetTransformData(kvp.Key, out var syncData))
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

        private void DrawChatInput()
        {
            GUILayout.BeginArea(new Rect(Screen.width / 2 - 250, Screen.height - 60, 500, 50), GUI.skin.box);
            GUILayout.BeginHorizontal();

            GUI.SetNextControlName("ChatInputField");
            _chatInputText = GUILayout.TextField(_chatInputText, GUILayout.Width(380), GUILayout.Height(30));

            if (GUILayout.Button("发送", GUILayout.Width(80), GUILayout.Height(30)))
            {
                if (!string.IsNullOrEmpty(_chatInputText) && _controller != null)
                {
                    _controller.SendChatBubble(_chatInputText);
                    _chatInputText = string.Empty;
                    GUI.FocusControl(null);
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawGuide()
        {
            GUI.Label(new Rect(10, Screen.height - 80, 300, 70),
                "<b>操作指南:</b>\n[W A S D] 移动\n[1] 挥手动作  [2] 跳舞动作\n点击输入框可发送聊天气泡",
                new GUIStyle(GUI.skin.label) { richText = true });
        }

        private void DrawOwnerPanel()
        {
            GUI.color = Color.red;
            if (GUI.Button(new Rect(Screen.width - 120, 20, 100, 40), "结束交友 (F12)"))
            {
                if (_controller != null) _controller.RequestEndGame();
            }

            GUI.color = Color.white;
        }
    }
}