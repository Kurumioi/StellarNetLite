using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using StellarFramework;
using StellarFramework.UI;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Binders;
using StellarNet.Lite.Shared.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Panel_SetRoomConfig : UIPanelBase
{
    [SerializeField] private TMP_InputField roomNameIpt;
    [SerializeField] private Slider memberCountSlider;
    [SerializeField] private TMP_Text memberCountText;
    [SerializeField] private Transform roomComContent;
    [SerializeField] private GameObject roomComPrefab;
    [SerializeField] private Button cancelBtn;
    [SerializeField] private Button createBtn;

    public override void OnInit()
    {
        base.OnInit();
        cancelBtn.onClick.AddListener(OnCancelBtn);
        createBtn.onClick.AddListener(OnCreateBtn);
        memberCountSlider.onValueChanged.AddListener(OnMemberCountSlider);

        GlobalTypeNetEvent.Register<S2C_CreateRoomResult>(OnS2C_CreateRoomResult)
            .UnRegisterWhenGameObjectDestroyed(gameObject);

        for (int i = 0; i < AutoRegistry.RoomComponentMetaList.Count; i++)
        {
            GenerateComItem(AutoRegistry.RoomComponentMetaList[i].Name, AutoRegistry.RoomComponentMetaList[i].Id);
        }
    }

    private void OnDestroy()
    {
        cancelBtn.onClick.RemoveAllListeners();
        createBtn.onClick.RemoveAllListeners();
        memberCountSlider.onValueChanged.RemoveAllListeners();
    }

    public override async UniTask OnOpen(object uiData = null)
    {
        await base.OnOpen(uiData);
        createBtn.interactable = true; // 每次打开恢复按钮状态
    }

    #region UI 事件

    private void OnMemberCountSlider(float arg0)
    {
        memberCountText.text = (int)arg0 + "人";
    }

    private void OnCreateBtn()
    {
        var roomName = roomNameIpt.text;
        if (string.IsNullOrEmpty(roomName))
        {
            LogKit.LogError("Panel_SetRoomConfig", "请输入房间名称");
            return;
        }

        createBtn.interactable = false; // 防连点

        var memberCount = (int)memberCountSlider.value;
        var roomComIds = GetRoomConIds();

        LogKit.Log("Panel_SetRoomConfig", $"请求创建房间 {roomName} {memberCount} {roomComIds}");

        var msg = new C2S_CreateRoom
        {
            RoomName = roomName,
            ComponentIds = roomComIds.ToArray(),
            MaxMembers = memberCount
        };

        GameLauncher.ClientSendMessage(msg);

        // 核心修复 3：移除危险的 MonoKit.Sequence 轮询，完全交由 OnS2C_CreateRoomResult 事件驱动
    }

    private void OnCancelBtn()
    {
        CloseSelf();
    }

    #endregion

    #region net 事件

    private void OnS2C_CreateRoomResult(S2C_CreateRoomResult msg)
    {
        if (msg.Success)
        {
            LogKit.Log("Panel_SetRoomConfig", "创建房间成功! 进入房间");
            UIKit.ClosePanel<Panel_StellarNetLobby>();
            UIKit.OpenPanel<Panel_StellarNetRoom>();
            CloseSelf();
        }
        else
        {
            createBtn.interactable = true; // 失败则恢复按钮
            LogKit.LogError("Panel_SetRoomConfig", $"创建房间失败: {msg.Reason}");
            GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = $"创建房间失败: {msg.Reason}" });
        }
    }

    #endregion

    private void GenerateComItem(string comName, int comId)
    {
        var roomComItem = Instantiate(roomComPrefab, roomComContent);
        roomComItem.Show();
        roomComItem.GetComponent<Panel_SetRoomConfig_RoomComItem>().Init(comName, comId);
    }

    public List<int> GetRoomConIds()
    {
        List<int> returnValue = new List<int>();
        var roomComItems = roomComContent.GetComponentsInChildren<Panel_SetRoomConfig_RoomComItem>();
        for (int i = 0; i < roomComItems.Length; i++)
        {
            var roomComItem = roomComItems[i];
            if (roomComItem.IsChoose())
            {
                returnValue.Add(roomComItem.ComId);
            }
        }

        return returnValue;
    }
}