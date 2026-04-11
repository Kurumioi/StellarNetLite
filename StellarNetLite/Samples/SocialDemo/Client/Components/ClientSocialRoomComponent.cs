using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Game.Shared.Protocol;
using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Game.Client.Components
{
    [RoomComponent(102, "SocialRoom", "简易交友房间")]
    /// <summary>
    /// Demo 社交房间客户端组件。
    /// </summary>
    public sealed class ClientSocialRoomComponent : ClientRoomComponent
    {
        private readonly ClientApp _app;

        public ClientSocialRoomComponent(ClientApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            if (Room == null) return;
        }

        public override void OnDestroy()
        {
        }

        [NetHandler]
        public void OnS2C_SocialBubbleSync(S2C_SocialBubbleSync msg)
        {
            if (msg == null || Room == null) return;
            Room.NetEventSystem.Broadcast(msg);
        }
    }
}
