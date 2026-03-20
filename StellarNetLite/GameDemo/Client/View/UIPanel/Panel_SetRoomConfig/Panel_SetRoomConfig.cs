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

    private readonly List<RoomTypeTemplateRegistry.RoomTypeTemplate> _roomTypeTemplates =
        new List<RoomTypeTemplateRegistry.RoomTypeTemplate>();

    private readonly List<Panel_SetRoomConfig_RoomComItem> _roomTypeItems =
        new List<Panel_SetRoomConfig_RoomComItem>();

    private int _selectedTemplateIndex = -1;

    public override void OnInit()
    {
        base.OnInit();

        if (cancelBtn == null)
        {
            LogKit.LogError("Panel_SetRoomConfig", "初始化失败: cancelBtn 未绑定");
            return;
        }

        if (createBtn == null)
        {
            LogKit.LogError("Panel_SetRoomConfig", "初始化失败: createBtn 未绑定");
            return;
        }

        if (memberCountSlider == null)
        {
            LogKit.LogError("Panel_SetRoomConfig", "初始化失败: memberCountSlider 未绑定");
            return;
        }

        if (memberCountText == null)
        {
            LogKit.LogError("Panel_SetRoomConfig", "初始化失败: memberCountText 未绑定");
            return;
        }

        if (roomComContent == null)
        {
            LogKit.LogError("Panel_SetRoomConfig", "初始化失败: roomComContent 未绑定");
            return;
        }

        if (roomComPrefab == null)
        {
            LogKit.LogError("Panel_SetRoomConfig", "初始化失败: roomComPrefab 未绑定");
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
        if (cancelBtn != null)
        {
            cancelBtn.onClick.RemoveAllListeners();
        }

        if (createBtn != null)
        {
            createBtn.onClick.RemoveAllListeners();
        }

        if (memberCountSlider != null)
        {
            memberCountSlider.onValueChanged.RemoveAllListeners();
        }

        ClearRoomTypeItemToggleListeners();
    }

    public override async UniTask OnOpen(object uiData = null)
    {
        await base.OnOpen(uiData);

        if (createBtn != null)
        {
            createBtn.interactable = true;
        }

        ApplyDefaultSelectionIfNeeded();
    }

    #region UI 事件

    private void OnMemberCountSlider(float value)
    {
        if (memberCountText == null)
        {
            LogKit.LogError("Panel_SetRoomConfig", $"更新人数文本失败: memberCountText 为空, Value:{value}");
            return;
        }

        memberCountText.text = ((int)value) + "人";
    }

    private void OnCreateBtn()
    {
        if (roomNameIpt == null)
        {
            LogKit.LogError("Panel_SetRoomConfig", "创建房间失败: roomNameIpt 为空");
            return;
        }

        string roomName = roomNameIpt.text;
        if (string.IsNullOrEmpty(roomName))
        {
            LogKit.LogError("Panel_SetRoomConfig", "创建房间失败: 请输入房间名称");
            return;
        }

        if (memberCountSlider == null)
        {
            LogKit.LogError("Panel_SetRoomConfig", $"创建房间失败: memberCountSlider 为空, RoomName:{roomName}");
            return;
        }

        List<int> roomComIds = GetSelectedRoomTypeComponentIds();
        if (roomComIds.Count == 0)
        {
            LogKit.LogError("Panel_SetRoomConfig", $"创建房间失败: 未选择有效房间类型模板, RoomName:{roomName}, SelectedTemplateIndex:{_selectedTemplateIndex}");
            return;
        }

        createBtn.interactable = false;

        int memberCount = (int)memberCountSlider.value;
        LogKit.Log("Panel_SetRoomConfig", $"请求创建房间 {roomName} {memberCount} {roomComIds}");

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

    #endregion

    #region 网络事件

    private void OnS2C_CreateRoomResult(S2C_CreateRoomResult msg)
    {
        if (msg == null)
        {
            LogKit.LogError("Panel_SetRoomConfig", "处理建房结果失败: msg 为空");
            return;
        }

        if (!msg.Success)
        {
            if (createBtn != null)
            {
                createBtn.interactable = true;
            }

            LogKit.LogError("Panel_SetRoomConfig", $"创建房间失败: {msg.Reason}");
            GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = $"创建房间失败: {msg.Reason}" });
        }
    }

    #endregion

    /// <summary>
    /// 生成人类可读的房间类型项。
    /// 我在这里渲染的是“房间模板”而不是“组件”，是为了让建房入口回到业务语义层，避免让用户继续接触底层组件组合。
    /// </summary>
    private void GenerateRoomTypeItems()
    {
        ClearRoomTypeItemToggleListeners();

        roomComContent.ClearChildren();
        _roomTypeTemplates.Clear();
        _roomTypeItems.Clear();
        _selectedTemplateIndex = -1;

        IReadOnlyList<RoomTypeTemplateRegistry.RoomTypeTemplate> templates = RoomTypeTemplateRegistry.GetAllTemplates();
        if (templates == null || templates.Count == 0)
        {
            LogKit.LogError("Panel_SetRoomConfig", "生成房间类型项失败: 未注册任何房间类型模板");
            return;
        }

        for (int i = 0; i < templates.Count; i++)
        {
            RoomTypeTemplateRegistry.RoomTypeTemplate template = templates[i];
            if (template == null)
            {
                LogKit.LogError("Panel_SetRoomConfig", $"生成房间类型项失败: 模板为空, Index:{i}");
                continue;
            }

            if (string.IsNullOrEmpty(template.TypeName))
            {
                LogKit.LogError("Panel_SetRoomConfig", $"生成房间类型项失败: TypeName 为空, Index:{i}");
                continue;
            }

            if (template.ComponentIds == null || template.ComponentIds.Length == 0)
            {
                LogKit.LogError("Panel_SetRoomConfig", $"生成房间类型项失败: ComponentIds 为空, TypeName:{template.TypeName}");
                continue;
            }

            _roomTypeTemplates.Add(template);
            GenerateTypeItem(template.TypeName, _roomTypeTemplates.Count - 1);
        }

        ApplyDefaultSelectionIfNeeded();
    }

    private void GenerateTypeItem(string typeName, int templateIndex)
    {
        GameObject roomTypeItem = Instantiate(roomComPrefab, roomComContent);
        if (roomTypeItem == null)
        {
            LogKit.LogError("Panel_SetRoomConfig", $"生成房间类型项失败: Instantiate 返回 null, TypeName:{typeName}, TemplateIndex:{templateIndex}");
            return;
        }

        roomTypeItem.Show();

        Panel_SetRoomConfig_RoomComItem item = roomTypeItem.GetComponent<Panel_SetRoomConfig_RoomComItem>();
        if (item == null)
        {
            LogKit.LogError("Panel_SetRoomConfig", $"生成房间类型项失败: 缺失 Panel_SetRoomConfig_RoomComItem, TypeName:{typeName}, TemplateIndex:{templateIndex}");
            Destroy(roomTypeItem);
            return;
        }

        item.Init(typeName, templateIndex);
        _roomTypeItems.Add(item);

        Toggle toggle = item.GetToggle();
        if (toggle == null)
        {
            LogKit.LogError("Panel_SetRoomConfig", $"生成房间类型项失败: Toggle 为空, TypeName:{typeName}, TemplateIndex:{templateIndex}");
            return;
        }

        int capturedTemplateIndex = templateIndex;
        toggle.onValueChanged.AddListener(isOn => OnRoomTypeToggleChanged(capturedTemplateIndex, isOn));
    }

    /// <summary>
    /// 单选房间类型切换逻辑。
    /// 我把互斥选择统一收口在面板层，是为了确保一次建房只对应一个模板，避免模板多选重新退化成组件拼装模式。
    /// </summary>
    private void OnRoomTypeToggleChanged(int templateIndex, bool isOn)
    {
        if (!isOn)
        {
            if (_selectedTemplateIndex == templateIndex)
            {
                _selectedTemplateIndex = -1;
            }

            return;
        }

        _selectedTemplateIndex = templateIndex;

        for (int i = 0; i < _roomTypeItems.Count; i++)
        {
            Panel_SetRoomConfig_RoomComItem item = _roomTypeItems[i];
            if (item == null)
            {
                LogKit.LogError("Panel_SetRoomConfig", $"单选房间类型失败: 条目为空, Index:{i}, SelectedTemplateIndex:{templateIndex}");
                continue;
            }

            bool shouldSelected = item.TemplateIndex == templateIndex;
            if (item.IsChoose() == shouldSelected)
            {
                continue;
            }

            item.SetSelected(shouldSelected);
        }
    }

    /// <summary>
    /// 默认选中第一种合法房间类型。
    /// 我在首次打开时自动选中第一项，是为了减少空选择导致的无效建房操作，同时保持面板进入后的默认态稳定可预期。
    /// </summary>
    private void ApplyDefaultSelectionIfNeeded()
    {
        if (_roomTypeItems.Count == 0)
        {
            _selectedTemplateIndex = -1;
            return;
        }

        if (_selectedTemplateIndex >= 0 && _selectedTemplateIndex < _roomTypeTemplates.Count)
        {
            return;
        }

        Panel_SetRoomConfig_RoomComItem firstItem = _roomTypeItems[0];
        if (firstItem == null)
        {
            LogKit.LogError("Panel_SetRoomConfig", "应用默认房间类型失败: 第一项为空");
            return;
        }

        firstItem.SetSelected(true);
        _selectedTemplateIndex = firstItem.TemplateIndex;
    }

    /// <summary>
    /// 获取当前选中的房间类型组件清单。
    /// 我只返回一个模板对应的完整组件列表，是为了保证“房间类型模板”始终代表一组原子化的业务组合，而不是可叠加的半成品配置。
    /// </summary>
    private List<int> GetSelectedRoomTypeComponentIds()
    {
        List<int> result = new List<int>();

        if (_selectedTemplateIndex < 0 || _selectedTemplateIndex >= _roomTypeTemplates.Count)
        {
            LogKit.LogError("Panel_SetRoomConfig", $"获取房间类型组件失败: 当前未选中合法模板, SelectedTemplateIndex:{_selectedTemplateIndex}, TemplateCount:{_roomTypeTemplates.Count}");
            return result;
        }

        RoomTypeTemplateRegistry.RoomTypeTemplate template = _roomTypeTemplates[_selectedTemplateIndex];
        if (template == null)
        {
            LogKit.LogError("Panel_SetRoomConfig", $"获取房间类型组件失败: 模板为空, SelectedTemplateIndex:{_selectedTemplateIndex}");
            return result;
        }

        if (template.ComponentIds == null || template.ComponentIds.Length == 0)
        {
            LogKit.LogError("Panel_SetRoomConfig", $"获取房间类型组件失败: ComponentIds 为空, TypeName:{template.TypeName}, SelectedTemplateIndex:{_selectedTemplateIndex}");
            return result;
        }

        for (int i = 0; i < template.ComponentIds.Length; i++)
        {
            int componentId = template.ComponentIds[i];
            if (result.Contains(componentId))
            {
                continue;
            }

            result.Add(componentId);
        }

        return result;
    }

    /// <summary>
    /// 清理房间类型项 Toggle 监听。
    /// 我在重建列表和销毁时统一解除监听，是为了避免窗口重复初始化或重建条目时出现旧回调残留，导致单选状态被多次驱动。
    /// </summary>
    private void ClearRoomTypeItemToggleListeners()
    {
        for (int i = 0; i < _roomTypeItems.Count; i++)
        {
            Panel_SetRoomConfig_RoomComItem item = _roomTypeItems[i];
            if (item == null)
            {
                continue;
            }

            Toggle toggle = item.GetToggle();
            if (toggle == null)
            {
                continue;
            }

            toggle.onValueChanged.RemoveAllListeners();
        }
    }
}