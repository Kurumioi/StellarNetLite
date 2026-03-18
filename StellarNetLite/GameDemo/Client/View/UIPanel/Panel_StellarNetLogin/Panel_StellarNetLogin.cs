using System;
using Cysharp.Threading.Tasks;
using StellarFramework;
using StellarFramework.UI;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Panel_StellarNetLogin : UIPanelBase
{
    [SerializeField] private TMP_InputField accountIpt;
    [SerializeField] private Button loginBtn;

    [SerializeField] private Transform reconnectGroupTrans;
    [SerializeField] private Button reconnectBtn;
    [SerializeField] private Button reconnectCancelBtn;

    public override void OnInit()
    {
        base.OnInit();

        if (NetClient.App == null)
        {
            LogKit.LogError("[Panel_StellarNetLogin]", "OnInit 失败: NetClient 尚未初始化");
            return;
        }

        loginBtn.onClick.AddListener(OnLoginBtnClick);
        reconnectBtn.onClick.AddListener(OnReconnectBtnClick);
        reconnectCancelBtn.onClick.AddListener(OnReconnectCancelBtnClick);

        GlobalTypeNetEvent.Register<S2C_LoginResult>(OnS2C_LoginResult)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
        GlobalTypeNetEvent.Register<S2C_ReconnectResult>(OnS2C_ReconnectResult)
            .UnRegisterWhenGameObjectDestroyed(gameObject);

        // 核心修复：断线重连也必须等待本地装配完毕事件
        GlobalTypeNetEvent.Register<Local_RoomEntered>(OnRoomEntered)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
    }

    public override async UniTask OnOpen(object uiData = null)
    {
        await base.OnOpen(uiData);
        reconnectGroupTrans.gameObject.SetActive(false);

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

    private void OnRoomEntered(Local_RoomEntered evt)
    {
        if (NetClient.State == ClientAppState.OnlineRoom)
        {
            LogKit.Log("[Panel_StellarNetLogin]", "重连房间装配完毕，准备进入房间面板");
            UIKit.OpenPanel<Panel_StellarNetRoom>();
            CloseSelf();
        }
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

        loginBtn.interactable = false;

        var msg = new C2S_Login()
        {
            AccountId = accountId,
            ClientVersion = Application.version
        };
        NetClient.Send(msg);
    }

    private void OnReconnectBtnClick()
    {
        reconnectBtn.interactable = false;
        reconnectCancelBtn.interactable = false;

        var msg = new C2S_ConfirmReconnect()
        {
            Accept = true
        };
        NetClient.Send(msg);
    }

    private void OnReconnectCancelBtnClick()
    {
        reconnectBtn.interactable = false;
        reconnectCancelBtn.interactable = false;

        var msg = new C2S_ConfirmReconnect()
        {
            Accept = false
        };
        NetClient.Send(msg);
    }

    #endregion

    #region 服务端回执监听 (S2C)

    private void OnS2C_LoginResult(S2C_LoginResult msg)
    {
        if (!msg.Success)
        {
            loginBtn.interactable = true;
            LogKit.LogError("[Panel_StellarNetLogin]", $"登录失败: {msg.Reason}");
            return;
        }

        if (msg.HasReconnectRoom)
        {
            reconnectGroupTrans.gameObject.SetActive(true);
            LogKit.Log("[Panel_StellarNetLogin]", $"登录成功: {msg.SessionId}，请选择是否重连房间");
        }
        else
        {
            EnterLobby();
        }
    }

    private void OnS2C_ReconnectResult(S2C_ReconnectResult msg)
    {
        // 核心修复：仅处理失败分支，成功跳转交由 Local_RoomEntered 统一处理
        if (!msg.Success)
        {
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
            uid = NetClient.Session.SessionId
        };
        UIKit.OpenPanel<Panel_StellarNetLobby>(uidata);
        LogKit.Log("[Panel_StellarNetLogin]", $"成功进入大厅: {NetClient.Session.SessionId}");

        CloseSelf();
    }

    #endregion
}