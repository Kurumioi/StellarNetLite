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

public class Panel_SetRoomConfig : UIPanelBase
{
    [SerializeField] private TMP_InputField roomNameIpt;
    [SerializeField] private Slider memberCountSlider;
    [SerializeField] private TMP_Text memberCountText;
    [SerializeField] private Transform roomComContent;
    [SerializeField] private GameObject roomComPrefab;
    [SerializeField] private Button cancelBtn;
    [SerializeField] private Button createBtn;

    private readonly List<RoomTypeTemplateRegistry.RoomTypeTemplate> _roomTypeTemplates =
        new List<RoomTypeTemplateRegistry.RoomTypeTemplate>();

    private readonly List<Panel_SetRoomConfig_RoomComItem> _roomTypeItems = new List<Panel_SetRoomConfig_RoomComItem>();
    private int _selectedTemplateIndex = -1;

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

        if (createBtn != null)
        {
            createBtn.interactable = true;
        }

        ApplyDefaultSelectionIfNeeded();
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
            NetLogger.LogError("Panel_SetRoomConfig", "创建房间失败: 请输入房间名称");
            return;
        }

        List<int> roomComIds = GetSelectedRoomTypeComponentIds();
        if (roomComIds.Count == 0)
        {
            NetLogger.LogError("Panel_SetRoomConfig",
                $"创建房间失败: 未选择有效房间类型模板, SelectedTemplateIndex:{_selectedTemplateIndex}");
            return;
        }

        createBtn.interactable = false;
        int memberCount = (int)memberCountSlider.value;

        NetLogger.LogInfo("Panel_SetRoomConfig", $"请求创建房间 {roomName} {memberCount} {roomComIds}");

        C2S_CreateRoom msg = new C2S_CreateRoom
        {
            RoomName = roomName,
            ComponentIds = roomComIds.ToArray(),
            MaxMembers = memberCount
        };

        NetClient.Send(msg);
    }

    private void OnCancelBtn()
    {
        CloseSelf();
    }

    private void OnS2C_CreateRoomResult(S2C_CreateRoomResult msg)
    {
        if (msg == null) return;

        if (!msg.Success)
        {
            if (createBtn != null) createBtn.interactable = true;
            NetLogger.LogError("Panel_SetRoomConfig", $"创建房间失败: {msg.Reason}");
            GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = $"创建房间失败: {msg.Reason}" });
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