using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using StellarFramework;
using StellarFramework.UI;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Panel_StellarNetRoom : UIPanelBase
{
    [SerializeField] private TMP_Text uidText;
    [SerializeField] private TMP_Text roomNameText;
    [SerializeField] private Transform playerListContent;
    [SerializeField] private Transform playerListItemPrefab;
    [SerializeField] private Button readyBtn;
    [SerializeField] private Button startGameBtn;
    [SerializeField] private Button gameOverBtn;
    [SerializeField] private Button leaveBtn;

    private ClientApp _app;
    private Dictionary<string, Panel_StellarNetRoom_MemberInfoItem> _memberInfoDict = new Dictionary<string, Panel_StellarNetRoom_MemberInfoItem>();

    public override void OnInit()
    {
        base.OnInit();
        readyBtn.onClick.AddListener(OnReadyBtn);
        startGameBtn.onClick.AddListener(OnStartGameBtn);
        gameOverBtn.onClick.AddListener(OnGameOverBtn);
        leaveBtn.onClick.AddListener(OnLeaveBtn);
    }

    private void OnDestroy()
    {
        readyBtn.onClick.RemoveAllListeners();
        startGameBtn.onClick.RemoveAllListeners();
        gameOverBtn.onClick.RemoveAllListeners();
        leaveBtn.onClick.RemoveAllListeners();
    }

    public override async UniTask OnOpen(object uiData = null)
    {
        await base.OnOpen(uiData);
        _app = GameLauncher.NetManager.ClientApp;

        GlobalTypeNetEvent.Register<S2C_LeaveRoomResult>(OnS2C_LeaveRoomResult).UnRegisterWhenMonoDisable(this);
        _app.CurrentRoom.NetEventSystem.Register<S2C_MemberJoined>(OnS2C_MemberJoined).UnRegisterWhenMonoDisable(this);
        _app.CurrentRoom.NetEventSystem.Register<S2C_MemberLeft>(OnS2C_MemberLeft).UnRegisterWhenMonoDisable(this);
        _app.CurrentRoom.NetEventSystem.Register<S2C_MemberReadyChanged>(OnS2C_MemberReadyChanged).UnRegisterWhenMonoDisable(this);
        _app.CurrentRoom.NetEventSystem.Register<S2C_RoomSnapshot>(OnS2C_RoomSnapshot).UnRegisterWhenMonoDisable(this);
        _app.CurrentRoom.NetEventSystem.Register<S2C_GameStarted>(OnS2C_GameStarted).UnRegisterWhenMonoDisable(this);

        uidText.text = _app.Session.Uid;

        var settingCom = _app.CurrentRoom.GetComponent<ClientRoomSettingsComponent>();
        if (settingCom == null) return;

        roomNameText.text = settingCom.RoomName;
        playerListContent.ClearChildren();
        _memberInfoDict.Clear();

        foreach (var member in settingCom.Members)
        {
            var item = Instantiate(playerListItemPrefab, playerListContent);
            item.Show();
            var itemCom = item.GetComponent<Panel_StellarNetRoom_MemberInfoItem>();
            itemCom.Init(member.Value.Uid, member.Value.DisplayName);
            itemCom.SetReady(member.Value.IsReady);
            _memberInfoDict.Add(member.Value.SessionId, itemCom);
        }
    }

    private void OnS2C_GameStarted(S2C_GameStarted msg)
    {
        // 核心修复 2：游戏开始时，必须关闭房间面板，将屏幕交接给局内战斗 UI
        LogKit.Log("[Panel_StellarNetRoom]", "游戏已开始，关闭房间 UI");
        CloseSelf();
    }

    private void OnS2C_RoomSnapshot(S2C_RoomSnapshot msg)
    {
        roomNameText.text = msg.RoomName;
        playerListContent.ClearChildren();
        _memberInfoDict.Clear();

        foreach (var member in msg.Members)
        {
            if (member == null) continue;
            var item = Instantiate(playerListItemPrefab, playerListContent);
            item.Show();
            var itemCom = item.GetComponent<Panel_StellarNetRoom_MemberInfoItem>();
            itemCom.Init(member.Uid, member.DisplayName);
            itemCom.SetReady(member.IsReady);
            _memberInfoDict.Add(member.SessionId, itemCom);
        }
    }

    private void OnS2C_LeaveRoomResult(S2C_LeaveRoomResult msg)
    {
        if (msg.Success)
        {
            CloseSelf();
            UIKit.OpenPanel<Panel_StellarNetLobby>();
        }
    }

    private void OnReadyBtn()
    {
        var settingsComp = _app.CurrentRoom.GetComponent<ClientRoomSettingsComponent>();
        if (settingsComp == null) return;
        string mySessionId = _app.Session.SessionId;
        bool isReady = false;
        if (settingsComp.Members.TryGetValue(mySessionId, out var myInfo)) isReady = myInfo.IsReady;
        _app.SendMessage(new C2S_SetReady() { IsReady = !isReady });
    }

    private void OnStartGameBtn()
    {
        var settingsComp = _app.CurrentRoom.GetComponent<ClientRoomSettingsComponent>();
        if (settingsComp == null) return;
        string mySessionId = _app.Session.SessionId;
        bool isMeOwner = false;
        if (settingsComp.Members.TryGetValue(mySessionId, out var myInfo)) isMeOwner = myInfo.IsOwner;

        if (!isMeOwner)
        {
            LogKit.LogError("[Panel_StellarNetRoom]", "只有房主才能开始游戏");
            return;
        }

        if (settingsComp.IsGameStarted) return;

        _app.SendMessage(new C2S_StartGame { });
    }

    private void OnGameOverBtn()
    {
    }

    private void OnLeaveBtn()
    {
        _app.SendMessage(new C2S_LeaveRoom { });
    }

    private void OnS2C_MemberLeft(S2C_MemberLeft msg)
    {
        if (_memberInfoDict.TryGetValue(msg.SessionId, out var item))
        {
            item.gameObject.SafeDestroy();
            _memberInfoDict.Remove(msg.SessionId);
        }
    }

    private void OnS2C_MemberReadyChanged(S2C_MemberReadyChanged msg)
    {
        if (_memberInfoDict.TryGetValue(msg.SessionId, out var item)) item.SetReady(msg.IsReady);
    }

    private void OnS2C_MemberJoined(S2C_MemberJoined msg)
    {
        if (_memberInfoDict.ContainsKey(msg.Member.SessionId)) return;
        var item = Instantiate(playerListItemPrefab, playerListContent);
        item.Show();
        var itemCom = item.GetComponent<Panel_StellarNetRoom_MemberInfoItem>();
        itemCom.Init(msg.Member.Uid, msg.Member.DisplayName);
        itemCom.SetReady(msg.Member.IsReady);
        _memberInfoDict.Add(msg.Member.SessionId, itemCom);
    }
}