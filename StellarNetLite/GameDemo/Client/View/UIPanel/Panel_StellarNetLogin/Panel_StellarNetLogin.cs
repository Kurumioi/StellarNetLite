using Mirror;
using StellarNet.UI;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Panel_StellarNetLogin : UIPanelBase
{
    // 登录表单。
    [SerializeField] private TMP_InputField accountIpt;
    [SerializeField] private Button loginBtn;
    [SerializeField] private TMP_Text loginStatusTxt;
    [SerializeField] private Button restartClientBtn;

    // 重连决策区域。
    [SerializeField] private Transform reconnectGroupTrans;
    [SerializeField] private Button reconnectBtn;
    [SerializeField] private Button reconnectCancelBtn;

    // 防止重复点击的本地请求锁。
    private bool _isRequesting;
    private float _requestStartTime;
    private const float RequestTimeoutSeconds = 5f;

    public override void OnInit()
    {
        base.OnInit();

        if (NetClient.App == null)
        {
            Debug.LogError($"[Panel_StellarNetLogin] 初始化失败: NetClient.App 为空, Object:{name}");
            return;
        }

        loginBtn.onClick.AddListener(OnLoginBtnClick);
        restartClientBtn.onClick.AddListener(OnRestartClientBtnClick);
        reconnectBtn.onClick.AddListener(OnReconnectBtnClick);
        reconnectCancelBtn.onClick.AddListener(OnReconnectCancelBtnClick);

        // 登录页只监听物理连接和登录结果。
        StellarNetMirrorManager.OnClientConnectedEvent += OnClientConnected;
        StellarNetMirrorManager.OnClientDisconnectedEvent += OnClientDisconnected;

        GlobalTypeNetEvent.Register<S2C_LoginResult>(OnS2C_LoginResult).UnRegisterWhenGameObjectDestroyed(gameObject);
    }

    public override void OnOpen(object uiData = null)
    {
        base.OnOpen(uiData);

        _isRequesting = false;

        if (reconnectGroupTrans != null)
        {
            reconnectGroupTrans.gameObject.SetActive(false);
        }

        if (loginBtn != null) loginBtn.interactable = true;
        if (reconnectBtn != null) reconnectBtn.interactable = true;
        if (reconnectCancelBtn != null) reconnectCancelBtn.interactable = true;

        // 打开时根据物理连接状态刷新按钮和提示文案。
        if (NetworkClient.isConnected)
        {
            restartClientBtn.gameObject.SetActive(false);
            loginStatusTxt.text = "<color=green>服务端已连接</color>";
        }
        else
        {
            restartClientBtn.gameObject.SetActive(true);
            restartClientBtn.interactable = true;
            loginStatusTxt.text = "<color=red>服务端已断开</color>";
        }
    }

    private void OnDestroy()
    {
        StellarNetMirrorManager.OnClientConnectedEvent -= OnClientConnected;
        StellarNetMirrorManager.OnClientDisconnectedEvent -= OnClientDisconnected;

        loginBtn?.onClick.RemoveAllListeners();
        reconnectBtn?.onClick.RemoveAllListeners();
        reconnectCancelBtn?.onClick.RemoveAllListeners();
        restartClientBtn?.onClick.RemoveAllListeners();
    }

    private void Update()
    {
        // 请求超时后自动解锁按钮，防止 UI 卡死。
        if (_isRequesting)
        {
            if (Time.realtimeSinceStartup - _requestStartTime > RequestTimeoutSeconds)
            {
                _isRequesting = false;
                if (loginBtn != null) loginBtn.interactable = true;
                if (reconnectBtn != null) reconnectBtn.interactable = true;
                if (reconnectCancelBtn != null) reconnectCancelBtn.interactable = true;

                NetLogger.LogWarning("Panel_StellarNetLogin", "登录或重连请求超时，已恢复 UI 交互");
                GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = "请求超时，请重试" });
            }
        }
    }

    private void OnClientConnected()
    {
        restartClientBtn.gameObject.SetActive(false);
        restartClientBtn.interactable = false;
        loginStatusTxt.text = "<color=green>服务端已连接</color>";
    }

    private void OnClientDisconnected()
    {
        restartClientBtn.gameObject.SetActive(true);
        restartClientBtn.interactable = true;
        loginStatusTxt.text = "<color=red>服务端已断开</color>";

        // 断线时自动解除请求锁定
        _isRequesting = false;
        if (loginBtn != null) loginBtn.interactable = true;
    }

    private void OnLoginBtnClick()
    {
        if (NetClient.App == null || NetClient.Session == null)
        {
            NetLogger.LogError($"Panel_StellarNetLogin", $"登录失败: NetClient 未初始化, Object:{name}");
            return;
        }

        string accountId = accountIpt != null ? accountIpt.text : string.Empty;
        if (string.IsNullOrWhiteSpace(accountId))
        {
            NetLogger.LogError($"Panel_StellarNetLogin", $"登录失败: 账号为空, Object:{name}");
            return;
        }

        // 登录请求会把本地输入账号写入 ClientSession。
        string safeAccountId = accountId.Trim();
        NetClient.Session.SetAccountId(safeAccountId);

        _isRequesting = true;
        _requestStartTime = Time.realtimeSinceStartup;
        if (loginBtn != null) loginBtn.interactable = false;

        var msg = new C2S_Login
        {
            AccountId = safeAccountId,
            ClientVersion = Application.version
        };
        NetClient.Send(msg);
    }

    private void OnRestartClientBtnClick()
    {
        if (NetworkClient.isConnected || NetworkClient.isConnecting) return;

        restartClientBtn.interactable = false;
        GameLauncher.Instance.StartClient();
        NetLogger.LogInfo("Panel_StellarNetLogin", "尝试重新连接服务端... ");
    }

    private void OnReconnectBtnClick()
    {
        _isRequesting = true;
        _requestStartTime = Time.realtimeSinceStartup;

        if (reconnectBtn != null) reconnectBtn.interactable = false;
        if (reconnectCancelBtn != null) reconnectCancelBtn.interactable = false;

        // 挂起态下，玩家可以决定继续恢复或放弃恢复。
        NetClient.Send(new C2S_ConfirmReconnect { Accept = true });
    }

    private void OnReconnectCancelBtnClick()
    {
        _isRequesting = true;
        _requestStartTime = Time.realtimeSinceStartup;

        if (reconnectBtn != null) reconnectBtn.interactable = false;
        if (reconnectCancelBtn != null) reconnectCancelBtn.interactable = false;

        NetClient.Send(new C2S_ConfirmReconnect { Accept = false });
    }

    private void OnS2C_LoginResult(S2C_LoginResult msg)
    {
        _isRequesting = false;

        if (msg == null)
        {
            NetLogger.LogError($"Panel_StellarNetLogin", $"处理登录结果失败: msg 为空, Object:{name}");
            if (loginBtn != null) loginBtn.interactable = true;
            return;
        }

        if (!msg.Success)
        {
            if (loginBtn != null) loginBtn.interactable = true;
            NetLogger.LogError($"Panel_StellarNetLogin", $"登录失败: Reason:{msg.Reason}, Object:{name}");
            GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = $"登录失败: {msg.Reason}" });
            return;
        }

        // 登录成功但存在恢复房间时，拉起重连决策 UI。
        if (msg.HasReconnectRoom && reconnectGroupTrans != null)
        {
            reconnectGroupTrans.gameObject.SetActive(true);
        }
    }
}
