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

/// <summary>
/// 房间准备阶段面板。
/// </summary>
public class Panel_StellarNetRoom : UIPanelBase
{
    /// <summary>
    /// 当前账号文本。
    /// </summary>
    [SerializeField] private TMP_Text accountIdText;

    /// <summary>
    /// 房间名称文本。
    /// </summary>
    [SerializeField] private TMP_Text roomNameText;

    /// <summary>
    /// 玩家列表父节点。
    /// </summary>
    [SerializeField] private Transform playerListContent;

    /// <summary>
    /// 玩家列表项预制体。
    /// </summary>
    [SerializeField] private GameObject playerListItemPrefab;

    /// <summary>
    /// 准备按钮。
    /// </summary>
    [SerializeField] private Button readyBtn;

    /// <summary>
    /// 房主开局按钮。
    /// </summary>
    [SerializeField] private Button startGameBtn;

    /// <summary>
    /// 离开房间按钮。
    /// </summary>
    [SerializeField] private Button leaveBtn;

    /// <summary>
    /// 当前房间成员列表项缓存。
    /// Key 为 SessionId。
    /// </summary>
    private Dictionary<string, Panel_StellarNetRoom_MemberInfoItem> _memberInfoDict =
        new Dictionary<string, Panel_StellarNetRoom_MemberInfoItem>();

    /// <summary>
    /// 初始化准备面板事件。
    /// </summary>
    public override void OnInit()
    {
        base.OnInit();
        readyBtn.onClick.AddListener(OnReadyBtn);
        startGameBtn.onClick.AddListener(OnStartGameBtn);
        leaveBtn.onClick.AddListener(OnLeaveBtn);
    }

    /// <summary>
    /// 销毁面板时移除按钮事件。
    /// </summary>
    private void OnDestroy()
    {
        readyBtn?.onClick.RemoveAllListeners();
        startGameBtn?.onClick.RemoveAllListeners();
        leaveBtn?.onClick.RemoveAllListeners();
    }

    /// <summary>
    /// 打开面板时绑定房间事件并构建当前成员列表。
    /// </summary>
    public override void OnOpen(object uiData = null)
    {
        base.OnOpen(uiData);

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

        accountIdText.text = NetClient.Session?.AccountId ?? "Unknown";

        var settingCom = NetClient.CurrentRoom?.GetComponent<ClientRoomSettingsComponent>();
        if (settingCom == null) return;

        roomNameText.text = settingCom.RoomName;

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
            itemCom.Init(member.Value.AccountId, member.Value.DisplayName);
            itemCom.SetReady(member.Value.IsReady);
            _memberInfoDict.Add(member.Value.SessionId, itemCom);
        }
    }

    /// <summary>
    /// 收到开局消息后关闭准备面板。
    /// </summary>
    private void OnS2C_GameStarted(S2C_GameStarted msg)
    {
        NetLogger.LogInfo("Panel_StellarNetRoom", "游戏已开始，关闭房间 UI");
        CloseSelf();
    }

    /// <summary>
    /// 收到完整房间快照时重建成员列表。
    /// </summary>
    private void OnS2C_RoomSnapshot(S2C_RoomSnapshot msg)
    {
        roomNameText.text = msg.RoomName;

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
            itemCom.Init(member.AccountId, member.DisplayName);
            itemCom.SetReady(member.IsReady);
            _memberInfoDict.Add(member.SessionId, itemCom);
        }
    }

    /// <summary>
    /// 切换自己的准备状态。
    /// </summary>
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

        NetClient.Send(new C2S_SetReady() { IsReady = !isReady });
    }

    /// <summary>
    /// 由房主发起开局请求。
    /// </summary>
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

        if (!isMeOwner)
        {
            NetLogger.LogError("Panel_StellarNetRoom", "只有房主才能开始游戏");
            return;
        }

        if (settingsComp.IsGameStarted) return;

        NetClient.Send(new C2S_StartGame { });
    }

    /// <summary>
    /// 请求离开当前房间。
    /// </summary>
    private void OnLeaveBtn()
    {
        NetClient.Send(new C2S_LeaveRoom { });
    }

    /// <summary>
    /// 移除已离开成员的列表项。
    /// </summary>
    private void OnS2C_MemberLeft(S2C_MemberLeft msg)
    {
        if (_memberInfoDict.TryGetValue(msg.SessionId, out var item))
        {
            Destroy(item.gameObject);
            _memberInfoDict.Remove(msg.SessionId);
        }
    }

    /// <summary>
    /// 刷新单个成员的准备状态。
    /// </summary>
    private void OnS2C_MemberReadyChanged(S2C_MemberReadyChanged msg)
    {
        if (_memberInfoDict.TryGetValue(msg.SessionId, out var item))
        {
            item.SetReady(msg.IsReady);
        }
    }

    /// <summary>
    /// 追加新加入成员的列表项。
    /// </summary>
    private void OnS2C_MemberJoined(S2C_MemberJoined msg)
    {
        if (_memberInfoDict.ContainsKey(msg.Member.SessionId)) return;

        var item = Instantiate(playerListItemPrefab, playerListContent);
        item.SetActive(true);
        var itemCom = item.GetComponent<Panel_StellarNetRoom_MemberInfoItem>();
        itemCom.Init(msg.Member.AccountId, msg.Member.DisplayName);
        itemCom.SetReady(msg.Member.IsReady);
        _memberInfoDict.Add(msg.Member.SessionId, itemCom);
    }
}
