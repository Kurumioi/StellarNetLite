// ========================================================
// 自动生成的分片协议元数据注册表。
// 请勿手动修改！由 LiteProtocolScanner 自动生成。
// ========================================================
using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Generated.Protocol.Meta
{
    public static class Generated_Lite_MessageMeta
    {
        public static void AppendTypeToMeta(Dictionary<Type, NetMessageMeta> target)
        {
            if (target == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_MessageMeta", "AppendTypeToMeta failed");
                return;
            }
            target[typeof(StellarNet.Lite.Shared.Protocol.C2S_Login)] = new NetMessageMeta(100, NetScope.Global, NetDir.C2S);
            target[typeof(StellarNet.Lite.Shared.Protocol.S2C_LoginResult)] = new NetMessageMeta(101, NetScope.Global, NetDir.S2C);
            target[typeof(StellarNet.Lite.Shared.Protocol.S2C_KickOut)] = new NetMessageMeta(102, NetScope.Global, NetDir.S2C);
            target[typeof(StellarNet.Lite.Shared.Protocol.C2S_ConfirmReconnect)] = new NetMessageMeta(103, NetScope.Global, NetDir.C2S);
            target[typeof(StellarNet.Lite.Shared.Protocol.S2C_ReconnectResult)] = new NetMessageMeta(104, NetScope.Global, NetDir.S2C);
            target[typeof(StellarNet.Lite.Shared.Protocol.C2S_ReconnectReady)] = new NetMessageMeta(105, NetScope.Global, NetDir.C2S);
            target[typeof(StellarNet.Lite.Shared.Protocol.C2S_CreateRoom)] = new NetMessageMeta(200, NetScope.Global, NetDir.C2S);
            target[typeof(StellarNet.Lite.Shared.Protocol.S2C_CreateRoomResult)] = new NetMessageMeta(201, NetScope.Global, NetDir.S2C);
            target[typeof(StellarNet.Lite.Shared.Protocol.C2S_JoinRoom)] = new NetMessageMeta(202, NetScope.Global, NetDir.C2S);
            target[typeof(StellarNet.Lite.Shared.Protocol.S2C_JoinRoomResult)] = new NetMessageMeta(203, NetScope.Global, NetDir.S2C);
            target[typeof(StellarNet.Lite.Shared.Protocol.C2S_LeaveRoom)] = new NetMessageMeta(204, NetScope.Global, NetDir.C2S);
            target[typeof(StellarNet.Lite.Shared.Protocol.S2C_LeaveRoomResult)] = new NetMessageMeta(205, NetScope.Global, NetDir.S2C);
            target[typeof(StellarNet.Lite.Shared.Protocol.C2S_RoomSetupReady)] = new NetMessageMeta(206, NetScope.Global, NetDir.C2S);
            target[typeof(StellarNet.Lite.Shared.Protocol.C2S_GetRoomList)] = new NetMessageMeta(210, NetScope.Global, NetDir.C2S);
            target[typeof(StellarNet.Lite.Shared.Protocol.S2C_RoomListResponse)] = new NetMessageMeta(211, NetScope.Global, NetDir.S2C);
            target[typeof(StellarNet.Lite.Shared.Protocol.S2C_RoomSnapshot)] = new NetMessageMeta(300, NetScope.Room, NetDir.S2C);
            target[typeof(StellarNet.Lite.Shared.Protocol.S2C_MemberJoined)] = new NetMessageMeta(301, NetScope.Room, NetDir.S2C);
            target[typeof(StellarNet.Lite.Shared.Protocol.S2C_MemberLeft)] = new NetMessageMeta(302, NetScope.Room, NetDir.S2C);
            target[typeof(StellarNet.Lite.Shared.Protocol.C2S_SetReady)] = new NetMessageMeta(303, NetScope.Room, NetDir.C2S);
            target[typeof(StellarNet.Lite.Shared.Protocol.S2C_MemberReadyChanged)] = new NetMessageMeta(304, NetScope.Room, NetDir.S2C);
            target[typeof(StellarNet.Lite.Shared.Protocol.C2S_SendLobbyChat)] = new NetMessageMeta(400, NetScope.Global, NetDir.C2S);
            target[typeof(StellarNet.Lite.Shared.Protocol.S2C_LobbyChatMsg)] = new NetMessageMeta(401, NetScope.Global, NetDir.S2C);
            target[typeof(StellarNet.Lite.Shared.Protocol.C2S_StartGame)] = new NetMessageMeta(500, NetScope.Room, NetDir.C2S);
            target[typeof(StellarNet.Lite.Shared.Protocol.S2C_GameStarted)] = new NetMessageMeta(501, NetScope.Room, NetDir.S2C);
            target[typeof(StellarNet.Lite.Shared.Protocol.C2S_EndGame)] = new NetMessageMeta(502, NetScope.Room, NetDir.C2S);
            target[typeof(StellarNet.Lite.Shared.Protocol.S2C_GameEnded)] = new NetMessageMeta(503, NetScope.Room, NetDir.S2C);
            target[typeof(StellarNet.Lite.Shared.Protocol.C2S_GetReplayList)] = new NetMessageMeta(600, NetScope.Global, NetDir.C2S);
            target[typeof(StellarNet.Lite.Shared.Protocol.S2C_ReplayList)] = new NetMessageMeta(601, NetScope.Global, NetDir.S2C);
            target[typeof(StellarNet.Lite.Shared.Protocol.C2S_DownloadReplay)] = new NetMessageMeta(602, NetScope.Global, NetDir.C2S);
            target[typeof(StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayResult)] = new NetMessageMeta(603, NetScope.Global, NetDir.S2C);
            target[typeof(StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayStart)] = new NetMessageMeta(604, NetScope.Global, NetDir.S2C);
            target[typeof(StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayChunk)] = new NetMessageMeta(605, NetScope.Global, NetDir.S2C);
            target[typeof(StellarNet.Lite.Shared.Protocol.C2S_DownloadReplayChunkAck)] = new NetMessageMeta(606, NetScope.Global, NetDir.C2S);
            target[typeof(StellarNet.Lite.Shared.Protocol.C2S_RenameReplay)] = new NetMessageMeta(607, NetScope.Global, NetDir.C2S);
            target[typeof(StellarNet.Lite.Shared.Protocol.S2C_ObjectSpawn)] = new NetMessageMeta(1100, NetScope.Room, NetDir.S2C);
            target[typeof(StellarNet.Lite.Shared.Protocol.S2C_ObjectDestroy)] = new NetMessageMeta(1101, NetScope.Room, NetDir.S2C);
            target[typeof(StellarNet.Lite.Shared.Protocol.S2C_ObjectSync)] = new NetMessageMeta(1102, NetScope.Room, NetDir.S2C);
        }

        public static void AppendMsgIdToType(Dictionary<int, Type> target)
        {
            if (target == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_MessageMeta", "AppendMsgIdToType failed");
                return;
            }
            target[100] = typeof(StellarNet.Lite.Shared.Protocol.C2S_Login);
            target[101] = typeof(StellarNet.Lite.Shared.Protocol.S2C_LoginResult);
            target[102] = typeof(StellarNet.Lite.Shared.Protocol.S2C_KickOut);
            target[103] = typeof(StellarNet.Lite.Shared.Protocol.C2S_ConfirmReconnect);
            target[104] = typeof(StellarNet.Lite.Shared.Protocol.S2C_ReconnectResult);
            target[105] = typeof(StellarNet.Lite.Shared.Protocol.C2S_ReconnectReady);
            target[200] = typeof(StellarNet.Lite.Shared.Protocol.C2S_CreateRoom);
            target[201] = typeof(StellarNet.Lite.Shared.Protocol.S2C_CreateRoomResult);
            target[202] = typeof(StellarNet.Lite.Shared.Protocol.C2S_JoinRoom);
            target[203] = typeof(StellarNet.Lite.Shared.Protocol.S2C_JoinRoomResult);
            target[204] = typeof(StellarNet.Lite.Shared.Protocol.C2S_LeaveRoom);
            target[205] = typeof(StellarNet.Lite.Shared.Protocol.S2C_LeaveRoomResult);
            target[206] = typeof(StellarNet.Lite.Shared.Protocol.C2S_RoomSetupReady);
            target[210] = typeof(StellarNet.Lite.Shared.Protocol.C2S_GetRoomList);
            target[211] = typeof(StellarNet.Lite.Shared.Protocol.S2C_RoomListResponse);
            target[300] = typeof(StellarNet.Lite.Shared.Protocol.S2C_RoomSnapshot);
            target[301] = typeof(StellarNet.Lite.Shared.Protocol.S2C_MemberJoined);
            target[302] = typeof(StellarNet.Lite.Shared.Protocol.S2C_MemberLeft);
            target[303] = typeof(StellarNet.Lite.Shared.Protocol.C2S_SetReady);
            target[304] = typeof(StellarNet.Lite.Shared.Protocol.S2C_MemberReadyChanged);
            target[400] = typeof(StellarNet.Lite.Shared.Protocol.C2S_SendLobbyChat);
            target[401] = typeof(StellarNet.Lite.Shared.Protocol.S2C_LobbyChatMsg);
            target[500] = typeof(StellarNet.Lite.Shared.Protocol.C2S_StartGame);
            target[501] = typeof(StellarNet.Lite.Shared.Protocol.S2C_GameStarted);
            target[502] = typeof(StellarNet.Lite.Shared.Protocol.C2S_EndGame);
            target[503] = typeof(StellarNet.Lite.Shared.Protocol.S2C_GameEnded);
            target[600] = typeof(StellarNet.Lite.Shared.Protocol.C2S_GetReplayList);
            target[601] = typeof(StellarNet.Lite.Shared.Protocol.S2C_ReplayList);
            target[602] = typeof(StellarNet.Lite.Shared.Protocol.C2S_DownloadReplay);
            target[603] = typeof(StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayResult);
            target[604] = typeof(StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayStart);
            target[605] = typeof(StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayChunk);
            target[606] = typeof(StellarNet.Lite.Shared.Protocol.C2S_DownloadReplayChunkAck);
            target[607] = typeof(StellarNet.Lite.Shared.Protocol.C2S_RenameReplay);
            target[1100] = typeof(StellarNet.Lite.Shared.Protocol.S2C_ObjectSpawn);
            target[1101] = typeof(StellarNet.Lite.Shared.Protocol.S2C_ObjectDestroy);
            target[1102] = typeof(StellarNet.Lite.Shared.Protocol.S2C_ObjectSync);
        }
    }
}
