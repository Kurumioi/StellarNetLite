using StellarNet.UI;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 对局结算态 UI 抽象。
/// </summary>
public class Panel_StellarNetGameOver : UIPanelBase
{
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private Button leaveRoomBtn;

    [Header("录像重命名 (仅房主可见)")] [SerializeField]
    private GameObject renameGroup;

    [SerializeField] private TMP_InputField renameIpt;

    private string _currentReplayId;
    private bool _isOwner;

    public override void OnInit()
    {
        base.OnInit();
        leaveRoomBtn.onClick.AddListener(OnLeaveRoomBtn);
    }

    private void OnDestroy()
    {
        leaveRoomBtn?.onClick.RemoveAllListeners();
    }

    public override void OnOpen(object uiData = null)
    {
        base.OnOpen(uiData);

        leaveRoomBtn.interactable = true;
        _currentReplayId = string.Empty;
        _isOwner = false;

        var settingsComp = NetClient.CurrentRoom?.GetComponent<ClientRoomSettingsComponent>();
        if (settingsComp != null && settingsComp.Members.TryGetValue(NetClient.Session.SessionId, out var myInfo))
        {
            _isOwner = myInfo.IsOwner;
        }

        if (uiData is S2C_GameEnded msg)
        {
            resultText.text = $"对局结束\n<size=30>结算信息: {msg.WinnerSessionId}</size>";
            _currentReplayId = msg.ReplayId;
        }
        else
        {
            resultText.text = "对局结束";
        }

        if (_isOwner && !string.IsNullOrEmpty(_currentReplayId))
        {
            if (renameGroup != null) renameGroup.SetActive(true);
            if (renameIpt != null && settingsComp != null)
            {
                renameIpt.text = settingsComp.RoomName;
            }
        }
        else
        {
            if (renameGroup != null) renameGroup.SetActive(false);
        }
    }

    private void OnLeaveRoomBtn()
    {
        if (_isOwner && !string.IsNullOrEmpty(_currentReplayId) && renameIpt != null)
        {
            string newName = renameIpt.text.Trim();
            if (!string.IsNullOrEmpty(newName))
            {
                NetClient.Send(new C2S_RenameReplay
                {
                    ReplayId = _currentReplayId,
                    NewName = newName
                });
            }
        }

        NetClient.Send(new C2S_LeaveRoom());
        leaveRoomBtn.interactable = false;
    }
}