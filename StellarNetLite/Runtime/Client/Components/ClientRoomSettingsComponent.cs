using System.Collections.Generic;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Game.Client.Infrastructure;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;

namespace StellarNet.Lite.Client.Components
{
    [RoomComponent(1, "RoomSettings", "基础房间设置")]
    public sealed class ClientRoomSettingsComponent : ClientRoomComponent
    {
        private readonly ClientApp _app;

        public readonly Dictionary<string, MemberInfo> Members = new Dictionary<string, MemberInfo>();
        public bool IsGameStarted { get; private set; }
        public string RoomName { get; private set; } = string.Empty;
        public int MaxMembers { get; private set; }
        public bool IsPrivate { get; private set; }

        private GameObject _viewRoot;
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

            _viewRoot = new GameObject($"[View] RoomSettings_{Room.RoomId}");
            Object.DontDestroyOnLoad(_viewRoot);

            if (_app == null)
            {
                NetLogger.LogError("ClientRoomSettingsComponent", $"初始化失败: _app 为空, RoomId:{Room.RoomId}");
                return;
            }

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