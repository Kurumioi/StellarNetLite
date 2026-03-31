using TMPro;
using UnityEngine;

/// <summary>
/// Room member list item.
/// Only displays member name and ready state.
/// </summary>
public class Panel_StellarNetRoom_MemberInfoItem : MonoBehaviour
{
    // Member name text and ready state text.
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text readyText;
    // Cached uid for debugging.
    [SerializeField] private string uidStr;

    public void Init(string uid, string nameStr)
    {
        // Initialize display and default to not ready.
        uidStr = uid;
        nameText.text = nameStr;
        SetReady(false);
    }

    public void SetReady(bool ready)
    {
        // Update ready label only.
        readyText.text = ready ? "<color=green>\u5DF2\u51C6\u5907</color>" : "\u672A\u51C6\u5907";
    }
}
