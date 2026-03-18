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

[Serializable]
public class Panel_StellarNetLobbyData
{
    public string uid;
}

public class Panel_StellarNetLobby : UIPanelBase
{
    [Header("基础信息")] [SerializeField] private TMP_Text uidText;
    [SerializeField] private Button logoutBtn;

    [Header("房间列表")] [SerializeField] private Button createRoomBtn;
    [SerializeField] private Button refreshRoomListBtn;
    [SerializeField] private Transform roomListContent;
    [SerializeField] private GameObject roomItemPrefab;

    [Header("录像列表")] [SerializeField] private Button refreshReplayBtn;
    [SerializeField] private Transform replayListContent;
    [SerializeField] private GameObject replayItemPrefab;

    [SerializeField] private Panel_StellarNetLobbyData dataModel;
    private string _downloadingReplayId = string.Empty;

    public override void OnInit()
    {
        base.OnInit();
        logoutBtn.onClick.AddListener(OnLogoutBtn);
        refreshRoomListBtn.onClick.AddListener(OnRefreshRoomListBtn);
        createRoomBtn.onClick.AddListener(OnCreateRoomBtn);
        if (refreshReplayBtn != null) refreshReplayBtn.onClick.AddListener(OnRefreshReplayBtn);

        GlobalTypeNetEvent.Register<S2C_RoomListResponse>(OnS2C_RoomListResponse).UnRegisterWhenGameObjectDestroyed(gameObject);
        GlobalTypeNetEvent.Register<S2C_ReplayList>(OnS2C_ReplayList).UnRegisterWhenGameObjectDestroyed(gameObject);
    }

    private void OnDestroy()
    {
        logoutBtn.onClick.RemoveAllListeners();
        refreshRoomListBtn.onClick.RemoveAllListeners();
        createRoomBtn.onClick.RemoveAllListeners();
        if (refreshReplayBtn != null) refreshReplayBtn.onClick.RemoveAllListeners();
    }

    public override async UniTask OnOpen(object uiData = null)
    {
        await base.OnOpen(uiData);
        if (uiData is Panel_StellarNetLobbyData data)
        {
            dataModel = data;
        }

        uidText.text = dataModel.uid;
        _downloadingReplayId = string.Empty;
    }

    private void OnLogoutBtn()
    {
        GameLauncher.NetManager.StopClient();
    }

    private void OnRefreshRoomListBtn()
    {
        NetClient.Send(new C2S_GetRoomList());
    }

    private void OnCreateRoomBtn()
    {
        UIKit.OpenPanel<Panel_SetRoomConfig>();
    }

    private void OnRefreshReplayBtn()
    {
        NetClient.Send(new C2S_GetReplayList());
    }

    private void OnS2C_RoomListResponse(S2C_RoomListResponse msg)
    {
        roomListContent.ClearChildren();
        for (int i = 0; i < msg.Rooms.Length; i++)
        {
            var room = msg.Rooms[i];
            var roomItem = Instantiate(roomItemPrefab, roomListContent);
            roomItem.Show();
            roomItem.GetComponent<Panel_StellarNetLobby_RoomItem>()
                .Init(room.RoomName, room.RoomId, room.MemberCount, room.MaxMembers, GetRoomStateByInt(room.State));
        }
    }

    private string GetRoomStateByInt(int stateId)
    {
        switch (stateId)
        {
            case 0: return "等待中";
            case 1: return "游戏中";
            case 2: return "已结束";
            default: return "未知";
        }
    }

    private void OnS2C_ReplayList(S2C_ReplayList msg)
    {
        if (replayListContent == null || replayItemPrefab == null) return;
        replayListContent.ClearChildren();

        if (msg.Replays == null || msg.Replays.Length == 0) return;

        foreach (var replay in msg.Replays)
        {
            var item = Instantiate(replayItemPrefab, replayListContent);
            item.Show();

            var text = item.GetComponentInChildren<TMP_Text>();
            var btn = item.GetComponentInChildren<Button>();

            if (text != null)
            {
                // 修复：渲染可读的 DisplayName 和时间戳
                DateTime dt = DateTimeOffset.FromUnixTimeSeconds(replay.Timestamp).LocalDateTime;
                text.text = $"[{dt:MM-dd HH:mm}] {replay.DisplayName}";
            }

            if (btn != null)
            {
                string rId = replay.ReplayId;
                btn.onClick.AddListener(() =>
                {
                    if (!string.IsNullOrEmpty(_downloadingReplayId)) return;
                    _downloadingReplayId = rId;
                    StellarNet.Lite.Client.Modules.ClientReplayModule.RequestDownload(NetClient.App, rId);
                    if (text != null) text.text = $"{replay.DisplayName} (下载/加载中...)";
                });
            }
        }
    }
}