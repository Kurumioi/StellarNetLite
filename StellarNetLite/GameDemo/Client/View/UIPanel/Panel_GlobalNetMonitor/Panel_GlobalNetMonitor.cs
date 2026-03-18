using Cysharp.Threading.Tasks;
using StellarFramework.UI;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Panel_GlobalNetMonitor : UIPanelBase
{
    [Header("RTT 监控")] [SerializeField] private GameObject rttGroup;
    [SerializeField] private TMP_Text rttText;

    [Header("弱网阻断遮罩 (半透明)")] [SerializeField]
    private GameObject weakNetGroup;

    [Header("断线挂起遮罩 (强遮罩)")] [SerializeField]
    private GameObject suspendGroup;

    [SerializeField] private TMP_Text suspendTimeText;

    [Header("超时决断框")] [SerializeField] private GameObject timeoutGroup;
    [SerializeField] private Button continueBtn;
    [SerializeField] private Button exitBtn;

    [Header("系统提示框")] [SerializeField] private GameObject promptGroup;
    [SerializeField] private TMP_Text promptText;
    [SerializeField] private Button promptOkBtn;

    public override void OnInit()
    {
        base.OnInit();
        continueBtn.onClick.AddListener(OnContinueBtnClick);
        exitBtn.onClick.AddListener(OnExitBtnClick);
        promptOkBtn.onClick.AddListener(OnPromptOkBtnClick);

        GlobalTypeNetEvent.Register<Local_NetworkQualityChanged>(HandleNetworkQuality)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
        GlobalTypeNetEvent.Register<Local_ConnectionSuspended>(HandleConnectionSuspended)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
        GlobalTypeNetEvent.Register<Local_ReconnectTimeout>(HandleReconnectTimeout)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
        GlobalTypeNetEvent.Register<Local_SystemPrompt>(HandleSystemPrompt)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
    }

    public override async UniTask OnOpen(object uiData = null)
    {
        await base.OnOpen(uiData);
        ResetAllUI();
    }

    private void OnDestroy()
    {
        continueBtn.onClick.RemoveAllListeners();
        exitBtn.onClick.RemoveAllListeners();
        promptOkBtn.onClick.RemoveAllListeners();
    }

    private void Update()
    {
        var state = NetClient.State;

        rttGroup.SetActive(state == ClientAppState.OnlineRoom);

        if (state != ClientAppState.ConnectionSuspended)
        {
            if (suspendGroup.activeSelf) suspendGroup.SetActive(false);
            if (timeoutGroup.activeSelf) timeoutGroup.SetActive(false);
        }
    }

    private void ResetAllUI()
    {
        rttGroup.SetActive(false);
        weakNetGroup.SetActive(false);
        suspendGroup.SetActive(false);
        timeoutGroup.SetActive(false);
        promptGroup.SetActive(false);
    }

    #region 事件处理逻辑

    private void HandleNetworkQuality(Local_NetworkQualityChanged evt)
    {
        if (NetClient.State != ClientAppState.OnlineRoom)
        {
            weakNetGroup.SetActive(false);
            return;
        }

        rttText.text = $"{evt.RttMs} ms";
        if (evt.RttMs < 35)
        {
            rttText.color = Color.green;
        }
        else if (evt.RttMs < 100)
        {
            rttText.color = Color.yellow;
        }
        else
        {
            rttText.color = Color.red;
        }

        weakNetGroup.SetActive(evt.IsWeakNetBlock);
    }

    private void HandleConnectionSuspended(Local_ConnectionSuspended evt)
    {
        weakNetGroup.SetActive(false);
        suspendGroup.SetActive(true);
        suspendTimeText.text = $"正在尝试恢复... ({evt.RemainingSeconds:F1}s)";
    }

    private void HandleReconnectTimeout(Local_ReconnectTimeout evt)
    {
        suspendGroup.SetActive(false);
        timeoutGroup.SetActive(true);
    }

    private void HandleSystemPrompt(Local_SystemPrompt evt)
    {
        promptText.text = evt.Message;
        promptGroup.SetActive(true);
    }

    #endregion

    #region 按钮交互逻辑

    private void OnContinueBtnClick()
    {
        timeoutGroup.SetActive(false);
        // 物理重连机制由 Manager 维护，保留调用
        GameLauncher.NetManager.RestartReconnectionRoutine();
    }

    private void OnExitBtnClick()
    {
        timeoutGroup.SetActive(false);
        // 核心修复：使用 NetClient.App 替代长链式调用
        NetClient.App?.AbortConnection();
    }

    private void OnPromptOkBtnClick()
    {
        promptGroup.SetActive(false);
    }

    #endregion
}