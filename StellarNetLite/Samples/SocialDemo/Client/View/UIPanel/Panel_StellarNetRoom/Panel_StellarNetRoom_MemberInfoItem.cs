using TMPro;
using UnityEngine;

/// <summary>
/// 房间成员列表条目。
/// </summary>
public class Panel_StellarNetRoom_MemberInfoItem : MonoBehaviour
{
    /// <summary>
    /// 成员显示名文本。
    /// </summary>
    [SerializeField] private TMP_Text nameText;

    /// <summary>
    /// 成员准备状态文本。
    /// </summary>
    [SerializeField] private TMP_Text readyText;

    /// <summary>
    /// 当前条目对应的账号 Id。
    /// </summary>
    [SerializeField] private string accountIdStr;

    /// <summary>
    /// 初始化成员名称和默认准备状态。
    /// </summary>
    public void Init(string accountId, string nameStr)
    {
        accountIdStr = accountId;
        nameText.text = nameStr;
        SetReady(false);
    }

    /// <summary>
    /// 刷新当前成员的准备文案。
    /// </summary>
    public void SetReady(bool ready)
    {
        readyText.text = ready ? "<color=green>\u5DF2\u51C6\u5907</color>" : "\u672A\u51C6\u5907";
    }
}
