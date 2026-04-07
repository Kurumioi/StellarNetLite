using StellarNet.UI;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Game.Client.Components;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Game.Client.Infrastructure
{
    public class SocialOnlineUIRouter : ClientRoomUIRouterBase<ClientSocialRoomComponent>
    {
        private IUnRegister _gameStartedToken;
        private IUnRegister _gameEndedToken;

        protected override void OnBind(ClientSocialRoomComponent component)
        {
            _gameStartedToken = BoundRoom.NetEventSystem.Register<S2C_GameStarted>(OnGameStarted);
            _gameEndedToken = BoundRoom.NetEventSystem.Register<S2C_GameEnded>(OnGameEnded);

            // 处理断线重连：如果绑定时游戏已经开始，直接打开战斗面板
            var settings = BoundRoom.GetComponent<ClientRoomSettingsComponent>();
            if (settings != null && settings.IsGameStarted)
            {
                UIKit.OpenPanel<Panel_SocialRoomView>();
            }
        }

        private void OnGameStarted(S2C_GameStarted msg)
        {
            // 状态流转：开始游戏时关闭准备大厅，打开战斗面板
            UIKit.ClosePanel<Panel_StellarNetRoom>();
            UIKit.OpenPanel<Panel_SocialRoomView>();
        }

        private void OnGameEnded(S2C_GameEnded msg)
        {
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