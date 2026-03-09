using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Mirror;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Demo
{
    /// <summary>
    /// StellarNet 全边界综合测试控制台。
    /// 职责：提供对核心框架所有极端情况（重连、踢人、顶号、回放、GC）的白盒测试入口。
    /// </summary>
    public class StellarNetDemoUI : MonoBehaviour
    {
        private StellarNetMirrorManager _manager;

        // 客户端状态
        private string _inputAccountId = "Player_1001";
        private string _inputRoomId = "";
        private Vector2 _clientScroll;

        // 回放沙盒状态
        private ClientReplayPlayer _replayPlayer;
        private ReplayFile _lastSavedReplay;

        // 服务端状态
        private Vector2 _serverScroll;

        private void Start()
        {
            _manager = NetworkManager.singleton as StellarNetMirrorManager;
            if (_manager == null)
            {
                Debug.LogError("[DemoUI] 致命错误: 场景中缺失 StellarNetMirrorManager 组件！");
            }
        }

        private void OnGUI()
        {
            if (_manager == null) return;

            if (!NetworkServer.active && !NetworkClient.active)
            {
                DrawModeSelection();
                return;
            }

            GUILayout.BeginHorizontal();

            if (NetworkClient.active && _manager.ClientApp != null)
            {
                GUILayout.BeginArea(new Rect(20, 20, 400, Screen.height - 40), GUI.skin.box);
                _clientScroll = GUILayout.BeginScrollView(_clientScroll);
                DrawClientPanel();
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }

            if (NetworkServer.active && _manager.ServerApp != null)
            {
                int xOffset = NetworkClient.active ? 440 : 20;
                GUILayout.BeginArea(new Rect(xOffset, 20, 500, Screen.height - 40), GUI.skin.box);
                _serverScroll = GUILayout.BeginScrollView(_serverScroll);
                DrawServerPanel();
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }

            GUILayout.EndHorizontal();
        }

        #region ================= 启动面板 =================

        private void DrawModeSelection()
        {
            GUILayout.BeginArea(new Rect(Screen.width / 2 - 150, Screen.height / 2 - 100, 300, 250), GUI.skin.box);
            GUILayout.Label("<b><size=16>StellarNet 综合测试台</size></b>", new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(20);

            if (GUILayout.Button("Host 模式 (Server + Client 同进程)", GUILayout.Height(40))) _manager.StartHost();
            GUILayout.Space(10);
            if (GUILayout.Button("Server Only (独立服务端)", GUILayout.Height(40))) _manager.StartServer();
            GUILayout.Space(10);
            if (GUILayout.Button("Client Only (独立客户端)", GUILayout.Height(40))) _manager.StartClient();

            GUILayout.EndArea();
        }

        #endregion

        #region ================= 客户端面板 (Client View) =================

        private void DrawClientPanel()
        {
            var app = _manager.ClientApp;
            var serialize = _manager.SerializeFunc;

            GUILayout.Label("<b><size=16>客户端控制台 (Client View)</size></b>", new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(10);

            // 核心防御测试：回放沙盒模式
            if (app.State == ClientAppState.ReplayRoom)
            {
                DrawClientReplayPanel();
                return;
            }

            if (!app.Session.IsLoggedIn)
            {
                GUILayout.Label("当前状态: 未登录");
                GUILayout.Space(10);

                GUILayout.BeginHorizontal();
                GUILayout.Label("账号 ID:", GUILayout.Width(60));
                _inputAccountId = GUILayout.TextField(_inputAccountId);
                GUILayout.EndHorizontal();

                GUILayout.Space(10);
                if (GUILayout.Button("发起登录 (Login)", GUILayout.Height(40)))
                {
                    var msg = new C2S_Login { AccountId = _inputAccountId };
                    app.SendGlobal(new Packet(100, NetScope.Global, "", serialize(msg)));
                }

                // 回放入口
                if (_lastSavedReplay != null)
                {
                    GUILayout.Space(20);
                    GUI.color = Color.cyan;
                    if (GUILayout.Button($"播放本地回放 (帧数: {_lastSavedReplay.Frames.Count})", GUILayout.Height(40)))
                    {
                        _replayPlayer = new ClientReplayPlayer(app);
                        _replayPlayer.StartReplay(_lastSavedReplay);
                    }

                    GUI.color = Color.white;
                }
            }
            else if (app.State == ClientAppState.Idle)
            {
                GUILayout.Label($"当前状态: 大厅闲置\nSessionId: {app.Session.SessionId}\nUID: {app.Session.Uid}");
                GUILayout.Space(10);

                // 重连测试区
                GUI.color = Color.yellow;
                GUILayout.Label("--- 断线重连测试区 ---");
                if (GUILayout.Button("1. 接受重连 (Confirm Reconnect)", GUILayout.Height(30)))
                {
                    var msg = new C2S_ConfirmReconnect { Accept = true };
                    app.SendGlobal(new Packet(103, NetScope.Global, "", serialize(msg)));
                }

                if (GUILayout.Button("2. 拒绝重连 (Reject Reconnect)", GUILayout.Height(30)))
                {
                    var msg = new C2S_ConfirmReconnect { Accept = false };
                    app.SendGlobal(new Packet(103, NetScope.Global, "", serialize(msg)));
                }

                GUI.color = Color.white;
                GUILayout.Space(10);

                // 房间创建区
                GUILayout.Label("--- 房间创建与加入 ---");
                if (GUILayout.Button("创建基础房间 (仅 Settings: ID 1)", GUILayout.Height(30)))
                {
                    var msg = new C2S_CreateRoom { RoomName = "DemoRoom", ComponentIds = new int[] { 1 }, RequestToken = Guid.NewGuid().ToString() };
                    app.SendGlobal(new Packet(200, NetScope.Global, "", serialize(msg)));
                }

                GUILayout.Space(5);
                GUI.color = Color.green;
                // 核心改动：新增对战房间创建入口，显式声明装配 ComponentId 100
                if (GUILayout.Button("创建对战房间 (Settings + GameDemo: ID 1, 100)", GUILayout.Height(40)))
                {
                    var msg = new C2S_CreateRoom { RoomName = "BattleRoom", ComponentIds = new int[] { 1, 100 }, RequestToken = Guid.NewGuid().ToString() };
                    app.SendGlobal(new Packet(200, NetScope.Global, "", serialize(msg)));
                }

                GUI.color = Color.white;

                GUILayout.Space(10);
                GUILayout.BeginHorizontal();
                _inputRoomId = GUILayout.TextField(_inputRoomId, GUILayout.Height(30));
                if (GUILayout.Button("加入房间", GUILayout.Height(30), GUILayout.Width(100)))
                {
                    var msg = new C2S_JoinRoom { RoomId = _inputRoomId };
                    app.SendGlobal(new Packet(202, NetScope.Global, "", serialize(msg)));
                }

                GUILayout.EndHorizontal();
            }
            else if (app.State == ClientAppState.OnlineRoom)
            {
                GUILayout.Label($"当前状态: 房间内\nRoomId: {app.CurrentRoom.RoomId}");
                GUILayout.Space(10);

                ClientRoomSettingsComponent settingsComp = GetClientComponent<ClientRoomSettingsComponent>(app.CurrentRoom);
                if (settingsComp != null)
                {
                    GUILayout.Label("<b>房间成员列表:</b>", new GUIStyle(GUI.skin.label) { richText = true });
                    foreach (var kvp in settingsComp.Members)
                    {
                        var member = kvp.Value;
                        string meStr = member.SessionId == app.Session.SessionId ? " (我)" : "";
                        string ownerStr = member.IsOwner ? "<color=yellow>[房主]</color>" : "";
                        string readyStr = member.IsReady ? "<color=green>已准备</color>" : "<color=red>未准备</color>";
                        GUILayout.Label($"- {member.SessionId.Substring(0, 8)}...{meStr} {ownerStr} 状态: {readyStr}", new GUIStyle(GUI.skin.label) { richText = true });
                    }

                    GUILayout.Space(10);
                    if (GUILayout.Button("切换准备状态 (Toggle Ready)", GUILayout.Height(40)))
                    {
                        if (settingsComp.Members.TryGetValue(app.Session.SessionId, out var myInfo))
                        {
                            var msg = new C2S_SetReady { IsReady = !myInfo.IsReady };
                            app.SendRoom(new Packet(303, NetScope.Room, app.CurrentRoom.RoomId, serialize(msg)));
                        }
                    }
                }

                GUILayout.Space(10);
                if (GUILayout.Button("正常离开房间 (Leave Room)", GUILayout.Height(40)))
                {
                    var msg = new C2S_LeaveRoom();
                    app.SendGlobal(new Packet(204, NetScope.Global, "", serialize(msg)));
                }
            }

            // 物理断网模拟（测试掉线、顶号、重连）
            GUILayout.Space(30);
            GUI.color = Color.red;
            if (GUILayout.Button("模拟物理断网 (断开 Mirror Client 但不发 Leave)", GUILayout.Height(40)))
            {
                _manager.StopClient();
            }

            GUI.color = Color.white;
        }

        private void DrawClientReplayPanel()
        {
            GUILayout.Label("当前状态: <color=cyan>回放沙盒模式 (ReplayRoom)</color>", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Space(10);

            ClientRoomSettingsComponent settingsComp = GetClientComponent<ClientRoomSettingsComponent>(_manager.ClientApp.CurrentRoom);
            if (settingsComp != null)
            {
                GUILayout.Label("<b>回放沙盒成员快照:</b>", new GUIStyle(GUI.skin.label) { richText = true });
                foreach (var kvp in settingsComp.Members)
                {
                    var member = kvp.Value;
                    string ownerStr = member.IsOwner ? "<color=yellow>[房主]</color>" : "";
                    string readyStr = member.IsReady ? "<color=green>已准备</color>" : "<color=red>未准备</color>";
                    GUILayout.Label($"- {member.SessionId.Substring(0, 8)}... {ownerStr} 状态: {readyStr}", new GUIStyle(GUI.skin.label) { richText = true });
                }
            }

            GUILayout.Space(20);
            if (GUILayout.Button("播放下一帧 (Tick)", GUILayout.Height(40)))
            {
                _replayPlayer?.Tick();
            }

            GUILayout.Space(10);
            GUI.color = Color.red;
            if (GUILayout.Button("退出回放 (Stop Replay)", GUILayout.Height(40)))
            {
                _replayPlayer?.StopReplay();
                _replayPlayer = null;
            }

            GUI.color = Color.white;
        }

        #endregion

        #region ================= 服务端面板 (Server Admin) =================

        private void DrawServerPanel()
        {
            GUILayout.Label("<b><size=16>服务端上帝视角 (Server Admin)</size></b>", new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(10);

            // 1. 获取内部数据
            var sessions = GetServerSessions();
            var rooms = GetServerRooms();

            // 2. 渲染 Sessions
            GUILayout.Label($"<b>在线/离线会话总数: {sessions.Count}</b>", new GUIStyle(GUI.skin.label) { richText = true });
            foreach (var kvp in sessions)
            {
                var session = kvp.Value;
                GUILayout.BeginVertical("box");

                string status = session.IsOnline ? "<color=green>在线</color>" : "<color=gray>物理离线</color>";
                GUILayout.Label($"UID: {session.Uid} | 状态: {status}", new GUIStyle(GUI.skin.label) { richText = true });
                GUILayout.Label($"SessionId: {session.SessionId}");
                GUILayout.Label($"所在房间: {(string.IsNullOrEmpty(session.CurrentRoomId) ? "无" : session.CurrentRoomId)}");

                if (session.IsOnline)
                {
                    GUI.color = Color.red;
                    if (GUILayout.Button("强制踢下线 (KickOut)"))
                    {
                        var msg = new S2C_KickOut { Reason = "被管理员强制踢出" };
                        byte[] payload = _manager.SerializeFunc(msg);
                        var packet = new Packet(102, NetScope.Global, "", payload);

                        // 绕过常规路由，直接通过底层连接发送
                        if (NetworkServer.connections.TryGetValue(session.ConnectionId, out var conn))
                        {
                            conn.Send(new MirrorPacketMsg(packet));
                        }

                        _manager.ServerApp.UnbindConnection(session);
                    }

                    GUI.color = Color.white;
                }

                GUILayout.EndVertical();
            }

            GUILayout.Space(20);

            // 3. 渲染 Rooms
            GUILayout.Label($"<b>活跃房间总数: {rooms.Count}</b>", new GUIStyle(GUI.skin.label) { richText = true });
            foreach (var kvp in rooms)
            {
                var room = kvp.Value;
                GUILayout.BeginVertical("box");

                GUILayout.Label($"RoomId: {room.RoomId} | 成员数: {room.MemberCount}");
                GUILayout.Label($"录制状态: {(room.IsRecording ? "<color=red>录制中...</color>" : "未录制")}", new GUIStyle(GUI.skin.label) { richText = true });

                GUILayout.BeginHorizontal();
                if (!room.IsRecording)
                {
                    if (GUILayout.Button("开始录像")) room.StartRecord();
                }
                else
                {
                    GUI.color = Color.cyan;
                    if (GUILayout.Button("停止并保存录像"))
                    {
                        _lastSavedReplay = room.StopRecordAndSave();
                        Debug.Log($"[DemoUI] 录像已生成，帧数: {_lastSavedReplay.Frames.Count}");
                    }

                    GUI.color = Color.white;
                }

                GUI.color = Color.red;
                if (GUILayout.Button("强制销毁房间"))
                {
                    _manager.ServerApp.DestroyRoom(room.RoomId);
                }

                GUI.color = Color.white;
                GUILayout.EndHorizontal();

                GUILayout.EndVertical();
            }
        }

        #endregion

        #region ================= 反射辅助工具 (仅限 Demo 测试用) =================

        private Dictionary<string, Session> GetServerSessions()
        {
            var field = typeof(ServerApp).GetField("_sessions", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && _manager.ServerApp != null)
            {
                return field.GetValue(_manager.ServerApp) as Dictionary<string, Session> ?? new Dictionary<string, Session>();
            }

            return new Dictionary<string, Session>();
        }

        private Dictionary<string, Room> GetServerRooms()
        {
            var field = typeof(ServerApp).GetField("_rooms", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && _manager.ServerApp != null)
            {
                return field.GetValue(_manager.ServerApp) as Dictionary<string, Room> ?? new Dictionary<string, Room>();
            }

            return new Dictionary<string, Room>();
        }

        private T GetClientComponent<T>(ClientRoom room) where T : ClientRoomComponent
        {
            if (room == null) return null;
            var field = typeof(ClientRoom).GetField("_components", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                var list = field.GetValue(room) as List<ClientRoomComponent>;
                if (list != null)
                {
                    foreach (var c in list)
                    {
                        if (c is T target) return target;
                    }
                }
            }

            return null;
        }

        #endregion
    }
}