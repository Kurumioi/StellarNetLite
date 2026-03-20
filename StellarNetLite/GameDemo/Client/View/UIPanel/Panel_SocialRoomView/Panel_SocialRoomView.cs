using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using StellarFramework;
using StellarFramework.UI;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Game.Shared.Protocol;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.ObjectSync;
using StellarNet.Lite.Shared.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UGUIFollow;

/// <summary>
/// 交友房间 UGUI 表现层。
/// 我只负责聊天气泡展示与交互输入，不接管房间生命周期，避免 UI 与业务状态机互相污染。
/// </summary>
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

        if (sendBtn == null)
        {
            Debug.LogError($"[Panel_SocialRoomView] 初始化失败: sendBtn 未绑定, Object:{name}");
            return;
        }

        if (endGameBtn == null)
        {
            Debug.LogError($"[Panel_SocialRoomView] 初始化失败: endGameBtn 未绑定, Object:{name}");
            return;
        }

        if (chatInput == null)
        {
            Debug.LogError($"[Panel_SocialRoomView] 初始化失败: chatInput 未绑定, Object:{name}");
            return;
        }

        if (bubbleContainer == null)
        {
            Debug.LogError($"[Panel_SocialRoomView] 初始化失败: bubbleContainer 未绑定, Object:{name}");
            return;
        }

        if (bubblePrefab == null)
        {
            Debug.LogError($"[Panel_SocialRoomView] 初始化失败: bubblePrefab 未绑定, Object:{name}");
            return;
        }

        sendBtn.onClick.AddListener(OnSendBtnClick);
        endGameBtn.onClick.AddListener(OnEndGameBtnClick);
        chatInput.onSubmit.AddListener(OnChatInputSubmit);

        // 我前置拦截 LayoutGroup，是为了避免跟随型 UI 的 anchoredPosition 被布局系统强行覆盖到中心点。
        if (bubbleContainer.GetComponent<LayoutGroup>() != null)
        {
            Debug.LogError($"[Panel_SocialRoomView] 初始化失败: bubbleContainer {bubbleContainer.name} 挂载了 LayoutGroup，当前状态会破坏气泡跟随坐标，Object:{name}");
            return;
        }
    }

    public override async UniTask OnOpen(object uiData = null)
    {
        await base.OnOpen(uiData);

        _spawnerView = FindObjectOfType<StellarNet.Lite.Client.Components.Views.ObjectSpawnerView>();
        _inputController = FindObjectOfType<StellarNet.Lite.Game.Client.Views.SocialRoomInputController>();
        _localNetId = -1;

        ClearAllBubbles();

        if (NetClient.CurrentRoom != null)
        {
            NetClient.CurrentRoom.NetEventSystem.Register<S2C_SocialBubbleSync>(HandleBubbleSync).UnRegisterWhenMonoDisable(this);
            NetClient.CurrentRoom.NetEventSystem.Register<Local_ObjectSpawned>(HandleObjectSpawned).UnRegisterWhenMonoDisable(this);

            ClientRoomSettingsComponent settingsComp = NetClient.CurrentRoom.GetComponent<ClientRoomSettingsComponent>();
            if (settingsComp != null && NetClient.Session != null && settingsComp.Members.TryGetValue(NetClient.Session.SessionId, out MemberInfo myInfo))
            {
                endGameBtn.gameObject.SetActive(myInfo.IsOwner);
            }
            else
            {
                endGameBtn.gameObject.SetActive(false);
            }
        }

        bool isReplay = NetClient.State == ClientAppState.ReplayRoom;
        chatInput.gameObject.SetActive(!isReplay);
        sendBtn.gameObject.SetActive(!isReplay);

        if (isReplay)
        {
            endGameBtn.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (sendBtn != null)
        {
            sendBtn.onClick.RemoveListener(OnSendBtnClick);
        }

        if (endGameBtn != null)
        {
            endGameBtn.onClick.RemoveListener(OnEndGameBtnClick);
        }

        if (chatInput != null)
        {
            chatInput.onSubmit.RemoveListener(OnChatInputSubmit);
        }

        ClearAllBubbles();
    }

    /// <summary>
    /// 本地对象生成事件回调。
    /// 我改为读取 evt.State，是因为本地生成事件已经收敛为共享完整生成态，不能再继续访问旧的扁平字段副本。
    /// </summary>
    private void HandleObjectSpawned(Local_ObjectSpawned evt)
    {
        if (NetClient.Session == null)
        {
            NetLogger.LogError("Panel_SocialRoomView", $"处理本地对象生成失败: NetClient.Session 为空, Object:{name}");
            return;
        }

        ObjectSpawnState state = evt.State;
        if (state.NetId <= 0)
        {
            NetLogger.LogError("Panel_SocialRoomView", $"处理本地对象生成失败: NetId 非法, NetId:{state.NetId}, OwnerSessionId:{state.OwnerSessionId}, Object:{name}");
            return;
        }

        if (state.OwnerSessionId == NetClient.Session.SessionId)
        {
            _localNetId = state.NetId;
        }
    }

    private void HandleBubbleSync(S2C_SocialBubbleSync evt)
    {
        if (evt == null)
        {
            NetLogger.LogError("Panel_SocialRoomView", $"处理聊天气泡失败: evt 为空, Object:{name}");
            return;
        }

        CreateOrUpdateBubble(evt.NetId, evt.Content);
    }

    private void CreateOrUpdateBubble(int netId, string content)
    {
        if (netId <= 0)
        {
            NetLogger.LogError("Panel_SocialRoomView", $"创建气泡失败: netId 非法, NetId:{netId}, Content:{content}, Object:{name}");
            return;
        }

        if (_activeBubbles.TryGetValue(netId, out SocialRoomBubbleItem existingBubble))
        {
            if (existingBubble != null)
            {
                existingBubble.UpdateContent(content, 4f);
                return;
            }

            _activeBubbles.Remove(netId);
        }

        if (_spawnerView == null)
        {
            _spawnerView = FindObjectOfType<StellarNet.Lite.Client.Components.Views.ObjectSpawnerView>();
        }

        if (_spawnerView == null)
        {
            NetLogger.LogError("Panel_SocialRoomView", $"创建气泡失败: _spawnerView 为空, NetId:{netId}, Content:{content}, Object:{name}");
            return;
        }

        GameObject targetObj = _spawnerView.GetSpawnedObject(netId);
        if (targetObj == null)
        {
            NetLogger.LogWarning("Panel_SocialRoomView", $"创建气泡跳过: 找不到目标实体, NetId:{netId}, Content:{content}, Object:{name}");
            return;
        }

        GameObject uiObj = Instantiate(bubblePrefab, bubbleContainer);
        if (uiObj == null)
        {
            NetLogger.LogError("Panel_SocialRoomView", $"创建气泡失败: Instantiate 返回 null, NetId:{netId}, BubblePrefab:{bubblePrefab.name}, Object:{name}");
            return;
        }

        uiObj.SetActive(true);

        SocialRoomBubbleItem bubbleItem = uiObj.GetComponent<SocialRoomBubbleItem>();
        if (bubbleItem == null)
        {
            bubbleItem = uiObj.AddComponent<SocialRoomBubbleItem>();
        }

        UGUIFollowTarget followTarget = uiObj.GetComponent<UGUIFollowTarget>();
        if (followTarget == null)
        {
            followTarget = uiObj.AddComponent<UGUIFollowTarget>();
        }

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
        if (_activeBubbles.Count == 0)
        {
            return;
        }

        foreach (KeyValuePair<int, SocialRoomBubbleItem> kvp in _activeBubbles)
        {
            if (kvp.Value != null)
            {
                Destroy(kvp.Value.gameObject);
            }
        }

        _activeBubbles.Clear();
    }

    private void OnSendBtnClick()
    {
        SendChat();
    }

    private void OnChatInputSubmit(string text)
    {
        SendChat();

        if (EventSystem.current == null)
        {
            NetLogger.LogError("Panel_SocialRoomView", $"聊天输入提交失败: EventSystem.current 为空, Text:{text}, Object:{name}");
            return;
        }

        EventSystem.current.SetSelectedGameObject(chatInput.gameObject);
        chatInput.ActivateInputField();
    }

    private void SendChat()
    {
        if (chatInput == null)
        {
            NetLogger.LogError("Panel_SocialRoomView", $"发送聊天失败: chatInput 为空, Object:{name}");
            return;
        }

        string safeText = chatInput.text.Trim();
        if (string.IsNullOrEmpty(safeText))
        {
            return;
        }

        if (_inputController != null)
        {
            _inputController.SendChatBubble(safeText);
        }
        else
        {
            NetClient.Send(new C2S_SocialBubbleReq { Content = safeText });
        }

        if (_localNetId != -1)
        {
            string displayContent = safeText.Length > 30 ? safeText.Substring(0, 30) + "..." : safeText;
            CreateOrUpdateBubble(_localNetId, displayContent);
        }

        chatInput.text = string.Empty;
    }

    private void OnEndGameBtnClick()
    {
        if (_inputController != null)
        {
            _inputController.RequestEndGame();
            return;
        }

        NetClient.Send(new C2S_EndGame());
    }
}