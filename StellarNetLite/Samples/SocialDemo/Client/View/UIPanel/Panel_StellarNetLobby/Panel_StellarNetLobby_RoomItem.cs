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
    public static bool HasPendingJoinRequest { get; private set; }

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
    }

    private void OnJoinRoomBtn()
    {
        HasPendingJoinRequest = true;

        // 点击后向服务端发起加入房间请求。
        var msg = new C2S_JoinRoom
        {
            RoomId = roomId
        };
        NetClient.Send(msg);
    }

    public static void ClearPendingJoinRequest()
    {
        HasPendingJoinRequest = false;
    }
}
