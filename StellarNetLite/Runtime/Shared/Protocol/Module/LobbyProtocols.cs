using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    public class RoomBriefInfo
    {
        public string RoomId;
        public string RoomName;
        public int MemberCount;
        public int MaxMembers;
        public bool IsPrivate;
        public int State;
    }

    public sealed class OnlinePlayerInfo
    {
        public string SessionId;
        public string AccountId;
        public string DisplayName;
        public bool IsOnline;
        public bool IsInRoom;
        public string RoomId;
    }

    [NetMsg(210, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_GetRoomList
    {
    }

    [NetMsg(211, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_RoomListResponse
    {
        public RoomBriefInfo[] Rooms;
    }

    [NetMsg(212, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_OnlinePlayerListSync
    {
        public OnlinePlayerInfo[] Players;
    }

    [NetMsg(213, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_GlobalPlayerStateIncrementalSync
    {
        public bool IsRemoved;
        public OnlinePlayerInfo Player;
    }

    [NetMsg(214, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_GlobalAnnouncement
    {
        public string Title;
        public string Content;
        public long PublishUnixTime;
    }

    [NetMsg(215, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_GlobalChat
    {
        public string Content;
    }

    [NetMsg(216, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_GlobalChatSync
    {
        public string SenderSessionId;
        public string SenderDisplayName;
        public string Content;
        public long SendUnixTime;
    }
}