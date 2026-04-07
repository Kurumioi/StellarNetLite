using TMPro;
using UnityEngine;
using StellarNet.Lite.Shared.Protocol;

public class Panel_StellarNetLobby_PlayerItem : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text stateText;

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