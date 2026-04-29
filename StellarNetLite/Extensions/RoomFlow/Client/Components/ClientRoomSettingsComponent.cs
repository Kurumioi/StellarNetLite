using System.Collections.Generic;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Client.Components
{
    /// <summary>
    /// 客户端房间基础信息组件。
    /// 负责缓存成员列表、房间名和开局状态。
    /// </summary>
    [RoomComponent(1, "RoomSettings", "基础房间设置")]
    public sealed class ClientRoomSettingsComponent : ClientRoomComponent
    {
        /// <summary>
        /// 所属客户端逻辑宿主。
        /// </summary>
        private readonly ClientApp _app;

        /// <summary>
        /// 房间成员快照。
        /// key: SessionId。
        /// value: 对应成员信息。
        /// </summary>
        public readonly Dictionary<string, MemberInfo> Members = new Dictionary<string, MemberInfo>();

        /// <summary>
        /// 当前房间是否已经开局。
        /// </summary>
        public bool IsGameStarted { get; private set; }

        /// <summary>
        /// 当前房间名。
        /// </summary>
        public string RoomName { get; private set; } = string.Empty;

        /// <summary>
        /// 当前房间最大人数。
        /// </summary>
        public int MaxMembers { get; private set; }

        /// <summary>
        /// 当前房间是否为私有房。
        /// </summary>
        public bool IsPrivate { get; private set; }

        /// <summary>
        /// 创建客户端房间设置组件。
        /// </summary>
        public ClientRoomSettingsComponent(ClientApp app)
        {
            _app = app;
        }

        /// <summary>
        /// 初始化房间基础状态缓存。
        /// </summary>
        public override void OnInit()
        {
            if (Room == null) return;
            Members.Clear();
            IsGameStarted = false;
            RoomName = string.Empty;
            MaxMembers = 0;
            IsPrivate = false;
        }

        /// <summary>
        /// 销毁组件时清理成员缓存。
        /// </summary>
        public override void OnDestroy()
        {
            Members.Clear();
        }

        /// <summary>
        /// 处理完整房间快照。
        /// </summary>
        [NetHandler]
        public void OnS2C_RoomSnapshot(S2C_RoomSnapshot msg)
        {
            if (msg == null || msg.Members == null || Room == null) return;
            RoomName = msg.RoomName ?? string.Empty;
            MaxMembers = msg.MaxMembers;
            IsPrivate = msg.IsPrivate;
            Members.Clear();
            for (int i = 0; i < msg.Members.Length; i++)
            {
                var member = msg.Members[i];
                if (member != null && !string.IsNullOrEmpty(member.SessionId))
                {
                    Members[member.SessionId] = member;
                }
            }

            Room.NetEventSystem.Broadcast(msg);
        }

        /// <summary>
        /// 处理成员加入事件。
        /// </summary>
        [NetHandler]
        public void OnS2C_MemberJoined(S2C_MemberJoined msg)
        {
            if (msg?.Member == null || string.IsNullOrEmpty(msg.Member.SessionId) || Room == null) return;
            Members[msg.Member.SessionId] = msg.Member;
            Room.NetEventSystem.Broadcast(msg);
        }

        /// <summary>
        /// 处理成员离开事件。
        /// </summary>
        [NetHandler]
        public void OnS2C_MemberLeft(S2C_MemberLeft msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.SessionId) || Room == null) return;
            Members.Remove(msg.SessionId);
            Room.NetEventSystem.Broadcast(msg);
        }

        /// <summary>
        /// 处理成员准备状态变化事件。
        /// </summary>
        [NetHandler]
        public void OnS2C_MemberReadyChanged(S2C_MemberReadyChanged msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.SessionId) || Room == null) return;
            if (Members.TryGetValue(msg.SessionId, out MemberInfo member))
            {
                member.IsReady = msg.IsReady;
            }

            Room.NetEventSystem.Broadcast(msg);
        }

        /// <summary>
        /// 处理开局事件。
        /// </summary>
        [NetHandler]
        public void OnS2C_GameStarted(S2C_GameStarted msg)
        {
            if (msg == null || Room == null) return;
            IsGameStarted = true;
            Room.NetEventSystem.Broadcast(msg);
        }

        /// <summary>
        /// 处理对局结束事件，并重置本地准备状态。
        /// </summary>
        [NetHandler]
        public void OnS2C_GameEnded(S2C_GameEnded msg)
        {
            if (msg == null || Room == null) return;
            IsGameStarted = false;
            foreach (var kvp in Members)
            {
                if (kvp.Value != null) kvp.Value.IsReady = false;
            }

            Room.NetEventSystem.Broadcast(msg);
        }
    }
}
