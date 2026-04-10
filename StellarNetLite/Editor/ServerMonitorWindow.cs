#if UNITY_EDITOR
using System.Collections.Generic;
using StellarNet.Lite.Runtime;
using StellarNet.Lite.Server.Core;
using UnityEditor;
using UnityEngine;

namespace StellarNet.Lite.Editor
{
    /// <summary>
    /// 服务端运行时监控面板。
    /// 用于在 Editor 运行期实时查看 ServerApp 的内存状态与业务流转。
    /// </summary>
    public class ServerMonitorWindow : EditorWindow
    {
        private Vector2 _roomScroll;
        private Vector2 _sessionScroll;
        private string _selectedRoomId = string.Empty;

        [MenuItem("StellarNetLite/服务端运行时监控 (Server Monitor)")]
        public static void ShowWindow()
        {
            var window = GetWindow<ServerMonitorWindow>("Server Monitor");
            window.minSize = new Vector2(600, 500);
            window.Show();
        }

        private void OnInspectorUpdate()
        {
            if (Application.isPlaying)
            {
                Repaint();
            }
        }

        private void OnGUI()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("请在运行模式下查看服务端状态。", MessageType.Info);
                return;
            }

            var appManager = FindObjectOfType<StellarNetAppManager>();
            if (appManager == null || appManager.ServerApp == null)
            {
                EditorGUILayout.HelpBox("未检测到活跃的 ServerApp 实例。", MessageType.Warning);
                return;
            }

            ServerApp serverApp = appManager.ServerApp;

            DrawOverview(serverApp);
            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            DrawRoomList(serverApp);
            DrawSessionList(serverApp);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);
            DrawRoomDetails(serverApp);
        }

        private void DrawOverview(ServerApp serverApp)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("全局概览", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"当前在线连接数: {serverApp.Sessions.Count}");
            EditorGUILayout.LabelField($"当前活跃房间数: {serverApp.Rooms.Count}");
            EditorGUILayout.LabelField($"TickRate: {serverApp.Config.TickRate} Hz");
            EditorGUILayout.EndVertical();
        }

        private void DrawRoomList(ServerApp serverApp)
        {
            EditorGUILayout.BeginVertical("box", GUILayout.Width(position.width * 0.45f));
            EditorGUILayout.LabelField($"房间列表 ({serverApp.Rooms.Count})", EditorStyles.boldLabel);
            _roomScroll = EditorGUILayout.BeginScrollView(_roomScroll, GUILayout.Height(150));

            foreach (var kvp in serverApp.Rooms)
            {
                Room room = kvp.Value;
                GUI.color = _selectedRoomId == room.RoomId ? Color.cyan : Color.white;
                if (GUILayout.Button($"[{room.State}] {room.RoomName} ({room.MemberCount}人)", EditorStyles.toolbarButton))
                {
                    _selectedRoomId = room.RoomId;
                }

                GUI.color = Color.white;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawSessionList(ServerApp serverApp)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"会话列表 ({serverApp.Sessions.Count})", EditorStyles.boldLabel);
            _sessionScroll = EditorGUILayout.BeginScrollView(_sessionScroll, GUILayout.Height(150));

            foreach (var kvp in serverApp.Sessions)
            {
                Session session = kvp.Value;
                string status = session.IsOnline ? "<color=green>在线</color>" : "<color=red>离线</color>";
                string roomInfo = string.IsNullOrEmpty(session.CurrentRoomId) ? "大厅" : $"房间:{session.CurrentRoomId}";
                EditorGUILayout.LabelField($"[{session.AccountId}] {status} | {roomInfo}", new GUIStyle(EditorStyles.label) { richText = true });
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawRoomDetails(ServerApp serverApp)
        {
            if (string.IsNullOrEmpty(_selectedRoomId) || !serverApp.Rooms.TryGetValue(_selectedRoomId, out Room room))
            {
                EditorGUILayout.HelpBox("请在上方选择一个房间查看详情。", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"房间详情: {room.RoomName} ({room.RoomId})", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"状态: {room.State} | Tick: {room.CurrentTick} | 录制中: {room.IsRecording}");

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("成员列表:", EditorStyles.boldLabel);
            foreach (var kvp in room.Members)
            {
                Session member = kvp.Value;
                EditorGUILayout.LabelField($"- {member.AccountId} (Ready: {member.IsRoomReady})");
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("挂载组件:", EditorStyles.boldLabel);
            if (room.ComponentIds != null)
            {
                foreach (int compId in room.ComponentIds)
                {
                    EditorGUILayout.LabelField($"- Component ID: {compId}");
                }
            }

            EditorGUILayout.EndVertical();
        }
    }
}
#endif