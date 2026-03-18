// ========================================================
// 自动生成的 0 反射静态装配器。
// 请勿手动修改！由 LiteProtocolScanner 自动生成。
// ========================================================
using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Shared.Binders
{
    public static class AutoRegistry
    {
        public static readonly List<RoomComponentMeta> RoomComponentMetaList = new List<RoomComponentMeta>
        {
            new RoomComponentMeta { Id = 1, Name = "RoomSettings", DisplayName = "基础房间设置" },
            new RoomComponentMeta { Id = 102, Name = "SocialRoom", DisplayName = "简易交友房间" },
            new RoomComponentMeta { Id = 200, Name = "ObjectSync", DisplayName = "空间与动画同步核心服务" },
        };

        public static void RegisterServer(ServerApp serverApp, Func<byte[], Type, object> deserializeFunc)
        {
            ServerRoomFactory.Clear();
            var mod_StellarNet_Lite_Server_Modules_ServerLobbyModule = new StellarNet.Lite.Server.Modules.ServerLobbyModule(serverApp);
            serverApp.GlobalDispatcher.Register(210, (session, packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.C2S_GetRoomList)) is StellarNet.Lite.Shared.Protocol.C2S_GetRoomList msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerLobbyModule.OnC2S_GetRoomList(session, msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.C2S_GetRoomList, MsgId: 210");
                }
            });
            var mod_StellarNet_Lite_Server_Modules_ServerUserModule = new StellarNet.Lite.Server.Modules.ServerUserModule(serverApp);
            serverApp.GlobalDispatcher.Register(100, (session, packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.C2S_Login)) is StellarNet.Lite.Shared.Protocol.C2S_Login msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerUserModule.OnC2S_Login(session, msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.C2S_Login, MsgId: 100");
                }
            });
            serverApp.GlobalDispatcher.Register(103, (session, packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.C2S_ConfirmReconnect)) is StellarNet.Lite.Shared.Protocol.C2S_ConfirmReconnect msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerUserModule.OnC2S_ConfirmReconnect(session, msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.C2S_ConfirmReconnect, MsgId: 103");
                }
            });
            serverApp.GlobalDispatcher.Register(105, (session, packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.C2S_ReconnectReady)) is StellarNet.Lite.Shared.Protocol.C2S_ReconnectReady msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerUserModule.OnC2S_ReconnectReady(session, msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.C2S_ReconnectReady, MsgId: 105");
                }
            });
            var mod_StellarNet_Lite_Server_Modules_ServerReplayModule = new StellarNet.Lite.Server.Modules.ServerReplayModule(serverApp);
            serverApp.GlobalDispatcher.Register(600, (session, packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.C2S_GetReplayList)) is StellarNet.Lite.Shared.Protocol.C2S_GetReplayList msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerReplayModule.OnC2S_GetReplayList(session, msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.C2S_GetReplayList, MsgId: 600");
                }
            });
            serverApp.GlobalDispatcher.Register(607, (session, packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.C2S_RenameReplay)) is StellarNet.Lite.Shared.Protocol.C2S_RenameReplay msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerReplayModule.OnC2S_RenameReplay(session, msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.C2S_RenameReplay, MsgId: 607");
                }
            });
            serverApp.GlobalDispatcher.Register(602, (session, packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.C2S_DownloadReplay)) is StellarNet.Lite.Shared.Protocol.C2S_DownloadReplay msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerReplayModule.OnC2S_DownloadReplay(session, msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.C2S_DownloadReplay, MsgId: 602");
                }
            });
            serverApp.GlobalDispatcher.Register(606, (session, packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.C2S_DownloadReplayChunkAck)) is StellarNet.Lite.Shared.Protocol.C2S_DownloadReplayChunkAck msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerReplayModule.OnC2S_DownloadReplayChunkAck(session, msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.C2S_DownloadReplayChunkAck, MsgId: 606");
                }
            });
            var mod_StellarNet_Lite_Server_Modules_ServerRoomModule = new StellarNet.Lite.Server.Modules.ServerRoomModule(serverApp);
            serverApp.GlobalDispatcher.Register(200, (session, packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.C2S_CreateRoom)) is StellarNet.Lite.Shared.Protocol.C2S_CreateRoom msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerRoomModule.OnC2S_CreateRoom(session, msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.C2S_CreateRoom, MsgId: 200");
                }
            });
            serverApp.GlobalDispatcher.Register(202, (session, packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.C2S_JoinRoom)) is StellarNet.Lite.Shared.Protocol.C2S_JoinRoom msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerRoomModule.OnC2S_JoinRoom(session, msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.C2S_JoinRoom, MsgId: 202");
                }
            });
            serverApp.GlobalDispatcher.Register(206, (session, packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.C2S_RoomSetupReady)) is StellarNet.Lite.Shared.Protocol.C2S_RoomSetupReady msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerRoomModule.OnC2S_RoomSetupReady(session, msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.C2S_RoomSetupReady, MsgId: 206");
                }
            });
            serverApp.GlobalDispatcher.Register(204, (session, packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.C2S_LeaveRoom)) is StellarNet.Lite.Shared.Protocol.C2S_LeaveRoom msg) {
                    mod_StellarNet_Lite_Server_Modules_ServerRoomModule.OnC2S_LeaveRoom(session, msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.C2S_LeaveRoom, MsgId: 204");
                }
            });
            ServerRoomFactory.Register(1, () => new StellarNet.Lite.Server.Components.ServerRoomSettingsComponent(serverApp));
            ServerRoomFactory.Register(200, () => new StellarNet.Lite.Server.Components.ServerObjectSyncComponent(serverApp));
            ServerRoomFactory.Register(102, () => new StellarNet.Lite.Game.Server.Components.ServerSocialRoomComponent(serverApp));
        }

        public static void BindServerComponent(StellarNet.Lite.Server.Core.RoomComponent comp, StellarNet.Lite.Server.Core.RoomDispatcher dispatcher, Func<byte[], Type, object> deserializeFunc)
        {
            switch (comp)
            {
                case StellarNet.Lite.Server.Components.ServerRoomSettingsComponent c_1:
                    dispatcher.Register(303, (session, packet) => {
                        if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.C2S_SetReady)) is StellarNet.Lite.Shared.Protocol.C2S_SetReady msg) {
                            c_1.OnC2S_SetReady(session, msg);
                        } else {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.C2S_SetReady, MsgId: 303");
                        }
                    });
                    dispatcher.Register(500, (session, packet) => {
                        if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.C2S_StartGame)) is StellarNet.Lite.Shared.Protocol.C2S_StartGame msg) {
                            c_1.OnC2S_StartGame(session, msg);
                        } else {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.C2S_StartGame, MsgId: 500");
                        }
                    });
                    dispatcher.Register(502, (session, packet) => {
                        if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.C2S_EndGame)) is StellarNet.Lite.Shared.Protocol.C2S_EndGame msg) {
                            c_1.OnC2S_EndGame(session, msg);
                        } else {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.C2S_EndGame, MsgId: 502");
                        }
                    });
                    break;
                case StellarNet.Lite.Game.Server.Components.ServerSocialRoomComponent c_102:
                    dispatcher.Register(1301, (session, packet) => {
                        if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Game.Shared.Protocol.C2S_SocialMoveReq)) is StellarNet.Lite.Game.Shared.Protocol.C2S_SocialMoveReq msg) {
                            c_102.OnC2S_SocialMoveReq(session, msg);
                        } else {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Game.Shared.Protocol.C2S_SocialMoveReq, MsgId: 1301");
                        }
                    });
                    dispatcher.Register(1302, (session, packet) => {
                        if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Game.Shared.Protocol.C2S_SocialActionReq)) is StellarNet.Lite.Game.Shared.Protocol.C2S_SocialActionReq msg) {
                            c_102.OnC2S_SocialActionReq(session, msg);
                        } else {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Game.Shared.Protocol.C2S_SocialActionReq, MsgId: 1302");
                        }
                    });
                    dispatcher.Register(1303, (session, packet) => {
                        if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Game.Shared.Protocol.C2S_SocialBubbleReq)) is StellarNet.Lite.Game.Shared.Protocol.C2S_SocialBubbleReq msg) {
                            c_102.OnC2S_SocialBubbleReq(session, msg);
                        } else {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Game.Shared.Protocol.C2S_SocialBubbleReq, MsgId: 1303");
                        }
                    });
                    break;
            }
        }

        public static void RegisterClient(ClientApp clientApp, Func<byte[], Type, object> deserializeFunc)
        {
            ClientRoomFactory.Clear();
            var mod_StellarNet_Lite_Client_Modules_ClientReplayModule = new StellarNet.Lite.Client.Modules.ClientReplayModule(clientApp);
            clientApp.GlobalDispatcher.Register(601, (packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.S2C_ReplayList)) is StellarNet.Lite.Shared.Protocol.S2C_ReplayList msg) {
                    mod_StellarNet_Lite_Client_Modules_ClientReplayModule.OnS2C_ReplayList(msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.S2C_ReplayList, MsgId: 601");
                }
            });
            clientApp.GlobalDispatcher.Register(604, (packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayStart)) is StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayStart msg) {
                    mod_StellarNet_Lite_Client_Modules_ClientReplayModule.OnS2C_DownloadReplayStart(msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayStart, MsgId: 604");
                }
            });
            clientApp.GlobalDispatcher.Register(605, (packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayChunk)) is StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayChunk msg) {
                    mod_StellarNet_Lite_Client_Modules_ClientReplayModule.OnS2C_DownloadReplayChunk(msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayChunk, MsgId: 605");
                }
            });
            clientApp.GlobalDispatcher.Register(603, (packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayResult)) is StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayResult msg) {
                    mod_StellarNet_Lite_Client_Modules_ClientReplayModule.OnS2C_DownloadReplayResult(msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.S2C_DownloadReplayResult, MsgId: 603");
                }
            });
            var mod_StellarNet_Lite_Client_Modules_ClientLobbyModule = new StellarNet.Lite.Client.Modules.ClientLobbyModule(clientApp);
            clientApp.GlobalDispatcher.Register(211, (packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.S2C_RoomListResponse)) is StellarNet.Lite.Shared.Protocol.S2C_RoomListResponse msg) {
                    mod_StellarNet_Lite_Client_Modules_ClientLobbyModule.OnS2C_RoomListResponse(msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.S2C_RoomListResponse, MsgId: 211");
                }
            });
            var mod_StellarNet_Lite_Client_Modules_ClientUserModule = new StellarNet.Lite.Client.Modules.ClientUserModule(clientApp);
            clientApp.GlobalDispatcher.Register(101, (packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.S2C_LoginResult)) is StellarNet.Lite.Shared.Protocol.S2C_LoginResult msg) {
                    mod_StellarNet_Lite_Client_Modules_ClientUserModule.OnS2C_LoginResult(msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.S2C_LoginResult, MsgId: 101");
                }
            });
            clientApp.GlobalDispatcher.Register(104, (packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.S2C_ReconnectResult)) is StellarNet.Lite.Shared.Protocol.S2C_ReconnectResult msg) {
                    mod_StellarNet_Lite_Client_Modules_ClientUserModule.OnS2C_ReconnectResult(msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.S2C_ReconnectResult, MsgId: 104");
                }
            });
            clientApp.GlobalDispatcher.Register(102, (packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.S2C_KickOut)) is StellarNet.Lite.Shared.Protocol.S2C_KickOut msg) {
                    mod_StellarNet_Lite_Client_Modules_ClientUserModule.OnS2C_KickOut(msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.S2C_KickOut, MsgId: 102");
                }
            });
            var mod_StellarNet_Lite_Client_Modules_ClientRoomModule = new StellarNet.Lite.Client.Modules.ClientRoomModule(clientApp);
            clientApp.GlobalDispatcher.Register(201, (packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.S2C_CreateRoomResult)) is StellarNet.Lite.Shared.Protocol.S2C_CreateRoomResult msg) {
                    mod_StellarNet_Lite_Client_Modules_ClientRoomModule.OnS2C_CreateRoomResult(msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.S2C_CreateRoomResult, MsgId: 201");
                }
            });
            clientApp.GlobalDispatcher.Register(203, (packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.S2C_JoinRoomResult)) is StellarNet.Lite.Shared.Protocol.S2C_JoinRoomResult msg) {
                    mod_StellarNet_Lite_Client_Modules_ClientRoomModule.OnS2C_JoinRoomResult(msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.S2C_JoinRoomResult, MsgId: 203");
                }
            });
            clientApp.GlobalDispatcher.Register(205, (packet) => {
                if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.S2C_LeaveRoomResult)) is StellarNet.Lite.Shared.Protocol.S2C_LeaveRoomResult msg) {
                    mod_StellarNet_Lite_Client_Modules_ClientRoomModule.OnS2C_LeaveRoomResult(msg);
                } else {
                    StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.S2C_LeaveRoomResult, MsgId: 205");
                }
            });
            ClientRoomFactory.Register(200, () => new StellarNet.Lite.Client.Components.ClientObjectSyncComponent(clientApp));
            ClientRoomFactory.Register(102, () => new StellarNet.Lite.Game.Client.Components.ClientSocialRoomComponent(clientApp));
            ClientRoomFactory.Register(1, () => new StellarNet.Lite.Client.Components.ClientRoomSettingsComponent(clientApp));
        }

        public static void BindClientComponent(StellarNet.Lite.Client.Core.ClientRoomComponent comp, StellarNet.Lite.Client.Core.ClientRoomDispatcher dispatcher, Func<byte[], Type, object> deserializeFunc)
        {
            switch (comp)
            {
                case StellarNet.Lite.Client.Components.ClientObjectSyncComponent c_200:
                    dispatcher.Register(1100, (packet) => {
                        if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.S2C_ObjectSpawn)) is StellarNet.Lite.Shared.Protocol.S2C_ObjectSpawn msg) {
                            c_200.OnS2C_ObjectSpawn(msg);
                        } else {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.S2C_ObjectSpawn, MsgId: 1100");
                        }
                    });
                    dispatcher.Register(1101, (packet) => {
                        if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.S2C_ObjectDestroy)) is StellarNet.Lite.Shared.Protocol.S2C_ObjectDestroy msg) {
                            c_200.OnS2C_ObjectDestroy(msg);
                        } else {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.S2C_ObjectDestroy, MsgId: 1101");
                        }
                    });
                    dispatcher.Register(1102, (packet) => {
                        if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.S2C_ObjectSync)) is StellarNet.Lite.Shared.Protocol.S2C_ObjectSync msg) {
                            c_200.OnS2C_ObjectSync(msg);
                        } else {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.S2C_ObjectSync, MsgId: 1102");
                        }
                    });
                    break;
                case StellarNet.Lite.Game.Client.Components.ClientSocialRoomComponent c_102:
                    dispatcher.Register(1304, (packet) => {
                        if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Game.Shared.Protocol.S2C_SocialBubbleSync)) is StellarNet.Lite.Game.Shared.Protocol.S2C_SocialBubbleSync msg) {
                            c_102.OnS2C_SocialBubbleSync(msg);
                        } else {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Game.Shared.Protocol.S2C_SocialBubbleSync, MsgId: 1304");
                        }
                    });
                    break;
                case StellarNet.Lite.Client.Components.ClientRoomSettingsComponent c_1:
                    dispatcher.Register(300, (packet) => {
                        if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.S2C_RoomSnapshot)) is StellarNet.Lite.Shared.Protocol.S2C_RoomSnapshot msg) {
                            c_1.OnS2C_RoomSnapshot(msg);
                        } else {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.S2C_RoomSnapshot, MsgId: 300");
                        }
                    });
                    dispatcher.Register(301, (packet) => {
                        if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.S2C_MemberJoined)) is StellarNet.Lite.Shared.Protocol.S2C_MemberJoined msg) {
                            c_1.OnS2C_MemberJoined(msg);
                        } else {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.S2C_MemberJoined, MsgId: 301");
                        }
                    });
                    dispatcher.Register(302, (packet) => {
                        if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.S2C_MemberLeft)) is StellarNet.Lite.Shared.Protocol.S2C_MemberLeft msg) {
                            c_1.OnS2C_MemberLeft(msg);
                        } else {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.S2C_MemberLeft, MsgId: 302");
                        }
                    });
                    dispatcher.Register(304, (packet) => {
                        if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.S2C_MemberReadyChanged)) is StellarNet.Lite.Shared.Protocol.S2C_MemberReadyChanged msg) {
                            c_1.OnS2C_MemberReadyChanged(msg);
                        } else {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.S2C_MemberReadyChanged, MsgId: 304");
                        }
                    });
                    dispatcher.Register(501, (packet) => {
                        if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.S2C_GameStarted)) is StellarNet.Lite.Shared.Protocol.S2C_GameStarted msg) {
                            c_1.OnS2C_GameStarted(msg);
                        } else {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.S2C_GameStarted, MsgId: 501");
                        }
                    });
                    dispatcher.Register(503, (packet) => {
                        if (deserializeFunc(packet.Payload, typeof(StellarNet.Lite.Shared.Protocol.S2C_GameEnded)) is StellarNet.Lite.Shared.Protocol.S2C_GameEnded msg) {
                            c_1.OnS2C_GameEnded(msg);
                        } else {
                            StellarNet.Lite.Shared.Infrastructure.NetLogger.LogError("AutoRegistry", $"反序列化失败: StellarNet.Lite.Shared.Protocol.S2C_GameEnded, MsgId: 503");
                        }
                    });
                    break;
            }
        }
    }
}
