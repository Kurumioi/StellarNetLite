using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Game.Shared.Protocol;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Game.Client.Infrastructure;
using StellarNet.Lite.Client.Components.Views;
using StellarNet.Lite.Game.Client.Views;

namespace StellarNet.Lite.Game.Client.Components
{
    [RoomComponent(102, "SocialRoom", "简易交友房间")]
    public sealed class ClientSocialRoomComponent : ClientRoomComponent
    {
        private readonly ClientApp _app;

        private GameObject _viewRoot;

        // 缓存路由引用
        private RoomUIRouterBase<ClientSocialRoomComponent> _activeRouter;

        public ClientSocialRoomComponent(ClientApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            NetLogger.LogInfo("[ClientSocialRoom]", "客户端交友组件初始化完毕，开始装配表现层");

            _viewRoot = new GameObject($"[View] SocialRoom_{Room.RoomId}");
            Object.DontDestroyOnLoad(_viewRoot);

            var spawner = _viewRoot.AddComponent<ObjectSpawnerView>();
            spawner.Init(Room);

            if (_app.State == ClientAppState.OnlineRoom)
            {
                var router = _viewRoot.AddComponent<SocialOnlineUIRouter>();
                router.Bind(this);
                _activeRouter = router;

                var input = _viewRoot.AddComponent<SocialRoomInputController>();
                input.Init(Room);
            }
            else if (_app.State == ClientAppState.ReplayRoom)
            {
                var router = _viewRoot.AddComponent<SocialReplayUIRouter>();
                router.Bind(this);
                _activeRouter = router;
            }
        }

        public override void OnDestroy()
        {
            // 核心修复：主动解绑路由，立即关闭 UI
            if (_activeRouter != null)
            {
                _activeRouter.Unbind();
                _activeRouter = null;
            }

            if (_viewRoot != null)
            {
                Object.Destroy(_viewRoot);
                _viewRoot = null;
            }
        }

        [NetHandler]
        public void OnS2C_SocialBubbleSync(S2C_SocialBubbleSync msg)
        {
            if (msg == null) return;
            Room.NetEventSystem.Broadcast(msg);
        }
    }
}