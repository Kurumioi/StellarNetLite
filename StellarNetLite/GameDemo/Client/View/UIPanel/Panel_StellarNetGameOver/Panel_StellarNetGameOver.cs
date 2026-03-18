using Cysharp.Threading.Tasks;
using StellarFramework.UI;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 核心修复 P1-6：独立的“对局结算态” UI 抽象。
/// 职责：展示对局结果，并提供退回等待态（房间）或直接离房的出口。
/// </summary>
public class Panel_StellarNetGameOver : UIPanelBase
{
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private Button returnRoomBtn;
    [SerializeField] private Button leaveRoomBtn;

    public override void OnInit()
    {
        base.OnInit();
        returnRoomBtn.onClick.AddListener(OnReturnRoomBtn);
        leaveRoomBtn.onClick.AddListener(OnLeaveRoomBtn);
    }

    private void OnDestroy()
    {
        returnRoomBtn.onClick.RemoveAllListeners();
        leaveRoomBtn.onClick.RemoveAllListeners();
    }

    public override async UniTask OnOpen(object uiData = null)
    {
        await base.OnOpen(uiData);

        if (uiData is S2C_GameEnded msg)
        {
            resultText.text = $"对局结束\n<size=30>结算信息: {msg.WinnerSessionId}</size>";
        }
        else
        {
            resultText.text = "对局结束";
        }
    }

    private void OnReturnRoomBtn()
    {
        // 状态机跃迁：从“结算态”退回“房间等待态”
        UIKit.ClosePanel<Panel_StellarNetGameOver>();
        UIKit.OpenPanel<Panel_StellarNetRoom>();
    }

    private void OnLeaveRoomBtn()
    {
        // 直接请求离房，成功后 Router 会监听 Local_RoomLeft 将我们带回大厅
        NetClient.Send(new C2S_LeaveRoom());
    }
}