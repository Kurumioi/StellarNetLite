using StellarNet.Lite.Client.Components;
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
        private ClientRoomUIRouterBase<ClientSocialRoomComponent> _activeRouter;
        private ObjectSpawnerView _spawnerView;
        private SocialRoomInputController _inputController;
        private bool _isInitialized;

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

            if (_isInitialized)
            {
                NetLogger.LogWarning("ClientSocialRoomComponent", $"重复初始化已忽略: RoomId:{Room.RoomId}");
                return;
            }

            if (_viewRoot != null)
            {
                NetLogger.LogError("ClientSocialRoomComponent", $"初始化失败: _viewRoot 残留未清理, RoomId:{Room.RoomId}, ViewRoot:{_viewRoot.name}");
                return;
            }

            if (_app == null)
            {
                NetLogger.LogError("ClientSocialRoomComponent", $"初始化失败: _app 为空, RoomId:{Room.RoomId}");
                return;
            }

            ClientObjectSyncComponent objectSyncComponent = Room.GetComponent<ClientObjectSyncComponent>();
            if (objectSyncComponent == null)
            {
                NetLogger.LogError("ClientSocialRoomComponent", $"初始化失败: SocialRoom 缺失 ClientObjectSyncComponent, RoomId:{Room.RoomId}, State:{_app.State}");
                return;
            }

            _viewRoot = new GameObject($"[View] SocialRoom_{Room.RoomId}");
            Object.DontDestroyOnLoad(_viewRoot);

            _spawnerView = _viewRoot.AddComponent<ObjectSpawnerView>();
            bool spawnerInitSuccess = _spawnerView.Init(Room);
            if (!spawnerInitSuccess)
            {
                NetLogger.LogError("ClientSocialRoomComponent", $"初始化失败: ObjectSpawnerView 初始化失败, RoomId:{Room.RoomId}, ViewRoot:{_viewRoot.name}, State:{_app.State}");
                SafeDestroyViewRoot();
                return;
            }

            if (_app.State == ClientAppState.OnlineRoom)
            {
                SocialOnlineUIRouter router = _viewRoot.AddComponent<SocialOnlineUIRouter>();
                router.Bind(this);
                _activeRouter = router;

                _inputController = _viewRoot.AddComponent<SocialRoomInputController>();
                _inputController.Init(Room);
            }
            else if (_app.State == ClientAppState.ReplayRoom)
            {
                SocialReplayUIRouter router = _viewRoot.AddComponent<SocialReplayUIRouter>();
                router.Bind(this);
                _activeRouter = router;
            }
            else
            {
                NetLogger.LogWarning("ClientSocialRoomComponent", $"初始化警告: 当前状态未挂接专属路由, RoomId:{Room.RoomId}, State:{_app.State}");
            }

            _isInitialized = true;
            NetLogger.LogInfo("ClientSocialRoomComponent", $"初始化完成: RoomId:{Room.RoomId}, State:{_app.State}");
        }

        public override void OnDestroy()
        {
            _isInitialized = false;

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

            SafeDestroyViewRoot();
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

        /// <summary>
        /// 安全销毁视图根节点。
        /// 我把它独立出来，是为了保证初始化中途失败和正常销毁都走同一条清理路径，避免残留 DontDestroyOnLoad 节点污染后续房间生命周期。
        /// </summary>
        private void SafeDestroyViewRoot()
        {
            if (_viewRoot == null)
            {
                return;
            }

            Object.Destroy(_viewRoot);
            _viewRoot = null;
        }
    }
}