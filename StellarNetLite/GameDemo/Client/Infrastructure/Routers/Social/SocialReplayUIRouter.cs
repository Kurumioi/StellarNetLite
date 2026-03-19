using StellarFramework.UI;
using StellarNet.Lite.Game.Client.Components;

namespace StellarNet.Lite.Game.Client.Infrastructure
{
    /// <summary>
    /// 交友房间业务组件 - 回放态 UI 路由
    /// </summary>
    public class SocialReplayUIRouter : RoomUIRouterBase<ClientSocialRoomComponent>
    {
        protected override void OnBind(ClientSocialRoomComponent component)
        {
            // 回放模式下不需要UI
            // UIKit.OpenPanel<Panel_SocialRoomView>();
        }

        protected override void OnUnbind()
        {
            // UIKit.ClosePanel<Panel_SocialRoomView>();
        }
    }
}