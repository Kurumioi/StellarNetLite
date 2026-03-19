using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using StellarFramework;
using StellarFramework.UI;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Game.Shared.Protocol;
using StellarNet.Lite.Shared.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UGUIFollow; // 引入跟随模块命名空间

/// <summary>
/// 交友房间 UGUI 表现层
/// 核心重构：彻底剥离生命周期控制（如监听 GameEnded 关闭自身），回归纯粹的展示与交互职责。
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
        sendBtn.onClick.AddListener(OnSendBtnClick);
        endGameBtn.onClick.AddListener(OnEndGameBtnClick);
        chatInput.onSubmit.AddListener(OnChatInputSubmit);

        // 前置拦截：防止 LayoutGroup 破坏坐标跟随逻辑
        if (bubbleContainer != null && bubbleContainer.GetComponent<LayoutGroup>() != null)
        {
            Debug.LogError($"[Panel_SocialRoomView] 严重错误: bubbleContainer ({bubbleContainer.name}) 上挂载了 LayoutGroup 组件！这会导致气泡坐标被强制覆盖在中心点。请在预制体中移除该组件！");
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

            // 核心修复：移除对 S2C_GameEnded 的监听，关闭面板的职责已移交至 SocialOnlineUIRouter

            var settingsComp = NetClient.CurrentRoom.GetComponent<ClientRoomSettingsComponent>();
            if (settingsComp != null && settingsComp.Members.TryGetValue(NetClient.Session.SessionId, out var myInfo))
            {
                endGameBtn.gameObject.SetActive(myInfo.IsOwner);
            }
        }

        // 表现层自适应：如果是回放模式，隐藏交互控件
        bool isReplay = NetClient.State == ClientAppState.ReplayRoom;
        chatInput.gameObject.SetActive(!isReplay);
        sendBtn.gameObject.SetActive(!isReplay);
        if (isReplay) endGameBtn.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        sendBtn.onClick.RemoveAllListeners();
        endGameBtn.onClick.RemoveAllListeners();
        chatInput.onSubmit.RemoveAllListeners();
    }

    private void HandleObjectSpawned(Local_ObjectSpawned evt)
    {
        if (NetClient.Session != null && evt.OwnerSessionId == NetClient.Session.SessionId)
        {
            _localNetId = evt.NetId;
        }
    }

    private void HandleBubbleSync(S2C_SocialBubbleSync evt)
    {
        CreateOrUpdateBubble(evt.NetId, evt.Content);
    }

    private void CreateOrUpdateBubble(int netId, string content)
    {
        // 若气泡已存在且未被销毁，直接更新内容与时间
        if (_activeBubbles.TryGetValue(netId, out var existingBubble))
        {
            if (existingBubble != null)
            {
                existingBubble.UpdateContent(content, 4f);
                return;
            }
            else
            {
                _activeBubbles.Remove(netId);
            }
        }

        if (_spawnerView == null) _spawnerView = FindObjectOfType<StellarNet.Lite.Client.Components.Views.ObjectSpawnerView>();
        if (_spawnerView == null) return;

        GameObject targetObj = _spawnerView.GetSpawnedObject(netId);
        if (targetObj == null)
        {
            Debug.LogWarning($"[Panel_SocialRoomView] 无法创建气泡: 找不到 NetId {netId} 对应的实体对象。");
            return;
        }

        GameObject uiObj = Instantiate(bubblePrefab, bubbleContainer);
        uiObj.SetActive(true);

        // 动态挂载独立气泡脚本，负责表现与生命周期
        SocialRoomBubbleItem bubbleItem = uiObj.GetComponent<SocialRoomBubbleItem>();
        if (bubbleItem == null)
        {
            bubbleItem = uiObj.AddComponent<SocialRoomBubbleItem>();
        }

        // 动态挂载跟随组件，负责底层坐标系映射与对齐
        UGUIFollowTarget followTarget = uiObj.GetComponent<UGUIFollowTarget>();
        if (followTarget == null)
        {
            followTarget = uiObj.AddComponent<UGUIFollowTarget>();
        }

        // 配置跟随参数：设置目标、偏移量与平滑模式
        followTarget.targetTransform = targetObj.transform;
        followTarget.worldOffset = new Vector3(0, 2.5f, 0); // 假设头顶偏移量为 2.5 米，可根据实际模型高度调整
        followTarget.followMode = FollowMode.Smooth; // 气泡推荐使用平滑跟随，避免角色瞬移时 UI 撕裂
        followTarget.smoothSpeed = 15f;
        followTarget.Init();

        // 初始化气泡业务逻辑
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

    private void OnSendBtnClick()
    {
        SendChat();
    }

    private void OnChatInputSubmit(string text)
    {
        SendChat();
        EventSystem.current.SetSelectedGameObject(chatInput.gameObject);
        chatInput.ActivateInputField();
    }

    private void SendChat()
    {
        string safeText = chatInput.text.Trim();
        if (string.IsNullOrEmpty(safeText)) return;

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
        }
        else
        {
            NetClient.Send(new C2S_EndGame());
        }
    }
}