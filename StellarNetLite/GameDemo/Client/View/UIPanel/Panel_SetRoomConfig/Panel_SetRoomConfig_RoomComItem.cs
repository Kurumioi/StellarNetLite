using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 建房面板房间类型条目。
/// </summary>
public class Panel_SetRoomConfig_RoomComItem : MonoBehaviour
{
    [SerializeField] private Toggle chooseTog;
    [SerializeField] private TMP_Text comNameText;

    public int TemplateIndex { get; private set; } = -1;

    public void Init(string displayName, int templateIndex)
    {
        if (chooseTog == null || comNameText == null)
        {
            Debug.LogError($"[Panel_SetRoomConfig_RoomComItem] 初始化失败: UI 引用未绑定, Object:{name}");
            return;
        }

        TemplateIndex = templateIndex;
        chooseTog.isOn = false;
        chooseTog.interactable = true;
        comNameText.text = string.IsNullOrEmpty(displayName) ? $"房间类型_{templateIndex}" : displayName;
    }

    public void SetSelected(bool selected)
    {
        if (chooseTog != null)
        {
            chooseTog.isOn = selected;
        }
    }

    public bool IsChoose()
    {
        return chooseTog != null && chooseTog.isOn;
    }

    public Toggle GetToggle()
    {
        return chooseTog;
    }
}