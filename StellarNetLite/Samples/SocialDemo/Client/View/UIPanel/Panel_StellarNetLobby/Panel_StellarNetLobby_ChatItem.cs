using System;
using TMPro;
using UnityEngine;
using StellarNet.Lite.Shared.Protocol;

/// <summary>
/// 大厅聊天条目。
/// </summary>
public class Panel_StellarNetLobby_ChatItem : MonoBehaviour
{
    [SerializeField] private TMP_Text contentText;

    public void Init(S2C_GlobalChatSync msg)
    {
        if (msg == null || contentText == null) return;

        DateTime dt = DateTimeOffset.FromUnixTimeSeconds(msg.SendUnixTime).LocalDateTime;

        // 格式化输出：[12:30:45] 玩家名: 消息内容
        contentText.text = $"<color=#A0A0A0>[{dt:HH:mm:ss}]</color> <color=#50A0FF>{msg.SenderDisplayName}</color>: {msg.Content}";
    }
}
