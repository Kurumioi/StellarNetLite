using System;
using Cysharp.Threading.Tasks;
using StellarFramework;
using StellarFramework.UI;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Panel_StellarNetLogin : UIPanelBase
{
    [SerializeField] private TMP_InputField accountIpt;
    [SerializeField] private Button loginBtn;

    // 重连 只有检查到有断线可重连时才会显示
    [SerializeField] private Transform reconnectGroupTrans;
    [SerializeField] private Button reconnectBtn;
    [SerializeField] private Button reconnectCancelBtn;

    private StellarNetMirrorManager _manager;

    public override void OnInit()
    {
        base.OnInit();

        if (GameLauncher.NetManager == null || GameLauncher.NetManager.ClientApp == null)
        {
            LogKit.LogError("[Panel_StellarNetLogin]", "OnInit 失败: 网络管理器或 ClientApp 为空");
            return;
        }

        _manager = GameLauncher.NetManager;

        loginBtn.onClick.AddListener(OnLoginBtnClick);
        reconnectBtn.onClick.AddListener(OnReconnectBtnClick);
        reconnectCancelBtn.onClick.AddListener(OnReconnectCancelBtnClick);

        GlobalTypeNetEvent.Register<S2C_LoginResult>(OnS2C_LoginResult)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
        GlobalTypeNetEvent.Register<S2C_ReconnectResult>(OnS2C_ReconnectResult)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
    }

    public override async UniTask OnOpen(object uiData = null)
    {
        await base.OnOpen(uiData);

        reconnectGroupTrans.gameObject.SetActive(false);

        // 恢复按钮交互状态
        loginBtn.interactable = true;
        reconnectBtn.interactable = true;
        reconnectCancelBtn.interactable = true;
    }

    private void OnDestroy()
    {
        loginBtn.onClick.RemoveAllListeners();
        reconnectBtn.onClick.RemoveAllListeners();
        reconnectCancelBtn.onClick.RemoveAllListeners();
    }

    #region 玩家交互请求 (C2S)

    private void OnLoginBtnClick()
    {
        var accountId = accountIpt.text;
        if (string.IsNullOrEmpty(accountId))
        {
            LogKit.LogError("[Panel_StellarNetLogin]", "登录失败: 账号不能为空");
            return;
        }

        // 禁用按钮防连点
        loginBtn.interactable = false;

        var msg = new C2S_Login()
        {
            AccountId = accountId,
            ClientVersion = Application.version
        };
        _manager.ClientApp.SendMessage(msg);
    }

    private void OnReconnectBtnClick()
    {
        reconnectBtn.interactable = false;
        reconnectCancelBtn.interactable = false;

        var msg = new C2S_ConfirmReconnect()
        {
            Accept = true
        };
        _manager.ClientApp.SendMessage(msg);
    }

    private void OnReconnectCancelBtnClick()
    {
        reconnectBtn.interactable = false;
        reconnectCancelBtn.interactable = false;

        var msg = new C2S_ConfirmReconnect()
        {
            Accept = false
        };
        _manager.ClientApp.SendMessage(msg);
    }

    #endregion

    #region 服务端回执监听 (S2C)

    private void OnS2C_LoginResult(S2C_LoginResult msg)
    {
        // 1. 前置拦截：登录失败
        if (!msg.Success)
        {
            loginBtn.interactable = true;
            LogKit.LogError("[Panel_StellarNetLogin]", $"登录失败: {msg.Reason}");
            return;
        }

        // 2. 登录成功，检查是否有断线重连房间
        if (msg.HasReconnectRoom)
        {
            reconnectGroupTrans.gameObject.SetActive(true);
            LogKit.Log("[Panel_StellarNetLogin]", $"登录成功: {msg.SessionId}，请选择是否重连房间");
        }
        else
        {
            // 3. 正常登录，直接进入大厅
            EnterLobby();
        }
    }

    private void OnS2C_ReconnectResult(S2C_ReconnectResult msg)
    {
        if (msg.Success)
        {
            // 重连成功，底层已自动恢复 ClientRoom 实例并切入 OnlineRoom 状态
            LogKit.Log("[Panel_StellarNetLogin]", "重连房间成功，准备进入房间面板");
            UIKit.OpenPanel<Panel_StellarNetRoom>();
            CloseSelf();
        }
        else
        {
            // 重连失败（可能是房间已解散，或者玩家选择了取消重连）
            LogKit.Log("[Panel_StellarNetLogin]", $"重连中止: {msg.Reason}，准备进入大厅");
            EnterLobby();
        }
    }

    #endregion

    #region 内部跳转逻辑

    private void EnterLobby()
    {
        var uidata = new Panel_StellarNetLobbyData
        {
            uid = _manager.ClientApp.Session.SessionId
        };
        UIKit.OpenPanel<Panel_StellarNetLobby>(uidata);

        LogKit.Log("[Panel_StellarNetLogin]", $"成功进入大厅: {_manager.ClientApp.Session.SessionId}");

        // 跳转后关闭当前登录面板
        CloseSelf();
    }

    #endregion
}