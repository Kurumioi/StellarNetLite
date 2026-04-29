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
    /// <summary>
    /// 是否存在尚未收到回包的加入房间请求。
    /// </summary>
    public static bool HasPendingJoinRequest { get; private set; }

    /// <summary>
    /// 房间名称文本。
    /// </summary>
    [SerializeField] private TMP_Text roomNameText;

    /// <summary>
    /// 房间详情文本。
    /// </summary>
    [SerializeField] private TMP_Text roomInfoText;

    /// <summary>
    /// 加入房间按钮。
    /// </summary>
    [SerializeField] private Button joinRoomBtn;

    /// <summary>
    /// 当前条目对应的房间 Id。
    /// </summary>
    [SerializeField] private string roomId;

    /// <summary>
    /// 初始化房间条目展示和按钮事件。
    /// </summary>
    public void Init(string roomName, string id, int playerCount, int maxPlayerCount, string roomState)
    {
        roomNameText.text = roomName;
        roomInfoText.text = $"ID:{id} | {playerCount}/{maxPlayerCount} | {roomState}";
        this.roomId = id;

        joinRoomBtn.onClick.AddListener(OnJoinRoomBtn);
    }

    /// <summary>
    /// 请求加入当前条目对应的房间。
    /// </summary>
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

    /// <summary>
    /// 清理全局待加入标记。
    /// </summary>
    public static void ClearPendingJoinRequest()
    {
        HasPendingJoinRequest = false;
    }
}
