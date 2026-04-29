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

/// <summary>
/// 社交房间中的操作与气泡展示面板。
/// </summary>
public class Panel_SocialRoomView : UIPanelBase
{
    /// <summary>
    /// 聊天输入框。
    /// </summary>
    [Header("UI 引用")] [SerializeField] private TMP_InputField chatInput;

    /// <summary>
    /// 发送聊天按钮。
    /// </summary>
    [SerializeField] private Button sendBtn;

    /// <summary>
    /// 房主结束对局按钮。
    /// </summary>
    [SerializeField] private Button endGameBtn;

    /// <summary>
    /// 离开房间按钮。
    /// </summary>
    [SerializeField] private Button leaveRoomBtn;

    /// <summary>
    /// 气泡 UI 根节点。
    /// </summary>
    [SerializeField] private RectTransform bubbleContainer;

    /// <summary>
    /// 单个聊天气泡预制体。
    /// </summary>
    [SerializeField] private GameObject bubblePrefab;

    /// <summary>
    /// 房间实体生成视图。
    /// 用于按 NetId 查找场景对象。
    /// </summary>
    private StellarNet.Lite.Client.Components.Views.ObjectSpawnerView _spawnerView;

    /// <summary>
    /// 房间输入控制器。
    /// </summary>
    private StellarNet.Lite.Game.Client.Views.SocialRoomInputController _inputController;

    /// <summary>
    /// 本地玩家对应的 NetId。
    /// </summary>
    private int _localNetId = -1;

    /// <summary>
    /// 当前已创建的气泡实例。
    /// Key 为玩家 NetId。
    /// </summary>
    private readonly Dictionary<int, SocialRoomBubbleItem> _activeBubbles = new Dictionary<int, SocialRoomBubbleItem>();

    /// <summary>
    /// 初始化面板事件。
    /// </summary>
    public override void OnInit()
    {
        base.OnInit();
        sendBtn.onClick.AddListener(OnSendBtnClick);
        endGameBtn.onClick.AddListener(OnEndGameBtnClick);
        leaveRoomBtn.onClick.AddListener(OnLeaveRoomBtnClick);
        chatInput.onSubmit.AddListener(OnChatInputSubmit);
    }

    /// <summary>
    /// 打开面板时绑定房间事件并刷新按钮状态。
    /// </summary>
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

        bool isReplay = NetClient.State == ClientAppState.SandboxRoom;
        chatInput.gameObject.SetActive(!isReplay);
        sendBtn.gameObject.SetActive(!isReplay);
        if (isReplay) endGameBtn.gameObject.SetActive(false);
    }

    /// <summary>
    /// 关闭面板时清理房间级缓存。
    /// </summary>
    public override void OnClose()
    {
        base.OnClose();
        _spawnerView = null;
        _inputController?.Clear();
        ClearAllBubbles();
    }

    /// <summary>
    /// 销毁时解除 UI 事件绑定。
    /// </summary>
    private void OnDestroy()
    {
        sendBtn?.onClick.RemoveListener(OnSendBtnClick);
        leaveRoomBtn?.onClick.RemoveListener(OnLeaveRoomBtnClick);
        endGameBtn?.onClick.RemoveListener(OnEndGameBtnClick);
        chatInput?.onSubmit.RemoveListener(OnChatInputSubmit);
    }

    /// <summary>
    /// 记录本地玩家刚生成出来的 NetId。
    /// </summary>
    private void HandleObjectSpawned(S2C_ObjectSpawn evt)
    {
        if (evt != null && NetClient.Session != null && evt.State.OwnerSessionId == NetClient.Session.SessionId)
        {
            _localNetId = evt.State.NetId;
        }
    }

    /// <summary>
    /// 收到房间气泡同步后刷新对应玩家的头顶气泡。
    /// </summary>
    private void HandleBubbleSync(S2C_SocialBubbleSync evt)
    {
        if (evt != null) CreateOrUpdateBubble(evt.NetId, evt.Content);
    }

    /// <summary>
    /// 收到房间快照时重新刷新房主权限按钮。
    /// </summary>
    private void HandleRoomSnapshot(S2C_RoomSnapshot msg)
    {
        // 收到快照时，重新评估房主权限
        RefreshOwnerUI();
    }

    /// <summary>
    /// 根据当前成员身份决定是否显示结束对局按钮。
    /// </summary>
    private void RefreshOwnerUI()
    {
        if (NetClient.State == ClientAppState.SandboxRoom)
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

    /// <summary>
    /// 为目标玩家创建或更新聊天气泡。
    /// </summary>
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

    /// <summary>
    /// 清理当前面板持有的全部气泡实例。
    /// </summary>
    private void ClearAllBubbles()
    {
        foreach (var kvp in _activeBubbles)
        {
            if (kvp.Value != null) Destroy(kvp.Value.gameObject);
        }

        _activeBubbles.Clear();
    }

    private void OnSendBtnClick() => SendChat();

    /// <summary>
    /// 输入框回车后发送聊天，并把焦点还给输入框。
    /// </summary>
    private void OnChatInputSubmit(string text)
    {
        SendChat();
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(chatInput.gameObject);
            chatInput.ActivateInputField();
        }
    }

    /// <summary>
    /// 发送聊天内容，并在本地立即补一条自己的气泡表现。
    /// </summary>
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

    /// <summary>
    /// 请求结束当前对局。
    /// </summary>
    private void OnEndGameBtnClick()
    {
        if (_inputController != null) _inputController.RequestEndGame();
        else NetClient.Send(new C2S_EndGame());
    }

    /// <summary>
    /// 请求离开当前房间。
    /// </summary>
    private void OnLeaveRoomBtnClick()
    {
        NetClient.Send(new C2S_DisconnectRoom());
    }
}
