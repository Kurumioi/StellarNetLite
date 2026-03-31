using StellarNet.UI;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Game.Client.Infrastructure
{
    /// <summary>
    /// 通用房间设置组件 - 在线态 UI 路由
    /// </summary>
    public class ClientRoomSettingsOnlineUIRouter : ClientRoomUIRouterBase<ClientRoomSettingsComponent>
    {
        // 结算事件订阅令牌。
        private IUnRegister _gameEndedToken;

        protected override void OnBind(ClientRoomSettingsComponent component)
        {
            // 进入在线房间时，先拉起准备大厅 UI。
            UIKit.OpenPanel<Panel_StellarNetRoom>();

            // 在线态监听结算事件，用于弹出结算面板。
            _gameEndedToken = BoundRoom.NetEventSystem.Register<S2C_GameEnded>(OnGameEnded);
        }

        private void OnGameEnded(S2C_GameEnded msg)
        {
            UIKit.OpenPanel<Panel_StellarNetGameOver>(msg);
        }

        protected override void OnUnbind()
        {
            // 离开房间时统一清理通用房间 UI。
            _gameEndedToken?.UnRegister();
            _gameEndedToken = null;

            UIKit.ClosePanel<Panel_StellarNetRoom>();
            UIKit.ClosePanel<Panel_StellarNetGameOver>();
        }
    }
}
