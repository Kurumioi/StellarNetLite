using System;
using System.IO;
using StellarNet.UI;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Shared.Infrastructure;
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

    public override void OnInit()
    {
        base.OnInit();
        logoutBtn.onClick.AddListener(OnLogoutBtn);
        refreshRoomListBtn.onClick.AddListener(OnRefreshRoomListBtn);
        createRoomBtn.onClick.AddListener(OnCreateRoomBtn);
        if (refreshReplayBtn != null) refreshReplayBtn.onClick.AddListener(OnRefreshReplayBtn);

        GlobalTypeNetEvent.Register<S2C_RoomListResponse>(OnS2C_RoomListResponse)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
        GlobalTypeNetEvent.Register<S2C_ReplayList>(OnS2C_ReplayList)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
        GlobalTypeNetEvent.Register<S2C_DownloadReplayResult>(OnS2C_DownloadReplayResult)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
    }

    private void OnDestroy()
    {
        logoutBtn.onClick.RemoveAllListeners();
        refreshRoomListBtn.onClick.RemoveAllListeners();
        createRoomBtn.onClick.RemoveAllListeners();
        if (refreshReplayBtn != null) refreshReplayBtn.onClick.RemoveAllListeners();
    }

    public override void OnOpen(object uiData = null)
    {
        base.OnOpen(uiData);
        if (uiData is Panel_StellarNetLobbyData data)
        {
            dataModel = data;
        }

        uidText.text = dataModel.uid;
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
        foreach (Transform child in roomListContent)
        {
            Destroy(child.gameObject);
        }

        for (int i = 0; i < msg.Rooms.Length; i++)
        {
            var room = msg.Rooms[i];
            var roomItem = Instantiate(roomItemPrefab, roomListContent);
            roomItem.SetActive(true);
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

        foreach (Transform child in replayListContent)
        {
            Destroy(child.gameObject);
        }

        if (msg.Replays == null || msg.Replays.Length == 0) return;

        for (int i = 0; i < msg.Replays.Length; i++)
        {
            var replay = msg.Replays[i];
            var item = Instantiate(replayItemPrefab, replayListContent);
            item.SetActive(true);

            var itemCom = item.GetComponent<Panel_StellarNetLobby_ReplayItem>();
            if (itemCom == null)
            {
                itemCom = item.AddComponent<Panel_StellarNetLobby_ReplayItem>();
            }

            string finalPath = Path.Combine(StellarNet.Lite.Client.Modules.ClientReplayModule.CacheFolderPath,
                $"{replay.ReplayId}.replay").Replace("\\", "/");
            bool isCached = File.Exists(finalPath);

            itemCom.Init(replay, isCached);
        }
    }

    private void OnS2C_DownloadReplayResult(S2C_DownloadReplayResult msg)
    {
        if (!msg.Success)
        {
            NetLogger.LogError("Panel_StellarNetLobby", $"录像下载失败: {msg.Reason}");
            GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = $"录像下载失败: {msg.Reason}" });
        }
    }
}