using System.Collections.Generic;
using StellarNet.UI;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Panel_StellarNetRoom : UIPanelBase
{
    // 房间成员列表 UI。
    [SerializeField] private TMP_Text uidText;
    [SerializeField] private TMP_Text roomNameText;
    [SerializeField] private Transform playerListContent;
    [SerializeField] private GameObject playerListItemPrefab;
    [SerializeField] private Button readyBtn;
    [SerializeField] private Button startGameBtn;
    [SerializeField] private Button leaveBtn;

    // SessionId -> 成员条目控件。
    private Dictionary<string, Panel_StellarNetRoom_MemberInfoItem> _memberInfoDict =
        new Dictionary<string, Panel_StellarNetRoom_MemberInfoItem>();

    public override void OnInit()
    {
        base.OnInit();

        readyBtn.onClick.AddListener(OnReadyBtn);
        startGameBtn.onClick.AddListener(OnStartGameBtn);
        leaveBtn.onClick.AddListener(OnLeaveBtn);
    }

    private void OnDestroy()
    {
        readyBtn?.onClick.RemoveAllListeners();
        startGameBtn?.onClick.RemoveAllListeners();
        leaveBtn?.onClick.RemoveAllListeners();
    }

    public override void OnOpen(object uiData = null)
    {
        base.OnOpen(uiData);

        // 面板打开后只监听当前房间的房间域事件。
        if (NetClient.CurrentRoom != null)
        {
            NetClient.CurrentRoom.NetEventSystem.Register<S2C_MemberJoined>(OnS2C_MemberJoined)
                .UnRegisterWhenMonoDisable(this);
            NetClient.CurrentRoom.NetEventSystem.Register<S2C_MemberLeft>(OnS2C_MemberLeft)
                .UnRegisterWhenMonoDisable(this);
            NetClient.CurrentRoom.NetEventSystem.Register<S2C_MemberReadyChanged>(OnS2C_MemberReadyChanged)
                .UnRegisterWhenMonoDisable(this);
            NetClient.CurrentRoom.NetEventSystem.Register<S2C_RoomSnapshot>(OnS2C_RoomSnapshot)
                .UnRegisterWhenMonoDisable(this);
            NetClient.CurrentRoom.NetEventSystem.Register<S2C_GameStarted>(OnS2C_GameStarted)
                .UnRegisterWhenMonoDisable(this);
        }

        uidText.text = NetClient.Session?.Uid ?? "Unknown";

        var settingCom = NetClient.CurrentRoom?.GetComponent<ClientRoomSettingsComponent>();
        if (settingCom == null) return;

        roomNameText.text = settingCom.RoomName;

        // 用当前快照重建一次成员列表。
        foreach (Transform child in playerListContent)
        {
            Destroy(child.gameObject);
        }

        _memberInfoDict.Clear();

        foreach (var member in settingCom.Members)
        {
            var item = Instantiate(playerListItemPrefab, playerListContent);
            item.SetActive(true);

            var itemCom = item.GetComponent<Panel_StellarNetRoom_MemberInfoItem>();
            itemCom.Init(member.Value.Uid, member.Value.DisplayName);
            itemCom.SetReady(member.Value.IsReady);

            _memberInfoDict.Add(member.Value.SessionId, itemCom);
        }
    }

    private void OnS2C_GameStarted(S2C_GameStarted msg)
    {
        // 游戏开始后，准备大厅 UI 要主动关闭。
        NetLogger.LogInfo("Panel_StellarNetRoom", "游戏已开始，关闭房间 UI");
        CloseSelf();
    }

    private void OnS2C_RoomSnapshot(S2C_RoomSnapshot msg)
    {
        roomNameText.text = msg.RoomName;

        // 快照到达时，整表重建最稳妥。
        foreach (Transform child in playerListContent)
        {
            Destroy(child.gameObject);
        }

        _memberInfoDict.Clear();

        foreach (var member in msg.Members)
        {
            if (member == null) continue;

            var item = Instantiate(playerListItemPrefab, playerListContent);
            item.SetActive(true);

            var itemCom = item.GetComponent<Panel_StellarNetRoom_MemberInfoItem>();
            itemCom.Init(member.Uid, member.DisplayName);
            itemCom.SetReady(member.IsReady);

            _memberInfoDict.Add(member.SessionId, itemCom);
        }
    }

    private void OnReadyBtn()
    {
        var settingsComp = NetClient.CurrentRoom?.GetComponent<ClientRoomSettingsComponent>();
        if (settingsComp == null) return;

        string mySessionId = NetClient.Session.SessionId;
        bool isReady = false;

        if (settingsComp.Members.TryGetValue(mySessionId, out var myInfo))
        {
            isReady = myInfo.IsReady;
        }

        // 准备按钮本质是 ready 状态切换请求。
        NetClient.Send(new C2S_SetReady() { IsReady = !isReady });
    }

    private void OnStartGameBtn()
    {
        var settingsComp = NetClient.CurrentRoom?.GetComponent<ClientRoomSettingsComponent>();
        if (settingsComp == null) return;

        string mySessionId = NetClient.Session.SessionId;
        bool isMeOwner = false;

        if (settingsComp.Members.TryGetValue(mySessionId, out var myInfo))
        {
            isMeOwner = myInfo.IsOwner;
        }

        // 开始游戏只允许房主触发。
        if (!isMeOwner)
        {
            NetLogger.LogError("Panel_StellarNetRoom", "只有房主才能开始游戏");
            return;
        }

        if (settingsComp.IsGameStarted) return;

        NetClient.Send(new C2S_StartGame { });
    }

    private void OnLeaveBtn()
    {
        // 离房统一走全局离房协议。
        NetClient.Send(new C2S_LeaveRoom { });
    }

    private void OnS2C_MemberLeft(S2C_MemberLeft msg)
    {
        if (_memberInfoDict.TryGetValue(msg.SessionId, out var item))
        {
            Destroy(item.gameObject);
            _memberInfoDict.Remove(msg.SessionId);
        }
    }

    private void OnS2C_MemberReadyChanged(S2C_MemberReadyChanged msg)
    {
        if (_memberInfoDict.TryGetValue(msg.SessionId, out var item))
        {
            item.SetReady(msg.IsReady);
        }
    }

    private void OnS2C_MemberJoined(S2C_MemberJoined msg)
    {
        if (_memberInfoDict.ContainsKey(msg.Member.SessionId)) return;

        var item = Instantiate(playerListItemPrefab, playerListContent);
        item.SetActive(true);

        var itemCom = item.GetComponent<Panel_StellarNetRoom_MemberInfoItem>();
        itemCom.Init(msg.Member.Uid, msg.Member.DisplayName);
        itemCom.SetReady(msg.Member.IsReady);

        _memberInfoDict.Add(msg.Member.SessionId, itemCom);
    }
}
