using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Game.Shared.Protocol;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Game.Client.Components
{
    /// <summary>
    /// 客户端交友房间状态组件 (Service层)
    /// 职责：接收服务端下发的社交专属协议（如聊天气泡），并通过房间事件总线直抛给表现层。
    /// 架构说明：实体的物理同步由 ObjectSync 负责，本组件专注于纯业务状态的流转。
    /// </summary>
    [RoomComponent(102, "SocialRoom", "简易交友房间")]
    public sealed class ClientSocialRoomComponent : ClientRoomComponent
    {
        private readonly ClientApp _app;

        public ClientSocialRoomComponent(ClientApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            NetLogger.LogInfo("[ClientSocialRoom]", "客户端交友组件初始化完毕，开始监听社交事件");
        }

        [NetHandler]
        public void OnS2C_SocialBubbleSync(S2C_SocialBubbleSync msg)
        {
            if (msg == null) return;

            // 0GC 直抛给表现层，由 View 层负责在屏幕上渲染气泡
            Room.NetEventSystem.Broadcast(msg);
        }
    }
}