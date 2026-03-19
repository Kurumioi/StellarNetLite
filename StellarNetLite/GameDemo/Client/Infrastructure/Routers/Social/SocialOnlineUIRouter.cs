using StellarFramework.UI;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Game.Client.Components;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Game.Client.Infrastructure
{
    /// <summary>
    /// 交友房间业务组件 - 在线态 UI 路由
    /// </summary>
    public class SocialOnlineUIRouter : RoomUIRouterBase<ClientSocialRoomComponent>
    {
        private IUnRegister _gameStartedToken;
        private IUnRegister _gameEndedToken;

        protected override void OnBind(ClientSocialRoomComponent component)
        {
            _gameStartedToken = BoundRoom.NetEventSystem.Register<S2C_GameStarted>(OnGameStarted);
            _gameEndedToken = BoundRoom.NetEventSystem.Register<S2C_GameEnded>(OnGameEnded);
        }

        private void OnGameStarted(S2C_GameStarted msg)
        {
            // 游戏正式开始，拉起交友房间专属表现层 UI
            UIKit.OpenPanel<Panel_SocialRoomView>();
        }

        private void OnGameEnded(S2C_GameEnded msg)
        {
            // 结算时必须关闭对局内 UI
            UIKit.ClosePanel<Panel_SocialRoomView>();
        }

        protected override void OnUnbind()
        {
            _gameStartedToken?.UnRegister();
            _gameStartedToken = null;

            _gameEndedToken?.UnRegister();
            _gameEndedToken = null;

            UIKit.ClosePanel<Panel_SocialRoomView>();
        }
    }
}