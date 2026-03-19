// ========================================================
// 自动生成的协议元数据静态注册表。
// 请勿手动修改！由 LiteProtocolScanner 自动生成。
// ========================================================
using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    public static class AutoMessageMetaRegistry
    {
        public static readonly Dictionary<Type, NetMessageMeta> TypeToMeta = new Dictionary<Type, NetMessageMeta>
        {
            { typeof(StellarNet.Lite.Shared.Protocol.C2S_Login), new NetMessageMeta(100, NetScope.Global, NetDir.C2S) },
            { typeof(StellarNet.Lite.Shared.Protocol.S2C_LoginResult), new NetMessageMeta(101, NetScope.Global, NetDir.S2C) },
            { typeof(StellarNet.Lite.Shared.Protocol.S2C_KickOut), new NetMessageMeta(102, NetScope.Global, NetDir.S2C) },
            { typeof(StellarNet.Lite.Shared.Protocol.C2S_ConfirmReconnect), new NetMessageMeta(103, NetScope.Global, NetDir.C2S) },
            { typeof(StellarNet.Lite.Shared.Protocol.S2C_ReconnectResult), new NetMessageMeta(104, NetScope.Global, NetDir.S2C) },
            { typeof(StellarNet.Lite.Shared.Protocol.C2S_ReconnectReady), new NetMessageMeta(105, NetScope.Global, NetDir.C2S) },
            { typeof(StellarNet.Lite.Shared.Protocol.C2S_CreateRoom), new NetMessageMeta(200, NetScope.Global, NetDir.C2S) },
            { typeof(StellarNet.Lite.Shared.Protocol.S2C_CreateRoomResult), new NetMessageMeta(201, NetScope.Global, NetDir.S2C) },
            { typeof(StellarNet.Lite.Shared.Protocol.C2S_JoinRoom), new NetMessageMeta(202, NetScope.Global, NetDir.C2S) },
            { typeof(StellarNet.Lite.Shared.Protocol.S2C_JoinRoomResult), new NetMessageMeta(203, NetScope.Global, NetDir.S2C) },
            { typeof(StellarNet.Lite.Shared.Protocol.C2S_LeaveRoom), new NetMessageMeta(204, NetScope.Global, NetDir.C2S) },
            { typeof(StellarNet.Lite.Shared.Protocol.S2C_LeaveRoomResult), new NetMessageMeta(205, NetScope.Global, NetDir.S2C) },
            { typeof(StellarNet.Lite.Shared.Protocol.C2S_RoomSetupReady), new NetMessageMeta(206, NetScope.Global, NetDir.C2S) },
            { typeof(StellarNet.Lite.Shared.Protocol.C2S_GetRoomList), new NetMessageMeta(210, NetScope.Global, NetDir.C2S) },
            { typeof(StellarNet.Lite.Shared.Protocol.S2C_RoomListResponse), new NetMessageMeta(211, NetScope.Global, NetDir.S2C) },
            { typeof(StellarNet.Lite.Shared.Protocol.S2C_RoomSnapshot), new NetMessageMeta(300, NetScope.Room, NetDir.S2C) },
            { typeof(StellarNet.Lite.Shared.Protocol.S2C_MemberJoined), new NetMessageMeta(301, NetScope.Room, NetDir.S2C) },
            { typeof(StellarNet.Lite.Shared.Protocol.S2C_MemberLeft), new NetMessageMeta(302, NetScope.Room, NetDir.S2C) },
            { typeof(StellarNet.Lite.Shared.Protocol.C2S_SetReady), new NetMessageMeta(303, NetScope.Room, NetDir.C2S) },
            { typeof(StellarNet.Lite.Shared.Protocol.S2C_MemberReadyChanged), new NetMessageMeta(304, NetScope.Room, NetDir.S2C) },
            { typeof(StellarNet.Lite.Shared.Protocol.C2S_SendLobbyChat), new NetMessageMeta(400, NetScope.Global, NetDir.C2S) },
            { typeof(StellarNet.Lite.Shared.Protocol.S2C_LobbyChatMsg), new NetMessageMeta(401, NetScope.Global, NetDir.S2C) },
            { typeof(StellarNet.Lite.Shared.Protocol.C2S_StartGame), new NetMessageMeta(500, NetScope.Room, NetDir.C2S) },
            { typeof(StellarNet.Lite.Shared.Protocol.S2C_GameStarted), new NetMessageMeta(501, NetScope.Room, NetDir.S2C) },
            { typeof(StellarNet.Lite.Shared.Protocol.C2S_EndGame), new NetMessageMeta(502, NetScope.Room, NetDir.C2S) },
            { typeof(StellarNet.Lite.Shared.Protocol.S2C_GameEnded), new NetMessageMeta(503, NetScope.Room, NetDir.S2C) },
            { typeof(StellarNet.Lite.Shared.Protocol.C2S_GetReplayList), new NetMessageMeta(600, NetScope.Global, NetDir.C2S) },
            { typeof(StellarNet.Lite.Shared.Protocol.S2C_ReplayList), new NetMessageMeta(601, NetScope.Global, NetDir.S2C) },
            { typeof(StellarNet.Lite.Shared.Protocol.C2S_DownloadReplay), new NetMessageMeta(602, NetScope.Global, NetDir.C2S) },
            { typeof(StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayResult), new NetMessageMeta(603, NetScope.Global, NetDir.S2C) },
            { typeof(StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayStart), new NetMessageMeta(604, NetScope.Global, NetDir.S2C) },
            { typeof(StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayChunk), new NetMessageMeta(605, NetScope.Global, NetDir.S2C) },
            { typeof(StellarNet.Lite.Shared.Protocol.C2S_DownloadReplayChunkAck), new NetMessageMeta(606, NetScope.Global, NetDir.C2S) },
            { typeof(StellarNet.Lite.Shared.Protocol.C2S_RenameReplay), new NetMessageMeta(607, NetScope.Global, NetDir.C2S) },
            { typeof(StellarNet.Lite.Shared.Protocol.S2C_ObjectSpawn), new NetMessageMeta(1100, NetScope.Room, NetDir.S2C) },
            { typeof(StellarNet.Lite.Shared.Protocol.S2C_ObjectDestroy), new NetMessageMeta(1101, NetScope.Room, NetDir.S2C) },
            { typeof(StellarNet.Lite.Shared.Protocol.S2C_ObjectSync), new NetMessageMeta(1102, NetScope.Room, NetDir.S2C) },
            { typeof(StellarNet.Lite.Game.Shared.Protocol.C2S_SocialMoveReq), new NetMessageMeta(1301, NetScope.Room, NetDir.C2S) },
            { typeof(StellarNet.Lite.Game.Shared.Protocol.C2S_SocialActionReq), new NetMessageMeta(1302, NetScope.Room, NetDir.C2S) },
            { typeof(StellarNet.Lite.Game.Shared.Protocol.C2S_SocialBubbleReq), new NetMessageMeta(1303, NetScope.Room, NetDir.C2S) },
            { typeof(StellarNet.Lite.Game.Shared.Protocol.S2C_SocialBubbleSync), new NetMessageMeta(1304, NetScope.Room, NetDir.S2C) },
        };

        public static readonly Dictionary<int, Type> MsgIdToType = new Dictionary<int, Type>
        {
            { 100, typeof(StellarNet.Lite.Shared.Protocol.C2S_Login) },
            { 101, typeof(StellarNet.Lite.Shared.Protocol.S2C_LoginResult) },
            { 102, typeof(StellarNet.Lite.Shared.Protocol.S2C_KickOut) },
            { 103, typeof(StellarNet.Lite.Shared.Protocol.C2S_ConfirmReconnect) },
            { 104, typeof(StellarNet.Lite.Shared.Protocol.S2C_ReconnectResult) },
            { 105, typeof(StellarNet.Lite.Shared.Protocol.C2S_ReconnectReady) },
            { 200, typeof(StellarNet.Lite.Shared.Protocol.C2S_CreateRoom) },
            { 201, typeof(StellarNet.Lite.Shared.Protocol.S2C_CreateRoomResult) },
            { 202, typeof(StellarNet.Lite.Shared.Protocol.C2S_JoinRoom) },
            { 203, typeof(StellarNet.Lite.Shared.Protocol.S2C_JoinRoomResult) },
            { 204, typeof(StellarNet.Lite.Shared.Protocol.C2S_LeaveRoom) },
            { 205, typeof(StellarNet.Lite.Shared.Protocol.S2C_LeaveRoomResult) },
            { 206, typeof(StellarNet.Lite.Shared.Protocol.C2S_RoomSetupReady) },
            { 210, typeof(StellarNet.Lite.Shared.Protocol.C2S_GetRoomList) },
            { 211, typeof(StellarNet.Lite.Shared.Protocol.S2C_RoomListResponse) },
            { 300, typeof(StellarNet.Lite.Shared.Protocol.S2C_RoomSnapshot) },
            { 301, typeof(StellarNet.Lite.Shared.Protocol.S2C_MemberJoined) },
            { 302, typeof(StellarNet.Lite.Shared.Protocol.S2C_MemberLeft) },
            { 303, typeof(StellarNet.Lite.Shared.Protocol.C2S_SetReady) },
            { 304, typeof(StellarNet.Lite.Shared.Protocol.S2C_MemberReadyChanged) },
            { 400, typeof(StellarNet.Lite.Shared.Protocol.C2S_SendLobbyChat) },
            { 401, typeof(StellarNet.Lite.Shared.Protocol.S2C_LobbyChatMsg) },
            { 500, typeof(StellarNet.Lite.Shared.Protocol.C2S_StartGame) },
            { 501, typeof(StellarNet.Lite.Shared.Protocol.S2C_GameStarted) },
            { 502, typeof(StellarNet.Lite.Shared.Protocol.C2S_EndGame) },
            { 503, typeof(StellarNet.Lite.Shared.Protocol.S2C_GameEnded) },
            { 600, typeof(StellarNet.Lite.Shared.Protocol.C2S_GetReplayList) },
            { 601, typeof(StellarNet.Lite.Shared.Protocol.S2C_ReplayList) },
            { 602, typeof(StellarNet.Lite.Shared.Protocol.C2S_DownloadReplay) },
            { 603, typeof(StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayResult) },
            { 604, typeof(StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayStart) },
            { 605, typeof(StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayChunk) },
            { 606, typeof(StellarNet.Lite.Shared.Protocol.C2S_DownloadReplayChunkAck) },
            { 607, typeof(StellarNet.Lite.Shared.Protocol.C2S_RenameReplay) },
            { 1100, typeof(StellarNet.Lite.Shared.Protocol.S2C_ObjectSpawn) },
            { 1101, typeof(StellarNet.Lite.Shared.Protocol.S2C_ObjectDestroy) },
            { 1102, typeof(StellarNet.Lite.Shared.Protocol.S2C_ObjectSync) },
            { 1301, typeof(StellarNet.Lite.Game.Shared.Protocol.C2S_SocialMoveReq) },
            { 1302, typeof(StellarNet.Lite.Game.Shared.Protocol.C2S_SocialActionReq) },
            { 1303, typeof(StellarNet.Lite.Game.Shared.Protocol.C2S_SocialBubbleReq) },
            { 1304, typeof(StellarNet.Lite.Game.Shared.Protocol.S2C_SocialBubbleSync) },
        };
    }
}
