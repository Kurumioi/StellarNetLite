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
    /// <summary>
    /// 房间名输入框。
    /// </summary>
    [Header("基础配置")] [SerializeField] private TMP_InputField roomNameIpt;

    /// <summary>
    /// 最大人数滑条。
    /// </summary>
    [SerializeField] private Slider memberCountSlider;

    /// <summary>
    /// 最大人数显示文本。
    /// </summary>
    [SerializeField] private TMP_Text memberCountText;

    /// <summary>
    /// 指定房间 Id 输入框。
    /// 留空时走普通建房。
    /// </summary>
    [Header("高级配置 (可选)")] [SerializeField] private TMP_InputField customRoomIdIpt;

    /// <summary>
    /// 是否启用录像录制。
    /// </summary>
    [SerializeField] private Toggle enableReplayRecordingTog;

    /// <summary>
    /// 房间类型列表父节点。
    /// </summary>
    [Header("组件模板")] [SerializeField] private Transform roomComContent;

    /// <summary>
    /// 房间类型列表项预制体。
    /// </summary>
    [SerializeField] private GameObject roomComPrefab;

    /// <summary>
    /// 取消按钮。
    /// </summary>
    [Header("操作按钮")] [SerializeField] private Button cancelBtn;

    /// <summary>
    /// 创建或加入按钮。
    /// </summary>
    [SerializeField] private Button createBtn;

    /// <summary>
    /// 当前可选的房间模板列表。
    /// </summary>
    private readonly List<RoomTypeTemplateRegistry.RoomTypeTemplate> _roomTypeTemplates =
        new List<RoomTypeTemplateRegistry.RoomTypeTemplate>();

    /// <summary>
    /// 当前已实例化的房间模板条目。
    /// </summary>
    private readonly List<Panel_SetRoomConfig_RoomComItem> _roomTypeItems = new List<Panel_SetRoomConfig_RoomComItem>();

    /// <summary>
    /// 当前选中的模板索引。
    /// </summary>
    private int _selectedTemplateIndex = -1;

    /// <summary>
    /// 是否正在等待服务端返回建房或加房结果。
    /// </summary>
    private bool _isRequesting;

    /// <summary>
    /// 请求发起时间。
    /// </summary>
    private float _requestStartTime;

    /// <summary>
    /// 建房或加房请求超时时间。
    /// </summary>
    private const float RequestTimeoutSeconds = 5f;

    /// <summary>
    /// 初始化建房面板事件和模板列表。
    /// </summary>
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

    /// <summary>
    /// 销毁面板时解除事件和 Toggle 监听。
    /// </summary>
    private void OnDestroy()
    {
        cancelBtn?.onClick.RemoveAllListeners();
        createBtn?.onClick.RemoveAllListeners();
        memberCountSlider?.onValueChanged.RemoveAllListeners();
        ClearRoomTypeItemToggleListeners();
    }

    /// <summary>
    /// 打开面板时恢复默认交互状态。
    /// </summary>
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

    /// <summary>
    /// 轮询请求超时并恢复按钮状态。
    /// </summary>
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

    /// <summary>
    /// 根据滑条值刷新人数文案。
    /// </summary>
    private void OnMemberCountSlider(float value)
    {
        if (memberCountText != null)
        {
            memberCountText.text = ((int)value) + "人";
        }
    }

    /// <summary>
    /// 构建建房或指定 Id 加房请求。
    /// </summary>
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

    /// <summary>
    /// 关闭当前面板。
    /// </summary>
    private void OnCancelBtn()
    {
        CloseSelf();
    }

    /// <summary>
    /// 处理普通建房返回结果。
    /// </summary>
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

    /// <summary>
    /// 处理加入已有房间返回结果。
    /// </summary>
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

    /// <summary>
    /// 重新生成房间类型模板列表。
    /// </summary>
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

    /// <summary>
    /// 实例化单个房间类型条目并挂上单选回调。
    /// </summary>
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

    /// <summary>
    /// 统一维护房间类型的单选状态。
    /// </summary>
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

    /// <summary>
    /// 在未选中任何模板时自动选中第一项。
    /// </summary>
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

    /// <summary>
    /// 读取当前选中的房间组件 Id 列表。
    /// </summary>
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

    /// <summary>
    /// 清理所有模板 Toggle 的事件监听。
    /// </summary>
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
