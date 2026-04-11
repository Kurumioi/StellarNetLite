using StellarNet.UI;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Game.Client.Infrastructure
{
    /// <summary>
    /// 在线房间的房间设置 UI 路由。
    /// </summary>
    public class ClientRoomSettingsOnlineUIRouter : ClientRoomUIRouterBase<ClientRoomSettingsComponent>
    {
        private IUnRegister _gameEndedToken;

        protected override void OnBind(ClientRoomSettingsComponent component)
        {
            // 如果游戏还没开始（正常进房），打开准备面板
            if (!component.IsGameStarted)
            {
                UIKit.OpenPanel<Panel_StellarNetRoom>();
            }

            _gameEndedToken = BoundRoom.NetEventSystem.Register<S2C_GameEnded>(OnGameEnded);
        }

        private void OnGameEnded(S2C_GameEnded msg)
        {
            UIKit.OpenPanel<Panel_StellarNetGameOver>(msg);
        }

        protected override void OnUnbind()
        {
            _gameEndedToken?.UnRegister();
            _gameEndedToken = null;
            UIKit.ClosePanel<Panel_StellarNetRoom>();
            UIKit.ClosePanel<Panel_StellarNetGameOver>();
        }
    }
}
