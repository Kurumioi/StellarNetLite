using StellarNet.UI;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Shared.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 大厅房间列表条目。
/// </summary>
public class Panel_StellarNetLobby_RoomItem : MonoBehaviour
{
    // 房间项展示字段。
    [SerializeField] private TMP_Text roomNameText;
    [SerializeField] private TMP_Text roomInfoText;
    [SerializeField] private Button joinRoomBtn;
    // 房间 Id，点击加入时使用。
    [SerializeField] private string roomId;

    public void Init(string roomName, string id, int playerCount, int maxPlayerCount, string roomState)
    {
        roomNameText.text = roomName;
        roomInfoText.text = $"ID:{id} | {playerCount}/{maxPlayerCount} | {roomState}";
        this.roomId = id;

        joinRoomBtn.onClick.AddListener(OnJoinRoomBtn);

        // 每个列表项自己监听一次加入结果，用于弹提示。
        GlobalTypeNetEvent.Register<S2C_JoinRoomResult>(OnS2C_JoinRoomResult)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
    }

    private void OnS2C_JoinRoomResult(S2C_JoinRoomResult msg)
    {
        if (!msg.Success)
        {
            NetLogger.LogInfo("Panel_StellarNetLobby_RoomItem", $"加入房间失败:{msg.Reason}");
            GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = $"加入房间失败: {msg.Reason}" });
        }
    }

    private void OnJoinRoomBtn()
    {
        // 点击后向服务端发起加入房间请求。
        var msg = new C2S_JoinRoom
        {
            RoomId = roomId
        };
        NetClient.Send(msg);
    }
}
