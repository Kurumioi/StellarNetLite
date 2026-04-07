using System.Collections.Generic;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;

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

        public ClientRoomSettingsComponent(ClientApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            if (Room == null) return;
            Members.Clear();
            IsGameStarted = false;
            RoomName = string.Empty;
            MaxMembers = 0;
            IsPrivate = false;
        }

        public override void OnDestroy()
        {
            Members.Clear();
        }

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

        [NetHandler]
        public void OnS2C_MemberJoined(S2C_MemberJoined msg)
        {
            if (msg?.Member == null || string.IsNullOrEmpty(msg.Member.SessionId) || Room == null) return;
            Members[msg.Member.SessionId] = msg.Member;
            Room.NetEventSystem.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_MemberLeft(S2C_MemberLeft msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.SessionId) || Room == null) return;
            Members.Remove(msg.SessionId);
            Room.NetEventSystem.Broadcast(msg);
        }

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

        [NetHandler]
        public void OnS2C_GameStarted(S2C_GameStarted msg)
        {
            if (msg == null || Room == null) return;
            IsGameStarted = true;
            Room.NetEventSystem.Broadcast(msg);
        }

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