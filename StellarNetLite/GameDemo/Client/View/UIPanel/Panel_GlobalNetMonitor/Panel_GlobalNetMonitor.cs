using StellarNet.UI;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 全局网络状态面板。
/// </summary>
public class Panel_GlobalNetMonitor : UIPanelBase
{
    // 在线 RTT 显示区域。
    [Header("RTT 监控")] [SerializeField] private GameObject rttGroup;
    [SerializeField] private TMP_Text rttText;

    [Header("弱网阻断遮罩 (半透明)")] [SerializeField]
    private GameObject weakNetGroup;

    [Header("断线挂起遮罩 (强遮罩)")] [SerializeField]
    private GameObject suspendGroup;

    [SerializeField] private TMP_Text suspendTimeText;

    // 自动重连超时后的决策区域。
    [Header("超时决断框")] [SerializeField] private GameObject timeoutGroup;
    [SerializeField] private Button continueBtn;
    [SerializeField] private Button exitBtn;

    // 全局系统提示区域。
    [Header("系统提示框")] [SerializeField] private GameObject promptGroup;
    [SerializeField] private TMP_Text promptText;
    [SerializeField] private Button promptOkBtn;

    public override void OnInit()
    {
        base.OnInit();

        continueBtn.onClick.AddListener(OnContinueBtnClick);
        exitBtn.onClick.AddListener(OnExitBtnClick);
        promptOkBtn.onClick.AddListener(OnPromptOkBtnClick);

        // 这里只监听全局网络状态事件。
        GlobalTypeNetEvent.Register<Local_NetworkQualityChanged>(HandleNetworkQuality)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
        GlobalTypeNetEvent.Register<Local_ConnectionSuspended>(HandleConnectionSuspended)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
        GlobalTypeNetEvent.Register<Local_ReconnectTimeout>(HandleReconnectTimeout)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
        GlobalTypeNetEvent.Register<Local_SystemPrompt>(HandleSystemPrompt)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
    }

    public override void OnOpen(object uiData = null)
    {
        base.OnOpen(uiData);
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

        // 非挂起态时自动收起挂起遮罩和超时决策框。
        if (state != ClientAppState.ConnectionSuspended)
        {
            if (suspendGroup.activeSelf) suspendGroup.SetActive(false);
            if (timeoutGroup.activeSelf) timeoutGroup.SetActive(false);
        }
    }

    private void ResetAllUI()
    {
        // 面板打开时先回到干净初始状态。
        rttGroup.SetActive(false);
        weakNetGroup.SetActive(false);
        suspendGroup.SetActive(false);
        timeoutGroup.SetActive(false);
        promptGroup.SetActive(false);
    }

    private void HandleNetworkQuality(Local_NetworkQualityChanged evt)
    {
        // 在线房间内才显示 RTT 和弱网阻断遮罩。
        if (NetClient.State != ClientAppState.OnlineRoom)
        {
            weakNetGroup.SetActive(false);
            return;
        }

        rttText.text = $"{evt.RttMs} ms";
        if (evt.RttMs < 35) rttText.color = Color.green;
        else if (evt.RttMs < 100) rttText.color = Color.yellow;
        else rttText.color = Color.red;

        weakNetGroup.SetActive(evt.IsWeakNetBlock);
    }

    private void HandleConnectionSuspended(Local_ConnectionSuspended evt)
    {
        // 进入挂起态后显示倒计时恢复提示。
        weakNetGroup.SetActive(false);
        suspendGroup.SetActive(true);
        suspendTimeText.text = $"正在尝试恢复... ({evt.RemainingSeconds:F1}s)";
    }

    private void HandleReconnectTimeout(Local_ReconnectTimeout evt)
    {
        // 自动重连超时后切换到玩家决策框。
        suspendGroup.SetActive(false);
        timeoutGroup.SetActive(true);
    }

    private void HandleSystemPrompt(Local_SystemPrompt evt)
    {
        transform.SetSiblingIndex(transform.childCount - 1);
        // 系统提示统一走一层轻提示弹窗。
        promptText.text = evt.Message;
        promptGroup.SetActive(true);
    }

    private void OnContinueBtnClick()
    {
        timeoutGroup.SetActive(false);
        GameLauncher.AppManager.RestartReconnectionRoutine();
    }

    private void OnExitBtnClick()
    {
        timeoutGroup.SetActive(false);
        NetClient.App?.AbortConnection();
    }

    private void OnPromptOkBtnClick()
    {
        promptGroup.SetActive(false);
    }
}
