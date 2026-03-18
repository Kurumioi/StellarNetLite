// ========================================================
// 自动生成的协议 ID 常量表。
// ========================================================
namespace StellarNet.Lite.Shared.Protocol
{
    public static class MsgIdConst
    {
        public const int C2S_Login = 100;
        public const int S2C_LoginResult = 101;
        public const int S2C_KickOut = 102;
        public const int C2S_ConfirmReconnect = 103;
        public const int S2C_ReconnectResult = 104;
        public const int C2S_ReconnectReady = 105;
        public const int C2S_CreateRoom = 200;
        public const int S2C_CreateRoomResult = 201;
        public const int C2S_JoinRoom = 202;
        public const int S2C_JoinRoomResult = 203;
        public const int C2S_LeaveRoom = 204;
        public const int S2C_LeaveRoomResult = 205;
        public const int C2S_RoomSetupReady = 206;
        public const int C2S_GetRoomList = 210;
        public const int S2C_RoomListResponse = 211;
        public const int S2C_RoomSnapshot = 300;
        public const int S2C_MemberJoined = 301;
        public const int S2C_MemberLeft = 302;
        public const int C2S_SetReady = 303;
        public const int S2C_MemberReadyChanged = 304;
        public const int C2S_SendLobbyChat = 400;
        public const int S2C_LobbyChatMsg = 401;
        public const int C2S_StartGame = 500;
        public const int S2C_GameStarted = 501;
        public const int C2S_EndGame = 502;
        public const int S2C_GameEnded = 503;
        public const int C2S_GetReplayList = 600;
        public const int S2C_ReplayList = 601;
        public const int C2S_DownloadReplay = 602;
        public const int S2C_DownloadReplayResult = 603;
        public const int S2C_DownloadReplayStart = 604;
        public const int S2C_DownloadReplayChunk = 605;
        public const int C2S_DownloadReplayChunkAck = 606;
        public const int S2C_ObjectSpawn = 1100;
        public const int S2C_ObjectDestroy = 1101;
        public const int S2C_ObjectSync = 1102;
        public const int C2S_SocialMoveReq = 1301;
        public const int C2S_SocialActionReq = 1302;
        public const int C2S_SocialBubbleReq = 1303;
        public const int S2C_SocialBubbleSync = 1304;
    }
}
