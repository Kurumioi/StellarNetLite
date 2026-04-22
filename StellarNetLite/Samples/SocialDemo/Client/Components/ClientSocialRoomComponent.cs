using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Game.Shared.Protocol;
using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Game.Client.Components
{
    /// <summary>
    /// Demo 社交房间客户端组件。
    /// </summary>
    [RoomComponent(102, "SocialRoom", "简易交友房间")]
    public sealed class ClientSocialRoomComponent : ClientRoomComponent
    {
        private ClientObjectSyncComponent _syncComponent;

        public ClientSocialRoomComponent(ClientApp app)
        {
        }

        public override void OnInit()
        {
            if (Room == null) return;
            _syncComponent = Room.GetComponent<ClientObjectSyncComponent>();
        }

        public override void OnDestroy()
        {
            _syncComponent = null;
        }

        [NetHandler]
        public void OnS2C_SocialBubbleSync(S2C_SocialBubbleSync msg)
        {
            if (msg == null || Room == null) return;
            Room.NetEventSystem.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_SocialStateSync(S2C_SocialStateSync msg)
        {
            if (msg == null || Room == null || _syncComponent == null)
            {
                return;
            }

            _syncComponent.ApplyLiveState(msg.State, msg.ServerTime);
            Room.NetEventSystem.Broadcast(msg);
        }
    }
}
