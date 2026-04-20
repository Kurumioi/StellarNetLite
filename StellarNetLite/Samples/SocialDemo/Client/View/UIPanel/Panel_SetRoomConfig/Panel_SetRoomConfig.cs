using System.Collections.Generic;
using StellarNet.UI;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Binders;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Shared.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 建房与指定房间加入配置面板。
/// </summary>
public class Panel_SetRoomConfig : UIPanelBase
{
    [Header("基础配置")] [SerializeField] private TMP_InputField roomNameIpt;
    [SerializeField] private Slider memberCountSlider;
    [SerializeField] private TMP_Text memberCountText;

    [Header("高级配置 (可选)")] [SerializeField] private TMP_InputField customRoomIdIpt;
    [SerializeField] private Toggle enableReplayRecordingTog;

    [Header("组件模板")] [SerializeField] private Transform roomComContent;
    [SerializeField] private GameObject roomComPrefab;

    [Header("操作按钮")] [SerializeField] private Button cancelBtn;
    [SerializeField] private Button createBtn;

    private readonly List<RoomTypeTemplateRegistry.RoomTypeTemplate> _roomTypeTemplates =
        new List<RoomTypeTemplateRegistry.RoomTypeTemplate>();

    private readonly List<Panel_SetRoomConfig_RoomComItem> _roomTypeItems = new List<Panel_SetRoomConfig_RoomComItem>();

    private int _selectedTemplateIndex = -1;
    private bool _isRequesting;
    private float _requestStartTime;
    private const float RequestTimeoutSeconds = 5f;

    public override void OnInit()
    {
        base.OnInit();

        if (cancelBtn == null || createBtn == null || memberCountSlider == null || memberCountText == null ||
            roomComContent == null || roomComPrefab == null)
        {
            NetLogger.LogError("Panel_SetRoomConfig", "初始化失败: 存在未绑定的 UI 引用");
            return;
        }

        cancelBtn.onClick.AddListener(OnCancelBtn);
        createBtn.onClick.AddListener(OnCreateBtn);
        memberCountSlider.onValueChanged.AddListener(OnMemberCountSlider);

        GlobalTypeNetEvent.Register<S2C_CreateRoomResult>(OnS2C_CreateRoomResult)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
        GlobalTypeNetEvent.Register<S2C_JoinRoomResult>(OnS2C_JoinRoomResult)
            .UnRegisterWhenGameObjectDestroyed(gameObject);

        GenerateRoomTypeItems();
    }

    private void OnDestroy()
    {
        cancelBtn?.onClick.RemoveAllListeners();
        createBtn?.onClick.RemoveAllListeners();
        memberCountSlider?.onValueChanged.RemoveAllListeners();
        ClearRoomTypeItemToggleListeners();
    }

    public override void OnOpen(object uiData = null)
    {
        base.OnOpen(uiData);
        _isRequesting = false;
        if (createBtn != null)
        {
            createBtn.interactable = true;
        }

        if (customRoomIdIpt != null)
        {
            customRoomIdIpt.text = string.IsNullOrEmpty(uiData as string) ? string.Empty : (string)uiData;
        }

        ApplyDefaultSelectionIfNeeded();
    }

    private void Update()
    {
        if (_isRequesting)
        {
            if (Time.realtimeSinceStartup - _requestStartTime > RequestTimeoutSeconds)
            {
                _isRequesting = false;
                if (createBtn != null) createBtn.interactable = true;
                NetLogger.LogWarning("Panel_SetRoomConfig", "创建/加入房间请求超时，已恢复 UI 交互");
                GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = "请求超时，请重试" });
            }
        }
    }

    private void OnMemberCountSlider(float value)
    {
        if (memberCountText != null)
        {
            memberCountText.text = ((int)value) + "人";
        }
    }

    private void OnCreateBtn()
    {
        string roomName = roomNameIpt != null ? roomNameIpt.text : string.Empty;
        if (string.IsNullOrEmpty(roomName))
        {
            NetLogger.LogError("Panel_SetRoomConfig", "操作失败: 请输入房间名称");
            GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = "请输入房间名称" });
            return;
        }

        List<int> roomComIds = GetSelectedRoomTypeComponentIds();
        if (roomComIds.Count == 0)
        {
            NetLogger.LogError("Panel_SetRoomConfig",
                $"操作失败: 未选择有效房间类型模板, SelectedTemplateIndex:{_selectedTemplateIndex}");
            return;
        }

        _isRequesting = true;
        _requestStartTime = Time.realtimeSinceStartup;
        createBtn.interactable = false;

        int memberCount = (int)memberCountSlider.value;
        string customRoomId = customRoomIdIpt != null ? customRoomIdIpt.text.Trim() : string.Empty;

        RoomDTO roomDto = new RoomDTO
        {
            RoomName = roomName,
            ComponentIds = roomComIds.ToArray(),
            MaxMembers = memberCount,
            EnableReplayRecording = enableReplayRecordingTog != null && enableReplayRecordingTog.isOn,
            Password = string.Empty,
            CustomProperties = null
        };

        if (string.IsNullOrEmpty(customRoomId))
        {
            NetLogger.LogInfo("Panel_SetRoomConfig", $"请求常规创建房间 {roomName} {memberCount}人");
            NetClient.Send(new C2S_CreateRoom { RoomConfig = roomDto });
        }
        else
        {
            NetLogger.LogInfo("Panel_SetRoomConfig", $"请求指定ID加入或创建房间 ID:{customRoomId}");
            NetClient.Send(new C2S_JoinOrCreateRoom { RoomId = customRoomId, RoomConfig = roomDto });
        }
    }

    private void OnCancelBtn()
    {
        CloseSelf();
    }

    private void OnS2C_CreateRoomResult(S2C_CreateRoomResult msg)
    {
        _isRequesting = false;
        if (msg == null) return;

        if (!msg.Success)
        {
            if (createBtn != null) createBtn.interactable = true;
            NetLogger.LogError("Panel_SetRoomConfig", $"创建房间失败: {msg.Reason}");
            GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = $"创建房间失败: {msg.Reason}" });
        }
        else
        {
            NetLogger.LogInfo("Panel_SetRoomConfig", $"成功创建并进入房间: {msg.RoomId}");
            GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = $"成功创建房间 [{msg.RoomId}]" });
        }
    }

    private void OnS2C_JoinRoomResult(S2C_JoinRoomResult msg)
    {
        _isRequesting = false;
        if (msg == null) return;

        if (!msg.Success)
        {
            if (createBtn != null) createBtn.interactable = true;
            NetLogger.LogError("Panel_SetRoomConfig", $"加入房间失败: {msg.Reason}");
            GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = $"加入房间失败: {msg.Reason}" });
        }
        else
        {
            NetLogger.LogInfo("Panel_SetRoomConfig", $"成功加入已有房间: {msg.RoomId}");
            GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = $"成功加入已有房间 [{msg.RoomId}]" });
        }
    }

    private void GenerateRoomTypeItems()
    {
        ClearRoomTypeItemToggleListeners();

        foreach (Transform child in roomComContent)
        {
            Destroy(child.gameObject);
        }

        _roomTypeTemplates.Clear();
        _roomTypeItems.Clear();
        _selectedTemplateIndex = -1;

        IReadOnlyList<RoomTypeTemplateRegistry.RoomTypeTemplate> templates = RoomTypeTemplateRegistry.GetAllTemplates();
        if (templates == null || templates.Count == 0) return;

        for (int i = 0; i < templates.Count; i++)
        {
            var template = templates[i];
            if (template == null || string.IsNullOrEmpty(template.TypeName) || template.ComponentIds == null ||
                template.ComponentIds.Length == 0)
                continue;

            _roomTypeTemplates.Add(template);
            GenerateTypeItem(template.TypeName, _roomTypeTemplates.Count - 1);
        }

        ApplyDefaultSelectionIfNeeded();
    }

    private void GenerateTypeItem(string typeName, int templateIndex)
    {
        GameObject roomTypeItem = Instantiate(roomComPrefab, roomComContent);
        roomTypeItem.SetActive(true);

        Panel_SetRoomConfig_RoomComItem item = roomTypeItem.GetComponent<Panel_SetRoomConfig_RoomComItem>();
        if (item == null)
        {
            Destroy(roomTypeItem);
            return;
        }

        item.Init(typeName, templateIndex);
        _roomTypeItems.Add(item);

        Toggle toggle = item.GetToggle();
        if (toggle != null)
        {
            int capturedTemplateIndex = templateIndex;
            toggle.onValueChanged.AddListener(isOn => OnRoomTypeToggleChanged(capturedTemplateIndex, isOn));
        }
    }

    private void OnRoomTypeToggleChanged(int templateIndex, bool isOn)
    {
        if (!isOn)
        {
            if (_selectedTemplateIndex == templateIndex) _selectedTemplateIndex = -1;
            return;
        }

        _selectedTemplateIndex = templateIndex;

        for (int i = 0; i < _roomTypeItems.Count; i++)
        {
            var item = _roomTypeItems[i];
            if (item == null) continue;

            bool shouldSelected = item.TemplateIndex == templateIndex;
            if (item.IsChoose() != shouldSelected)
            {
                item.SetSelected(shouldSelected);
            }
        }
    }

    private void ApplyDefaultSelectionIfNeeded()
    {
        if (_roomTypeItems.Count == 0 ||
            (_selectedTemplateIndex >= 0 && _selectedTemplateIndex < _roomTypeTemplates.Count))
            return;

        var firstItem = _roomTypeItems[0];
        if (firstItem != null)
        {
            firstItem.SetSelected(true);
            _selectedTemplateIndex = firstItem.TemplateIndex;
        }
    }

    private List<int> GetSelectedRoomTypeComponentIds()
    {
        List<int> result = new List<int>();
        if (_selectedTemplateIndex < 0 || _selectedTemplateIndex >= _roomTypeTemplates.Count) return result;

        var template = _roomTypeTemplates[_selectedTemplateIndex];
        if (template == null || template.ComponentIds == null) return result;

        for (int i = 0; i < template.ComponentIds.Length; i++)
        {
            int componentId = template.ComponentIds[i];
            if (!result.Contains(componentId))
            {
                result.Add(componentId);
            }
        }

        return result;
    }

    private void ClearRoomTypeItemToggleListeners()
    {
        for (int i = 0; i < _roomTypeItems.Count; i++)
        {
            var item = _roomTypeItems[i];
            if (item != null && item.GetToggle() != null)
            {
                item.GetToggle().onValueChanged.RemoveAllListeners();
            }
        }
    }
}
