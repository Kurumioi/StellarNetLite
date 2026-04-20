using System.Collections.Generic;
using StellarNet.UI;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Game.Shared.Protocol;
using StellarNet.Lite.Shared.ObjectSync;
using StellarNet.Lite.Shared.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UGUIFollow;

public class Panel_SocialRoomView : UIPanelBase
{
    [Header("UI 引用")] [SerializeField] private TMP_InputField chatInput;
    [SerializeField] private Button sendBtn;
    [SerializeField] private Button endGameBtn;
    [SerializeField] private RectTransform bubbleContainer;
    [SerializeField] private GameObject bubblePrefab;

    private StellarNet.Lite.Client.Components.Views.ObjectSpawnerView _spawnerView;
    private StellarNet.Lite.Game.Client.Views.SocialRoomInputController _inputController;

    private int _localNetId = -1;
    private readonly Dictionary<int, SocialRoomBubbleItem> _activeBubbles = new Dictionary<int, SocialRoomBubbleItem>();

    public override void OnInit()
    {
        base.OnInit();
        sendBtn.onClick.AddListener(OnSendBtnClick);
        endGameBtn.onClick.AddListener(OnEndGameBtnClick);
        chatInput.onSubmit.AddListener(OnChatInputSubmit);
    }

    public override void OnOpen(object uiData = null)
    {
        base.OnOpen(uiData);
        _localNetId = -1;
        ClearAllBubbles();

        if (NetClient.CurrentRoom != null)
        {
            _spawnerView = FindObjectOfType<StellarNet.Lite.Client.Components.Views.ObjectSpawnerView>();
            _inputController = gameObject.GetComponent<StellarNet.Lite.Game.Client.Views.SocialRoomInputController>();
            if (_inputController == null) _inputController = gameObject.AddComponent<StellarNet.Lite.Game.Client.Views.SocialRoomInputController>();
            
            _inputController.Init(NetClient.CurrentRoom);

            NetClient.CurrentRoom.NetEventSystem.Register<S2C_SocialBubbleSync>(HandleBubbleSync).UnRegisterWhenMonoDisable(this);
            NetClient.CurrentRoom.NetEventSystem.Register<S2C_ObjectSpawn>(HandleObjectSpawned).UnRegisterWhenMonoDisable(this);
            // 监听房间快照，用于房主顺位时的 UI 动态刷新
            NetClient.CurrentRoom.NetEventSystem.Register<S2C_RoomSnapshot>(HandleRoomSnapshot).UnRegisterWhenMonoDisable(this);

            var syncComp = NetClient.CurrentRoom.GetComponent<ClientObjectSyncComponent>();
            if (syncComp != null && NetClient.Session != null)
            {
                var states = syncComp.GetAllSpawnStates();
                for (int i = 0; i < states.Count; i++)
                {
                    if (states[i].OwnerSessionId == NetClient.Session.SessionId)
                    {
                        _localNetId = states[i].NetId;
                        break;
                    }
                }
            }

            RefreshOwnerUI();
        }

        bool isReplay = NetClient.State == ClientAppState.ReplayRoom;
        chatInput.gameObject.SetActive(!isReplay);
        sendBtn.gameObject.SetActive(!isReplay);
        if (isReplay) endGameBtn.gameObject.SetActive(false);
    }

    public override void OnClose()
    {
        base.OnClose();
        _spawnerView = null;
        _inputController?.Clear();
        ClearAllBubbles();
    }

    private void OnDestroy()
    {
        sendBtn?.onClick.RemoveListener(OnSendBtnClick);
        endGameBtn?.onClick.RemoveListener(OnEndGameBtnClick);
        chatInput?.onSubmit.RemoveListener(OnChatInputSubmit);
    }

    private void HandleObjectSpawned(S2C_ObjectSpawn evt)
    {
        if (evt != null && NetClient.Session != null && evt.State.OwnerSessionId == NetClient.Session.SessionId)
        {
            _localNetId = evt.State.NetId;
        }
    }

    private void HandleBubbleSync(S2C_SocialBubbleSync evt)
    {
        if (evt != null) CreateOrUpdateBubble(evt.NetId, evt.Content);
    }

    private void HandleRoomSnapshot(S2C_RoomSnapshot msg)
    {
        // 收到快照时，重新评估房主权限
        RefreshOwnerUI();
    }

    private void RefreshOwnerUI()
    {
        if (NetClient.State == ClientAppState.ReplayRoom)
        {
            endGameBtn.gameObject.SetActive(false);
            return;
        }

        var settingsComp = NetClient.CurrentRoom?.GetComponent<ClientRoomSettingsComponent>();
        if (settingsComp != null && NetClient.Session != null &&
            settingsComp.Members.TryGetValue(NetClient.Session.SessionId, out MemberInfo myInfo))
        {
            endGameBtn.gameObject.SetActive(myInfo.IsOwner);
        }
        else
        {
            endGameBtn.gameObject.SetActive(false);
        }
    }

    private void CreateOrUpdateBubble(int netId, string content)
    {
        if (netId <= 0) return;

        if (_activeBubbles.TryGetValue(netId, out SocialRoomBubbleItem existingBubble))
        {
            if (existingBubble != null)
            {
                existingBubble.UpdateContent(content, 4f);
                return;
            }
            _activeBubbles.Remove(netId);
        }

        if (_spawnerView == null) return;
        GameObject targetObj = _spawnerView.GetSpawnedObject(netId);
        if (targetObj == null) return;

        GameObject uiObj = Instantiate(bubblePrefab, bubbleContainer);
        uiObj.SetActive(true);

        SocialRoomBubbleItem bubbleItem = uiObj.GetComponent<SocialRoomBubbleItem>();
        if (bubbleItem == null) bubbleItem = uiObj.AddComponent<SocialRoomBubbleItem>();

        UGUIFollowTarget followTarget = uiObj.GetComponent<UGUIFollowTarget>();
        if (followTarget == null) followTarget = uiObj.AddComponent<UGUIFollowTarget>();

        followTarget.targetTransform = targetObj.transform;
        followTarget.worldOffset = new Vector3(0f, 2.5f, 0f);
        followTarget.followMode = FollowMode.Smooth;
        followTarget.smoothSpeed = 15f;
        followTarget.Init();

        bubbleItem.Init(content, 4f);
        _activeBubbles[netId] = bubbleItem;
    }

    private void ClearAllBubbles()
    {
        foreach (var kvp in _activeBubbles)
        {
            if (kvp.Value != null) Destroy(kvp.Value.gameObject);
        }
        _activeBubbles.Clear();
    }

    private void OnSendBtnClick() => SendChat();

    private void OnChatInputSubmit(string text)
    {
        SendChat();
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(chatInput.gameObject);
            chatInput.ActivateInputField();
        }
    }

    private void SendChat()
    {
        if (chatInput == null) return;
        string safeText = chatInput.text.Trim();
        if (string.IsNullOrEmpty(safeText)) return;

        if (_inputController != null) _inputController.SendChatBubble(safeText);
        else NetClient.Send(new C2S_SocialBubbleReq { Content = safeText });

        if (_localNetId != -1)
        {
            string displayContent = safeText.Length > 30 ? safeText.Substring(0, 30) + "..." : safeText;
            CreateOrUpdateBubble(_localNetId, displayContent);
        }

        chatInput.text = string.Empty;
    }

    private void OnEndGameBtnClick()
    {
        if (_inputController != null) _inputController.RequestEndGame();
        else NetClient.Send(new C2S_EndGame());
    }
}
