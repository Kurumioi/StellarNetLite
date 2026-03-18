using Cysharp.Threading.Tasks;
using StellarFramework.UI;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 核心修复：对局结算态 UI 抽象。
/// 业务调整：遵循“一局一会”原则，结算确认后直接请求离开房间，由 Router 统一接管并路由回大厅。
/// </summary>
public class Panel_StellarNetGameOver : UIPanelBase
{
    [SerializeField] private TMP_Text resultText;

    // 隐藏或移除原有的 returnRoomBtn，统一使用一个确认离开按钮
    [SerializeField] private Button leaveRoomBtn;

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

        if (uiData is S2C_GameEnded msg)
        {
            resultText.text = $"对局结束\n<size=30>结算信息: {msg.WinnerSessionId}</size>";
        }
        else
        {
            resultText.text = "对局结束";
        }
    }

    private void OnLeaveRoomBtn()
    {
        // 核心修复：点击确认/离开时，直接向服务端请求离房。
        // 架构说明：这里不需要手动关闭 Panel 或打开大厅 UI。
        // 服务端处理完毕下发 S2C_LeaveRoomResult 后，ClientRoomModule 会调用 App.LeaveRoom()，
        // 进而抛出 Local_RoomLeft 事件，ClientUIRouter 监听到该事件后，会自动执行全局 UI 清理并打开大厅。
        NetClient.Send(new C2S_LeaveRoom());

        // 为了防止玩家重复点击，可以先将按钮置灰
        leaveRoomBtn.interactable = false;
    }
}