using StellarNet.UI;
using StellarNet.Lite.Client.Components;

namespace StellarNet.Lite.Game.Client.Infrastructure
{
    /// <summary>
    /// 通用房间设置组件 - 回放态 UI 路由
    /// </summary>
    public class ClientRoomSettingsReplayUIRouter : ClientRoomUIRouterBase<ClientRoomSettingsComponent>
    {
        protected override void OnBind(ClientRoomSettingsComponent component)
        {
            // 当前示例中，回放态不需要通用准备大厅 UI。
        }

        protected override void OnUnbind()
        {
            // 若后续回放态增加 UI，可在这里统一清理。
        }
    }
}
