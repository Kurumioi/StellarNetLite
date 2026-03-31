using System.Collections.Generic;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Game.Client.Infrastructure;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;

namespace StellarNet.Lite.Client.Components
{
    // 客户端房间基础信息组件。
    [RoomComponent(1, "RoomSettings", "基础房间设置")]
    public sealed class ClientRoomSettingsComponent : ClientRoomComponent
    {
        private readonly ClientApp _app;

        // 本地房间成员缓存，供 UI 和业务查询。
        public readonly Dictionary<string, MemberInfo> Members = new Dictionary<string, MemberInfo>();
        // 当前房间是否已进入游戏中。
        public bool IsGameStarted { get; private set; }
        // 房间展示名。
        public string RoomName { get; private set; } = string.Empty;
        // 房间最大人数。
        public int MaxMembers { get; private set; }
        // 是否为私有房间。
        public bool IsPrivate { get; private set; }

        // 当前组件对应的表现层根节点。
        private GameObject _viewRoot;
        // 在线/回放态共用的房间设置路由。
        private ClientRoomUIRouterBase<ClientRoomSettingsComponent> _activeRouter;

        public ClientRoomSettingsComponent(ClientApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            if (Room == null)
            {
                NetLogger.LogError("ClientRoomSettingsComponent", "初始化失败: Room 为空");
                return;
            }

            Members.Clear();
            IsGameStarted = false;
            RoomName = string.Empty;
            MaxMembers = 0;
            IsPrivate = false;

            if (_viewRoot != null)
            {
                NetLogger.LogWarning("ClientRoomSettingsComponent", $"重复初始化已忽略: RoomId:{Room.RoomId}");
                return;
            }

            if (_app == null)
            {
                NetLogger.LogError("ClientRoomSettingsComponent", $"初始化失败: _app 为空, RoomId:{Room.RoomId}");
                return;
            }

            // 先确认 App 可用，再创建常驻视图节点，避免提前 return 留下残留对象。
            _viewRoot = new GameObject($"[View] RoomSettings_{Room.RoomId}");
            Object.DontDestroyOnLoad(_viewRoot);

            if (_app.State == ClientAppState.OnlineRoom)
            {
                var router = _viewRoot.AddComponent<ClientRoomSettingsOnlineUIRouter>();
                router.Bind(this);
                _activeRouter = router;
            }
            else if (_app.State == ClientAppState.ReplayRoom)
            {
                var router = _viewRoot.AddComponent<ClientRoomSettingsReplayUIRouter>();
                router.Bind(this);
                _activeRouter = router;
            }
        }

        public override void OnDestroy()
        {
            // 先解绑路由，再销毁承载节点。
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

            Members.Clear();
        }

        [NetHandler]
        public void OnS2C_RoomSnapshot(S2C_RoomSnapshot msg)
        {
            if (msg == null || msg.Members == null)
            {
                NetLogger.LogError("ClientRoomSettingsComponent", $"处理房间快照失败: msg 或 members 为空, RoomId:{Room?.RoomId ?? "-"}");
                return;
            }

            RoomName = msg.RoomName ?? string.Empty;
            MaxMembers = msg.MaxMembers;
            IsPrivate = msg.IsPrivate;

            // 快照以服务端全量数据为准，先清空再重建本地缓存。
            Members.Clear();
            for (int i = 0; i < msg.Members.Length; i++)
            {
                MemberInfo member = msg.Members[i];
                if (member == null || string.IsNullOrEmpty(member.SessionId))
                {
                    continue;
                }

                Members[member.SessionId] = member;
            }

            if (Room == null)
            {
                NetLogger.LogError("ClientRoomSettingsComponent", "处理房间快照失败: Room 为空");
                return;
            }

            NetLogger.LogInfo("ClientRoomSettingsComponent", $"收到房间快照, RoomId:{Room.RoomId}, RoomName:{RoomName}, Members:{Members.Count}/{MaxMembers}");
            Room.NetEventSystem.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_MemberJoined(S2C_MemberJoined msg)
        {
            if (msg == null || msg.Member == null || string.IsNullOrEmpty(msg.Member.SessionId))
            {
                NetLogger.LogError("ClientRoomSettingsComponent", $"处理成员加入失败: 参数非法, RoomId:{Room?.RoomId ?? "-"}");
                return;
            }

            // 单人增量加入时只刷新该成员。
            Members[msg.Member.SessionId] = msg.Member;

            if (Room == null)
            {
                NetLogger.LogError("ClientRoomSettingsComponent", "处理成员加入失败: Room 为空");
                return;
            }

            Room.NetEventSystem.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_MemberLeft(S2C_MemberLeft msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.SessionId))
            {
                NetLogger.LogError("ClientRoomSettingsComponent", $"处理成员离开失败: 参数非法, RoomId:{Room?.RoomId ?? "-"}");
                return;
            }

            // 单人离开时移除本地缓存。
            Members.Remove(msg.SessionId);

            if (Room == null)
            {
                NetLogger.LogError("ClientRoomSettingsComponent", "处理成员离开失败: Room 为空");
                return;
            }

            Room.NetEventSystem.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_MemberReadyChanged(S2C_MemberReadyChanged msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.SessionId))
            {
                NetLogger.LogError("ClientRoomSettingsComponent", $"处理准备状态变更失败: 参数非法, RoomId:{Room?.RoomId ?? "-"}");
                return;
            }

            if (Members.TryGetValue(msg.SessionId, out MemberInfo member))
            {
                // MemberInfo 是引用类型，直接改字段即可同步到缓存。
                member.IsReady = msg.IsReady;
            }

            if (Room == null)
            {
                NetLogger.LogError("ClientRoomSettingsComponent", "处理准备状态变更失败: Room 为空");
                return;
            }

            Room.NetEventSystem.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_GameStarted(S2C_GameStarted msg)
        {
            if (msg == null)
            {
                NetLogger.LogError("ClientRoomSettingsComponent", $"处理游戏开始失败: msg 为空, RoomId:{Room?.RoomId ?? "-"}");
                return;
            }

            // 游戏开始后切换本地房间状态。
            IsGameStarted = true;

            if (Room == null)
            {
                NetLogger.LogError("ClientRoomSettingsComponent", "处理游戏开始失败: Room 为空");
                return;
            }

            Room.NetEventSystem.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_GameEnded(S2C_GameEnded msg)
        {
            if (msg == null)
            {
                NetLogger.LogError("ClientRoomSettingsComponent", $"处理游戏结束失败: msg 为空, RoomId:{Room?.RoomId ?? "-"}");
                return;
            }

            // 游戏结束后回到未开始态，并重置所有准备标记。
            IsGameStarted = false;

            foreach (var kvp in Members)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.IsReady = false;
                }
            }

            if (Room == null)
            {
                NetLogger.LogError("ClientRoomSettingsComponent", "处理游戏结束失败: Room 为空");
                return;
            }

            Room.NetEventSystem.Broadcast(msg);
        }
    }
}
