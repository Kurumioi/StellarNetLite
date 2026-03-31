using StellarNet.UI;
using StellarNet.Lite.Game.Client.Components;

namespace StellarNet.Lite.Game.Client.Infrastructure
{
    /// <summary>
    /// 交友房间业务组件 - 回放态 UI 路由
    /// </summary>
    public class SocialReplayUIRouter : ClientRoomUIRouterBase<ClientSocialRoomComponent>
    {
        protected override void OnBind(ClientSocialRoomComponent component)
        {
            // 当前示例中，回放态不拉起社交输入 UI。
            // UIKit.OpenPanel<Panel_SocialRoomView>();
        }

        protected override void OnUnbind()
        {
            // 若后续回放态也有 UI，可在这里统一清理。
            // UIKit.ClosePanel<Panel_SocialRoomView>();
        }
    }
}
