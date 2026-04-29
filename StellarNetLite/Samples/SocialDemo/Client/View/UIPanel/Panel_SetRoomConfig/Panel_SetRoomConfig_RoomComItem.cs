using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 建房面板房间类型条目。
/// </summary>
public class Panel_SetRoomConfig_RoomComItem : MonoBehaviour
{
    /// <summary>
    /// 房型单选 Toggle。
    /// </summary>
    [SerializeField] private Toggle chooseTog;

    /// <summary>
    /// 房型名称文本。
    /// </summary>
    [SerializeField] private TMP_Text comNameText;

    /// <summary>
    /// 对应模板在列表中的索引。
    /// </summary>
    public int TemplateIndex { get; private set; } = -1;

    /// <summary>
    /// 初始化房型名称和模板索引。
    /// </summary>
    public void Init(string displayName, int templateIndex)
    {
        if (chooseTog == null || comNameText == null)
        {
            Debug.LogError($"[Panel_SetRoomConfig_RoomComItem] 初始化失败: UI 引用未绑定, Object:{name}");
            return;
        }

        // 条目初始化时只负责显示，不直接注册点击逻辑。
        TemplateIndex = templateIndex;
        chooseTog.isOn = false;
        chooseTog.interactable = true;
        comNameText.text = string.IsNullOrEmpty(displayName) ? $"房间类型_{templateIndex}" : displayName;
    }

    /// <summary>
    /// 设置当前条目的选中状态。
    /// </summary>
    public void SetSelected(bool selected)
    {
        // 由上层面板统一控制单选状态。
        if (chooseTog != null)
        {
            chooseTog.isOn = selected;
        }
    }

    /// <summary>
    /// 返回当前 Toggle 是否选中。
    /// </summary>
    public bool IsChoose()
    {
        return chooseTog != null && chooseTog.isOn;
    }

    /// <summary>
    /// 返回当前条目的 Toggle 组件。
    /// </summary>
    public Toggle GetToggle()
    {
        return chooseTog;
    }
}
