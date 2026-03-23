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
        private IUnRegister _gameEndedToken;

        protected override void OnBind(ClientRoomSettingsComponent component)
        {
            // 进入在线房间，首先拉起准备大厅 UI
            UIKit.OpenPanel<Panel_StellarNetRoom>();

            // 注册对局结束事件，用于弹出结算面板
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

            // 离开房间时，强制清理残留的通用业务 UI
            UIKit.ClosePanel<Panel_StellarNetRoom>();
            UIKit.ClosePanel<Panel_StellarNetGameOver>();
        }
    }
}