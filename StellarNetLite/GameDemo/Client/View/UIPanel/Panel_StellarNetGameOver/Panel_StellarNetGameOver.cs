using Cysharp.Threading.Tasks;
using StellarFramework.UI;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 核心修复：对局结算态 UI 抽象。
/// 请在 Unity 编辑器中，为本预制体添加一个 TMP_InputField 并挂载到 renameIpt 字段上。
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
        leaveRoomBtn.onClick.RemoveAllListeners();
    }

    public override async UniTask OnOpen(object uiData = null)
    {
        await base.OnOpen(uiData);
        leaveRoomBtn.interactable = true;
        _currentReplayId = string.Empty;
        _isOwner = false;

        // 判断当前玩家是否为房主
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

        // 仅房主且存在录像 ID 时，显示重命名输入框
        if (_isOwner && !string.IsNullOrEmpty(_currentReplayId))
        {
            if (renameGroup != null) renameGroup.SetActive(true);
            // 默认填入房间名
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
        // 如果是房主，在离开前先发送重命名请求
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

        // 发送离房请求，交由 Router 统一接管跳转
        NetClient.Send(new C2S_LeaveRoom());
        leaveRoomBtn.interactable = false;
    }
}