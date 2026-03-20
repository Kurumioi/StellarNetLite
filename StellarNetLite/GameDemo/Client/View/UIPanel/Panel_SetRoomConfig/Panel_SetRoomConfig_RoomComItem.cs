using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 建房面板房间类型条目。
/// 我把它从“组件勾选项”收成“房间类型选择项”，是为了让 UI 语义和当前业务入口保持一致，
/// 避免脚本命名和字段含义继续停留在旧的组件拼装模式里。
/// </summary>
public class Panel_SetRoomConfig_RoomComItem : MonoBehaviour
{
    [SerializeField] private Toggle chooseTog;
    [SerializeField] private TMP_Text comNameText;

    /// <summary>
    /// 房间类型模板索引。
    /// 我保留 int 索引而不是直接挂模板引用，是为了继续兼容当前 UI 预制体和面板生成流程，减少序列化与运行时引用复杂度。
    /// </summary>
    public int TemplateIndex { get; private set; } = -1;

    /// <summary>
    /// 初始化房间类型条目。
    /// 我让外部显式传入模板显示名和模板索引，是为了让这个条目只做纯表现和选择，不感知模板注册表的来源。
    /// </summary>
    public void Init(string displayName, int templateIndex)
    {
        if (chooseTog == null)
        {
            Debug.LogError($"[Panel_SetRoomConfig_RoomComItem] 初始化失败: chooseTog 未绑定, Object:{name}, TemplateIndex:{templateIndex}, DisplayName:{displayName}");
            return;
        }

        if (comNameText == null)
        {
            Debug.LogError($"[Panel_SetRoomConfig_RoomComItem] 初始化失败: comNameText 未绑定, Object:{name}, TemplateIndex:{templateIndex}, DisplayName:{displayName}");
            return;
        }

        TemplateIndex = templateIndex;
        chooseTog.isOn = false;
        chooseTog.interactable = true;
        comNameText.text = string.IsNullOrEmpty(displayName) ? $"房间类型_{templateIndex}" : displayName;
    }

    /// <summary>
    /// 设置当前选择状态。
    /// 我保留外部强控入口，是为了让面板层可以统一实现“单选房间类型”，而不是让条目脚本各自维护分散的互斥逻辑。
    /// </summary>
    public void SetSelected(bool selected)
    {
        if (chooseTog == null)
        {
            Debug.LogError($"[Panel_SetRoomConfig_RoomComItem] 设置选中状态失败: chooseTog 为空, Object:{name}, TemplateIndex:{TemplateIndex}, Selected:{selected}");
            return;
        }

        chooseTog.isOn = selected;
    }

    /// <summary>
    /// 读取当前是否选中。
    /// 我保留最小只读接口，是为了让面板层只依赖条目的选择结果，而不直接操作内部 Toggle。
    /// </summary>
    public bool IsChoose()
    {
        if (chooseTog == null)
        {
            Debug.LogError($"[Panel_SetRoomConfig_RoomComItem] 读取选中状态失败: chooseTog 为空, Object:{name}, TemplateIndex:{TemplateIndex}");
            return false;
        }

        return chooseTog.isOn;
    }

    /// <summary>
    /// 获取内部 Toggle。
    /// 我显式暴露只读引用，是为了让面板层可以注册单选互斥逻辑，而不是依赖脆弱的 GetComponent 链式查找。
    /// </summary>
    public Toggle GetToggle()
    {
        if (chooseTog == null)
        {
            Debug.LogError($"[Panel_SetRoomConfig_RoomComItem] 获取 Toggle 失败: chooseTog 为空, Object:{name}, TemplateIndex:{TemplateIndex}");
            return null;
        }

        return chooseTog;
    }
}