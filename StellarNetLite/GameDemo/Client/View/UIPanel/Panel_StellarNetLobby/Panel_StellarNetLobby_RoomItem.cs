using StellarFramework;
using StellarFramework.UI;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Panel_StellarNetLobby_RoomItem : MonoBehaviour
{
    [SerializeField] private TMP_Text roomNameText;
    [SerializeField] private TMP_Text roomInfoText;
    [SerializeField] private Button joinRoomBtn;

    [SerializeField] private string roomId;


    public void Init(string roomName, string id, int playerCount, int maxPlayerCount, string roomState)
    {
        roomNameText.text = roomName;
        roomInfoText.text = $"ID:{id} | {playerCount}/{maxPlayerCount} | {roomState}";
        this.roomId = id;
        joinRoomBtn.onClick.AddListener(OnJoinRoomBtn);

        GlobalTypeNetEvent.Register<S2C_JoinRoomResult>(OnS2C_JoinRoomResult)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
    }

    private void OnS2C_JoinRoomResult(S2C_JoinRoomResult msg)
    {
        if (msg.Success)
        {
            UIKit.ClosePanel<Panel_StellarNetLobby>();
            UIKit.OpenPanel<Panel_StellarNetRoom>();
        }
        else
        {
            LogKit.Log("Panel_StellarNetLobby_RoomItem", $"加入房间失败:{msg.Reason}");
        }
    }

    private void OnJoinRoomBtn()
    {
        var msg = new C2S_JoinRoom
        {
            RoomId = roomId
        };
        GameLauncher.ClientSendMessage(msg);
    }
}