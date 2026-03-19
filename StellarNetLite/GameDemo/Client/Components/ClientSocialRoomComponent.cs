using StellarNet.Lite.Client.Components.Views;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Game.Client.Infrastructure;
using StellarNet.Lite.Game.Client.Views;
using StellarNet.Lite.Game.Shared.Protocol;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

namespace StellarNet.Lite.Game.Client.Components
{
    [RoomComponent(102, "SocialRoom", "简易交友房间")]
    public sealed class ClientSocialRoomComponent : ClientRoomComponent
    {
        private readonly ClientApp _app;
        private GameObject _viewRoot;
        private RoomUIRouterBase<ClientSocialRoomComponent> _activeRouter;
        private ObjectSpawnerView _spawnerView;
        private SocialRoomInputController _inputController;

        public ClientSocialRoomComponent(ClientApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            if (Room == null)
            {
                NetLogger.LogError("ClientSocialRoomComponent", "初始化失败: Room 为空");
                return;
            }

            if (_viewRoot != null)
            {
                NetLogger.LogWarning("ClientSocialRoomComponent", $"重复初始化已忽略: RoomId:{Room.RoomId}");
                return;
            }

            _viewRoot = new GameObject($"[View] SocialRoom_{Room.RoomId}");
            Object.DontDestroyOnLoad(_viewRoot);

            _spawnerView = _viewRoot.AddComponent<ObjectSpawnerView>();
            _spawnerView.Init(Room);

            if (_app == null)
            {
                NetLogger.LogError("ClientSocialRoomComponent", $"初始化失败: _app 为空, RoomId:{Room.RoomId}");
                return;
            }

            if (_app.State == ClientAppState.OnlineRoom)
            {
                var router = _viewRoot.AddComponent<SocialOnlineUIRouter>();
                router.Bind(this);
                _activeRouter = router;

                _inputController = _viewRoot.AddComponent<SocialRoomInputController>();
                _inputController.Init(Room);
            }
            else if (_app.State == ClientAppState.ReplayRoom)
            {
                var router = _viewRoot.AddComponent<SocialReplayUIRouter>();
                router.Bind(this);
                _activeRouter = router;
            }

            NetLogger.LogInfo("ClientSocialRoomComponent", $"初始化完成: RoomId:{Room.RoomId}, State:{_app.State}");
        }

        public override void OnDestroy()
        {
            if (_activeRouter != null)
            {
                _activeRouter.Unbind();
                _activeRouter = null;
            }

            if (_inputController != null)
            {
                _inputController.Clear();
                _inputController = null;
            }

            if (_spawnerView != null)
            {
                _spawnerView.Clear();
                _spawnerView = null;
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
            if (msg == null)
            {
                NetLogger.LogError("ClientSocialRoomComponent", $"处理气泡同步失败: msg 为空, RoomId:{Room?.RoomId ?? "-"}");
                return;
            }

            if (Room == null)
            {
                NetLogger.LogError("ClientSocialRoomComponent", "处理气泡同步失败: Room 为空");
                return;
            }

            Room.NetEventSystem.Broadcast(msg);
        }
    }
}