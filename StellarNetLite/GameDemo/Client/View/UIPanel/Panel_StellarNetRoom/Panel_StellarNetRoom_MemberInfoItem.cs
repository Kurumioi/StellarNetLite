using TMPro;
using UnityEngine;

public class Panel_StellarNetRoom_MemberInfoItem : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text readyText;
    [SerializeField] private string uidStr;

    public void Init(string uid, string nameStr)
    {
        this.uidStr = uid;
        nameText.text = nameStr;
        SetReady(false);
    }

    public void SetReady(bool ready)
    {
        readyText.text = ready ? "<color=green>已准备</color>" : "未准备";
    }
}