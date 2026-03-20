// ========================================================
// 自动生成的全局模块绑定分片。
// ========================================================
using System;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Client.Core;

namespace StellarNet.Lite.Shared.Generated.Binders.GlobalModules
{
    public static class Generated_Lite_ModuleBinder
    {
        public static void RegisterServer(ServerApp serverApp, Func<byte[], int, int, Type, object> deserializeFunc)
        {
            if (serverApp == null || deserializeFunc == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "RegisterServer failed");
                return;
            }
            var mod_StellarNet_Lite_Server_Modules_ServerLobbyModule = new StellarNet.Lite.Server.Modules.ServerLobbyModule(serverApp);
            serverApp.GlobalDispatcher.Register(210, (session, packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.C2S_GetRoomList)) is StellarNet.Lite.Shared.Protocol.C2S_GetRoomList msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerLobbyModule.OnC2S_GetRoomList(session, msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
            var mod_StellarNet_Lite_Server_Modules_ServerReplayModule = new StellarNet.Lite.Server.Modules.ServerReplayModule(serverApp);
            serverApp.GlobalDispatcher.Register(600, (session, packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.C2S_GetReplayList)) is StellarNet.Lite.Shared.Protocol.C2S_GetReplayList msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerReplayModule.OnC2S_GetReplayList(session, msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
            serverApp.GlobalDispatcher.Register(607, (session, packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.C2S_RenameReplay)) is StellarNet.Lite.Shared.Protocol.C2S_RenameReplay msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerReplayModule.OnC2S_RenameReplay(session, msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
            serverApp.GlobalDispatcher.Register(602, (session, packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.C2S_DownloadReplay)) is StellarNet.Lite.Shared.Protocol.C2S_DownloadReplay msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerReplayModule.OnC2S_DownloadReplay(session, msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
            serverApp.GlobalDispatcher.Register(606, (session, packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.C2S_DownloadReplayChunkAck)) is StellarNet.Lite.Shared.Protocol.C2S_DownloadReplayChunkAck msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerReplayModule.OnC2S_DownloadReplayChunkAck(session, msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
            var mod_StellarNet_Lite_Server_Modules_ServerRoomModule = new StellarNet.Lite.Server.Modules.ServerRoomModule(serverApp);
            serverApp.GlobalDispatcher.Register(200, (session, packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.C2S_CreateRoom)) is StellarNet.Lite.Shared.Protocol.C2S_CreateRoom msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerRoomModule.OnC2S_CreateRoom(session, msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
            serverApp.GlobalDispatcher.Register(202, (session, packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.C2S_JoinRoom)) is StellarNet.Lite.Shared.Protocol.C2S_JoinRoom msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerRoomModule.OnC2S_JoinRoom(session, msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
            serverApp.GlobalDispatcher.Register(206, (session, packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.C2S_RoomSetupReady)) is StellarNet.Lite.Shared.Protocol.C2S_RoomSetupReady msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerRoomModule.OnC2S_RoomSetupReady(session, msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
            serverApp.GlobalDispatcher.Register(204, (session, packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.C2S_LeaveRoom)) is StellarNet.Lite.Shared.Protocol.C2S_LeaveRoom msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerRoomModule.OnC2S_LeaveRoom(session, msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
            var mod_StellarNet_Lite_Server_Modules_ServerUserModule = new StellarNet.Lite.Server.Modules.ServerUserModule(serverApp);
            serverApp.GlobalDispatcher.Register(100, (session, packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.C2S_Login)) is StellarNet.Lite.Shared.Protocol.C2S_Login msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerUserModule.OnC2S_Login(session, msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
            serverApp.GlobalDispatcher.Register(103, (session, packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.C2S_ConfirmReconnect)) is StellarNet.Lite.Shared.Protocol.C2S_ConfirmReconnect msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerUserModule.OnC2S_ConfirmReconnect(session, msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
            serverApp.GlobalDispatcher.Register(105, (session, packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.C2S_ReconnectReady)) is StellarNet.Lite.Shared.Protocol.C2S_ReconnectReady msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerUserModule.OnC2S_ReconnectReady(session, msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
        }

        public static void RegisterClient(ClientApp clientApp, Func<byte[], int, int, Type, object> deserializeFunc)
        {
            if (clientApp == null || deserializeFunc == null)
            {
                StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "RegisterClient failed");
                return;
            }
            var mod_StellarNet_Lite_Client_Modules_ClientLobbyModule = new StellarNet.Lite.Client.Modules.ClientLobbyModule(clientApp);
            clientApp.GlobalDispatcher.Register(211, (packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.S2C_RoomListResponse)) is StellarNet.Lite.Shared.Protocol.S2C_RoomListResponse msg) {
                    mod_StellarNet_Lite_Client_Modules_ClientLobbyModule.OnS2C_RoomListResponse(msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
            var mod_StellarNet_Lite_Client_Modules_ClientReplayModule = new StellarNet.Lite.Client.Modules.ClientReplayModule(clientApp);
            clientApp.GlobalDispatcher.Register(601, (packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.S2C_ReplayList)) is StellarNet.Lite.Shared.Protocol.S2C_ReplayList msg) {
                    mod_StellarNet_Lite_Client_Modules_ClientReplayModule.OnS2C_ReplayList(msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
            clientApp.GlobalDispatcher.Register(604, (packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayStart)) is StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayStart msg) {
                    mod_StellarNet_Lite_Client_Modules_ClientReplayModule.OnS2C_DownloadReplayStart(msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
            clientApp.GlobalDispatcher.Register(605, (packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayChunk)) is StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayChunk msg) {
                    mod_StellarNet_Lite_Client_Modules_ClientReplayModule.OnS2C_DownloadReplayChunk(msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
            clientApp.GlobalDispatcher.Register(603, (packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayResult)) is StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayResult msg) {
                    mod_StellarNet_Lite_Client_Modules_ClientReplayModule.OnS2C_DownloadReplayResult(msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
            var mod_StellarNet_Lite_Client_Modules_ClientRoomModule = new StellarNet.Lite.Client.Modules.ClientRoomModule(clientApp);
            clientApp.GlobalDispatcher.Register(201, (packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.S2C_CreateRoomResult)) is StellarNet.Lite.Shared.Protocol.S2C_CreateRoomResult msg) {
                    mod_StellarNet_Lite_Client_Modules_ClientRoomModule.OnS2C_CreateRoomResult(msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
            clientApp.GlobalDispatcher.Register(203, (packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.S2C_JoinRoomResult)) is StellarNet.Lite.Shared.Protocol.S2C_JoinRoomResult msg) {
                    mod_StellarNet_Lite_Client_Modules_ClientRoomModule.OnS2C_JoinRoomResult(msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
            clientApp.GlobalDispatcher.Register(205, (packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.S2C_LeaveRoomResult)) is StellarNet.Lite.Shared.Protocol.S2C_LeaveRoomResult msg) {
                    mod_StellarNet_Lite_Client_Modules_ClientRoomModule.OnS2C_LeaveRoomResult(msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
            var mod_StellarNet_Lite_Client_Modules_ClientUserModule = new StellarNet.Lite.Client.Modules.ClientUserModule(clientApp);
            clientApp.GlobalDispatcher.Register(101, (packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.S2C_LoginResult)) is StellarNet.Lite.Shared.Protocol.S2C_LoginResult msg) {
                    mod_StellarNet_Lite_Client_Modules_ClientUserModule.OnS2C_LoginResult(msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
            clientApp.GlobalDispatcher.Register(104, (packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.S2C_ReconnectResult)) is StellarNet.Lite.Shared.Protocol.S2C_ReconnectResult msg) {
                    mod_StellarNet_Lite_Client_Modules_ClientUserModule.OnS2C_ReconnectResult(msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
            clientApp.GlobalDispatcher.Register(102, (packet) => {
                if (deserializeFunc(packet.Payload, packet.PayloadOffset, packet.PayloadLength, typeof(StellarNet.Lite.Shared.Protocol.S2C_KickOut)) is StellarNet.Lite.Shared.Protocol.S2C_KickOut msg) {
                    mod_StellarNet_Lite_Client_Modules_ClientUserModule.OnS2C_KickOut(msg);
                }
                else
                {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("Generated_Lite_ModuleBinder", "Deserialize failed");
                }
            });
        }
    }
}
