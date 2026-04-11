using System;
using System.Collections.Generic;
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
/// <summary>
/// 大厅面板打开数据。
/// </summary>
public class Panel_StellarNetLobbyData
{
    /// <summary>
    /// 当前登录账号 Id。
    /// </summary>
    public string accountId;
}

/// <summary>
/// 大厅面板。
/// </summary>
public class Panel_StellarNetLobby : UIPanelBase
{
    [Header("基础信息")] [SerializeField] private TMP_Text accountIdText;

    [SerializeField] private Button logoutBtn;

    [Header("房间列表")] [SerializeField] private Button createRoomBtn;
    [SerializeField] private Button refreshRoomListBtn;
    [SerializeField] private Transform roomListContent;
    [SerializeField] private GameObject roomItemPrefab;

    [Header("录像列表")] [SerializeField] private Button refreshReplayBtn;
    [SerializeField] private Transform replayListContent;
    [SerializeField] private GameObject replayItemPrefab;

    [Header("在线玩家列表 (新增)")] [SerializeField]
    private Transform playerListContent;

    [SerializeField] private GameObject playerItemPrefab;

    [Header("大厅聊天 (新增)")] [SerializeField] private Transform chatContent;
    [SerializeField] private GameObject chatItemPrefab;
    [SerializeField] private TMP_InputField chatInput;
    [SerializeField] private Button sendChatBtn;

    [SerializeField] private Panel_StellarNetLobbyData dataModel;

    private readonly Dictionary<string, Panel_StellarNetLobby_PlayerItem> _playerItems = new Dictionary<string, Panel_StellarNetLobby_PlayerItem>();

    public override void OnInit()
    {
        base.OnInit();
        logoutBtn.onClick.AddListener(OnLogoutBtn);
        refreshRoomListBtn.onClick.AddListener(OnRefreshRoomListBtn);
        createRoomBtn.onClick.AddListener(OnCreateRoomBtn);

        if (refreshReplayBtn != null) refreshReplayBtn.onClick.AddListener(OnRefreshReplayBtn);
        if (sendChatBtn != null) sendChatBtn.onClick.AddListener(OnSendChatBtn);
        if (chatInput != null) chatInput.onSubmit.AddListener(OnChatInputSubmit);

        GlobalTypeNetEvent.Register<S2C_RoomListResponse>(OnS2C_RoomListResponse).UnRegisterWhenGameObjectDestroyed(gameObject);
        GlobalTypeNetEvent.Register<S2C_ReplayList>(OnS2C_ReplayList).UnRegisterWhenGameObjectDestroyed(gameObject);
        GlobalTypeNetEvent.Register<S2C_DownloadReplayResult>(OnS2C_DownloadReplayResult).UnRegisterWhenGameObjectDestroyed(gameObject);
        GlobalTypeNetEvent.Register<S2C_JoinRoomResult>(OnS2C_JoinRoomResult).UnRegisterWhenGameObjectDestroyed(gameObject);
        GlobalTypeNetEvent.Register<S2C_OnlinePlayerListSync>(OnS2C_OnlinePlayerListSync).UnRegisterWhenGameObjectDestroyed(gameObject);
        GlobalTypeNetEvent.Register<S2C_GlobalPlayerStateIncrementalSync>(OnS2C_GlobalPlayerStateIncrementalSync)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
        GlobalTypeNetEvent.Register<S2C_GlobalChatSync>(OnS2C_GlobalChatSync).UnRegisterWhenGameObjectDestroyed(gameObject);
    }

    private void OnDestroy()
    {
        Panel_StellarNetLobby_RoomItem.ClearPendingJoinRequest();
        logoutBtn.onClick.RemoveAllListeners();
        refreshRoomListBtn.onClick.RemoveAllListeners();
        createRoomBtn.onClick.RemoveAllListeners();
        if (refreshReplayBtn != null) refreshReplayBtn.onClick.RemoveAllListeners();
        if (sendChatBtn != null) sendChatBtn.onClick.RemoveAllListeners();
        if (chatInput != null) chatInput.onSubmit.RemoveAllListeners();
    }

    public override void OnOpen(object uiData = null)
    {
        base.OnOpen(uiData);
        Panel_StellarNetLobby_RoomItem.ClearPendingJoinRequest();
        if (uiData is Panel_StellarNetLobbyData data)
        {
            dataModel = data;
        }

        accountIdText.text = dataModel.accountId;
    }

    private void OnLogoutBtn()
    {
        GameLauncher.AppManager.StopClient();
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

    #region 房间与录像列表处理

    private void OnS2C_RoomListResponse(S2C_RoomListResponse msg)
    {
        if (roomListContent == null || roomItemPrefab == null) return;

        foreach (Transform child in roomListContent) Destroy(child.gameObject);

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

        foreach (Transform child in replayListContent) Destroy(child.gameObject);

        if (msg.Replays == null || msg.Replays.Length == 0) return;

        for (int i = 0; i < msg.Replays.Length; i++)
        {
            var replay = msg.Replays[i];
            var item = Instantiate(replayItemPrefab, replayListContent);
            item.SetActive(true);

            var itemCom = item.GetComponent<Panel_StellarNetLobby_ReplayItem>();
            if (itemCom == null) itemCom = item.AddComponent<Panel_StellarNetLobby_ReplayItem>();

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

    private void OnS2C_JoinRoomResult(S2C_JoinRoomResult msg)
    {
        if (msg == null || !Panel_StellarNetLobby_RoomItem.HasPendingJoinRequest)
        {
            return;
        }

        Panel_StellarNetLobby_RoomItem.ClearPendingJoinRequest();
        if (msg.Success)
        {
            return;
        }

        NetLogger.LogInfo("Panel_StellarNetLobby", $"加入房间失败:{msg.Reason}");
        GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = $"加入房间失败: {msg.Reason}" });
    }

    #endregion

    #region 在线玩家列表处理 (全量与增量)

    private void OnS2C_OnlinePlayerListSync(S2C_OnlinePlayerListSync msg)
    {
        if (playerListContent == null || playerItemPrefab == null) return;

        foreach (Transform child in playerListContent) Destroy(child.gameObject);
        _playerItems.Clear();

        if (msg.Players == null) return;

        for (int i = 0; i < msg.Players.Length; i++)
        {
            AddOrUpdatePlayerItem(msg.Players[i]);
        }
    }

    private void OnS2C_GlobalPlayerStateIncrementalSync(S2C_GlobalPlayerStateIncrementalSync msg)
    {
        if (msg.Player == null) return;

        if (msg.IsRemoved)
        {
            if (_playerItems.TryGetValue(msg.Player.SessionId, out var item))
            {
                if (item != null) Destroy(item.gameObject);
                _playerItems.Remove(msg.Player.SessionId);
            }
        }
        else
        {
            AddOrUpdatePlayerItem(msg.Player);
        }
    }

    private void AddOrUpdatePlayerItem(OnlinePlayerInfo player)
    {
        if (playerListContent == null || playerItemPrefab == null) return;

        if (_playerItems.TryGetValue(player.SessionId, out var item) && item != null)
        {
            item.Init(player);
        }
        else
        {
            var go = Instantiate(playerItemPrefab, playerListContent);
            go.SetActive(true);
            var newItem = go.GetComponent<Panel_StellarNetLobby_PlayerItem>();
            if (newItem == null) newItem = go.AddComponent<Panel_StellarNetLobby_PlayerItem>();
            newItem.Init(player);
            _playerItems[player.SessionId] = newItem;
        }
    }

    #endregion

    #region 大厅聊天处理

    private void OnSendChatBtn()
    {
        SendChat();
    }

    private void OnChatInputSubmit(string text)
    {
        SendChat();
        if (chatInput != null)
        {
            chatInput.ActivateInputField();
        }
    }

    private void SendChat()
    {
        if (chatInput == null) return;
        string content = chatInput.text.Trim();
        if (string.IsNullOrEmpty(content)) return;

        NetClient.Send(new C2S_GlobalChat { Content = content });
        chatInput.text = string.Empty;
    }

    private void OnS2C_GlobalChatSync(S2C_GlobalChatSync msg)
    {
        if (chatContent == null || chatItemPrefab == null) return;

        var go = Instantiate(chatItemPrefab, chatContent);
        go.SetActive(true);
        var item = go.GetComponent<Panel_StellarNetLobby_ChatItem>();
        if (item == null) item = go.AddComponent<Panel_StellarNetLobby_ChatItem>();
        item.Init(msg);
    }

    #endregion
}
