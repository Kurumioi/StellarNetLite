using TMPro;
using UnityEngine;
using StellarNet.Lite.Shared.Protocol;

/// <summary>
/// 大厅在线玩家条目。
/// </summary>
public class Panel_StellarNetLobby_PlayerItem : MonoBehaviour
{
    /// <summary>
    /// 玩家显示名文本。
    /// </summary>
    [SerializeField] private TMP_Text nameText;

    /// <summary>
    /// 玩家当前状态文本。
    /// </summary>
    [SerializeField] private TMP_Text stateText;

    /// <summary>
    /// 根据在线状态刷新大厅玩家条目。
    /// </summary>
    public void Init(OnlinePlayerInfo info)
    {
        if (info == null) return;

        if (nameText != null)
        {
            nameText.text = info.DisplayName;
        }

        if (stateText != null)
        {
            if (!info.IsOnline)
            {
                stateText.text = "<color=#808080FF>断线挂起</color>";
            }
            else if (info.IsInRoom)
            {
                stateText.text = $"<color=#FFFF00FF>房间中</color>";
            }
            else
            {
                stateText.text = "<color=#00FF00FF>大厅空闲</color>";
            }
        }
    }
}
