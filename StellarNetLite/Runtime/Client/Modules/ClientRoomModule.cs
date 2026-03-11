using System;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Client.Core.Events;

namespace StellarNet.Lite.Client.Modules
{
    public sealed class ClientRoomModule
    {
        private readonly ClientApp _app;
        private readonly Action<Packet> _networkSender;
        private readonly Func<object, byte[]> _serializeFunc;

        public ClientRoomModule(ClientApp app, Action<Packet> networkSender, Func<object, byte[]> serializeFunc)
        {
            _app = app;
            _networkSender = networkSender;
            _serializeFunc = serializeFunc;
        }

        [NetHandler]
        public void OnS2C_CreateRoomResult(S2C_CreateRoomResult msg)
        {
            if (msg == null) return;

            if (_app.State == ClientAppState.ReplayRoom)
            {
                LiteLogger.LogWarning("[ClientRoomModule] ", $" 拦截: 当前处于回放模式，忽略建房结果");
                return;
            }

            if (msg.Success)
            {
                _app.EnterOnlineRoom(msg.RoomId);
                bool buildSuccess = ClientRoomFactory.BuildComponents(_app.CurrentRoom, msg.ComponentIds);
                if (!buildSuccess)
                {
                    LiteLogger.LogError($"[ClientRoomModule]", $"  房间 {msg.RoomId} 本地装配失败，已强制销毁本地实例并终止握手");
                    _app.LeaveRoom();
                    return;
                }

                LiteLogger.LogInfo($"[ClientRoomModule] ", $" 建房成功, 本地装配完毕，准备发送就绪握手。房间: {msg.RoomId}");
                var readyMsg = new C2S_RoomSetupReady { RoomId = msg.RoomId };
                _app.SendMessage(readyMsg);
            }
            else
            {
                LiteLogger.LogError($"[ClientRoomModule] ", $" 建房失败: {msg.Reason}");
            }

            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_JoinRoomResult(S2C_JoinRoomResult msg)
        {
            if (msg == null) return;

            if (_app.State == ClientAppState.ReplayRoom)
            {
                LiteLogger.LogWarning("[ClientRoomModule] ", $" 拦截: 当前处于回放模式，忽略加房结果");
                return;
            }

            if (msg.Success)
            {
                _app.EnterOnlineRoom(msg.RoomId);
                bool buildSuccess = ClientRoomFactory.BuildComponents(_app.CurrentRoom, msg.ComponentIds);
                if (!buildSuccess)
                {
                    LiteLogger.LogError($"[ClientRoomModule] ", $" 房间 {msg.RoomId} 本地装配失败，已强制销毁本地实例并终止握手");
                    _app.LeaveRoom();
                    return;
                }

                LiteLogger.LogInfo($"[ClientRoomModule] ", $" 加房成功, 本地装配完毕，准备发送就绪握手。房间: {msg.RoomId}");
                var readyMsg = new C2S_RoomSetupReady { RoomId = msg.RoomId };
                _app.SendMessage(readyMsg);
            }
            else
            {
                LiteLogger.LogError($"[ClientRoomModule] ", $" 加房失败: {msg.Reason}");
            }

            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_LeaveRoomResult(S2C_LeaveRoomResult msg)
        {
            if (msg == null) return;

            if (_app.State == ClientAppState.ReplayRoom)
            {
                LiteLogger.LogWarning("[ClientRoomModule]", $"  拦截: 当前处于回放模式，忽略离房结果");
                return;
            }

            _app.LeaveRoom();
            LiteLogger.LogInfo("[ClientRoomModule] ", $" 已离开房间");

            GlobalTypeNetEvent.Broadcast(msg);
        }
    }
}