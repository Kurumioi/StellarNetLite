using System;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using StellarFramework;
using StellarFramework.UI;
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
    [SerializeField] private GameObject replayItemPrefab; // 需在编辑器中制作一个包含 Text 和 Button 的预制体

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

        // 新增：注册回放相关事件
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

    #region 按钮交互

    private void OnLogoutBtn()
    {
        GameLauncher.NetManager.StopClient();
    }

    private void OnRefreshRoomListBtn()
    {
        var msg = new C2S_GetRoomList();
        GameLauncher.ClientSendMessage(msg);
        LogKit.Log("Panel_StellarNetLobby", "请求房间列表");
    }

    private void OnCreateRoomBtn()
    {
        UIKit.OpenPanel<Panel_SetRoomConfig>();
    }

    private void OnRefreshReplayBtn()
    {
        var msg = new C2S_GetReplayList();
        GameLauncher.ClientSendMessage(msg);
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

    #region 网络事件 - 回放 (新增)

    private void OnS2C_ReplayList(S2C_ReplayList msg)
    {
        if (replayListContent == null || replayItemPrefab == null) return;
        replayListContent.ClearChildren();
        if (msg.ReplayIds == null || msg.ReplayIds.Length == 0) return;

        foreach (var replayId in msg.ReplayIds)
        {
            var item = Instantiate(replayItemPrefab, replayListContent);
            item.Show();

            // 假设预制体下有两个子节点：Text 显示 ID，Button 用于下载
            var text = item.GetComponentInChildren<TMP_Text>();
            var btn = item.GetComponentInChildren<Button>();

            if (text != null) text.text = replayId;

            if (btn != null)
            {
                string rId = replayId; // 闭包捕获
                btn.onClick.AddListener(() =>
                {
                    if (!string.IsNullOrEmpty(_downloadingReplayId)) return;
                    _downloadingReplayId = rId;

                    // 核心修改：调用 ClientReplayModule 的静态入口，接管缓存与断点续传逻辑
                    StellarNet.Lite.Client.Modules.ClientReplayModule.RequestDownload(GameLauncher.NetManager.ClientApp, rId);

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
                    // 核心闭环：关闭大厅，携带数据打开回放面板
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