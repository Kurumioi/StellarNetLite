using TMPro;
using UnityEngine;

/// <summary>
/// 房间成员列表条目。
/// </summary>
public class Panel_StellarNetRoom_MemberInfoItem : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text readyText;

    [SerializeField] private string accountIdStr;

    public void Init(string accountId, string nameStr)
    {
        accountIdStr = accountId;
        nameText.text = nameStr;
        SetReady(false);
    }

    public void SetReady(bool ready)
    {
        readyText.text = ready ? "<color=green>\u5DF2\u51C6\u5907</color>" : "\u672A\u51C6\u5907";
    }
}
