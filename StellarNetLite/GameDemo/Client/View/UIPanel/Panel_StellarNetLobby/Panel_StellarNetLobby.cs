using System;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using StellarFramework;
using StellarFramework.UI;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
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

    [Header("录像列表 (新增)")] [SerializeField] private Button refreshReplayBtn;
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

        if (refreshReplayBtn != null)
        {
            refreshReplayBtn.onClick.AddListener(OnRefreshReplayBtn);
        }

        GlobalTypeNetEvent.Register<S2C_RoomListResponse>(OnS2C_RoomListResponse)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
        GlobalTypeNetEvent.Register<S2C_ReplayList>(OnS2C_ReplayList)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
        GlobalTypeNetEvent.Register<S2C_DownloadReplayResult>(OnS2C_DownloadReplayResult)
            .UnRegisterWhenGameObjectDestroyed(gameObject);

        // 核心修复：大厅统一监听本地房间进入事件，确保装配完毕后再切 UI
        GlobalTypeNetEvent.Register<Local_RoomEntered>(OnRoomEntered)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
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

    private void OnRoomEntered(Local_RoomEntered evt)
    {
        // 确保只有在线模式才跳转房间 UI（排除回放沙盒触发的事件）
        if (NetClient.State == ClientAppState.OnlineRoom)
        {
            LogKit.Log("Panel_StellarNetLobby", "检测到房间装配完毕，执行 UI 路由跳转");
            UIKit.ClosePanel<Panel_SetRoomConfig>();
            UIKit.OpenPanel<Panel_StellarNetRoom>();
            CloseSelf();
        }
    }

    #region 按钮交互

    private void OnLogoutBtn()
    {
        GameLauncher.NetManager.StopClient();
    }

    private void OnRefreshRoomListBtn()
    {
        var msg = new C2S_GetRoomList();
        NetClient.Send(msg);
        LogKit.Log("Panel_StellarNetLobby", "请求房间列表");
    }

    private void OnCreateRoomBtn()
    {
        UIKit.OpenPanel<Panel_SetRoomConfig>();
    }

    private void OnRefreshReplayBtn()
    {
        var msg = new C2S_GetReplayList();
        NetClient.Send(msg);
        LogKit.Log("Panel_StellarNetLobby", "请求录像列表");
    }

    #endregion

    #region 网络事件 - 房间

    private void OnS2C_RoomListResponse(S2C_RoomListResponse msg)
    {
        var roomList = msg.Rooms;
        LogKit.Log("Panel_StellarNetLobby", $"收到房间列表 {roomList.Length} 个房间");
        roomListContent.ClearChildren();

        for (int i = 0; i < roomList.Length; i++)
        {
            var room = roomList[i];
            var roomItem = Instantiate(roomItemPrefab, roomListContent);
            roomItem.Show();
            roomItem.GetComponent<Panel_StellarNetLobby_RoomItem>()
                .Init(room.RoomName, room.RoomId, room.MemberCount, room.MaxMembers, GetRoomStateByInt(room.State));
        }
    }

    private string GetRoomStateByInt(int stateId)
    {
        string returnValue = null;
        switch (stateId)
        {
            case 0: returnValue = "等待中"; break;
            case 1: returnValue = "游戏中"; break;
            case 2: returnValue = "已结束"; break;
        }

        return returnValue;
    }

    #endregion

    #region 网络事件 - 回放

    private void OnS2C_ReplayList(S2C_ReplayList msg)
    {
        if (replayListContent == null || replayItemPrefab == null) return;
        replayListContent.ClearChildren();

        if (msg.ReplayIds == null || msg.ReplayIds.Length == 0) return;

        foreach (var replayId in msg.ReplayIds)
        {
            var item = Instantiate(replayItemPrefab, replayListContent);
            item.Show();
            var text = item.GetComponentInChildren<TMP_Text>();
            var btn = item.GetComponentInChildren<Button>();

            if (text != null) text.text = replayId;

            if (btn != null)
            {
                string rId = replayId;
                btn.onClick.AddListener(() =>
                {
                    if (!string.IsNullOrEmpty(_downloadingReplayId)) return;
                    _downloadingReplayId = rId;
                    StellarNet.Lite.Client.Modules.ClientReplayModule.RequestDownload(NetClient.App, rId);
                    text.text = $"{rId} (下载/加载中...)";
                });
            }
        }
    }

    private void OnS2C_DownloadReplayResult(S2C_DownloadReplayResult msg)
    {
        _downloadingReplayId = string.Empty;

        if (msg.Success && !string.IsNullOrEmpty(msg.ReplayFileData))
        {
            try
            {
                var replayFile = JsonConvert.DeserializeObject<ReplayFile>(msg.ReplayFileData);
                if (replayFile != null)
                {
                    LogKit.Log("Panel_StellarNetLobby", "录像解析成功，准备进入回放沙盒");
                    UIKit.OpenPanel<Panel_StellarNetReplay>(replayFile);
                    CloseSelf();
                }
            }
            catch (Exception e)
            {
                LogKit.LogError("Panel_StellarNetLobby", $"录像解析异常: {e.Message}");
                GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = "录像文件损坏或解析失败" });
            }
        }
        else
        {
            GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = $"录像下载失败: {msg.Reason}" });
        }
    }

    #endregion
}