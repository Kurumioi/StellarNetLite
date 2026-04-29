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
        /// <summary>
        /// 房间列表滚动位置。
        /// </summary>
        private Vector2 _roomScroll;

        /// <summary>
        /// 会话列表滚动位置。
        /// </summary>
        private Vector2 _sessionScroll;

        /// <summary>
        /// 当前选中的房间 Id。
        /// </summary>
        private string _selectedRoomId = string.Empty;

        /// <summary>
        /// 打开服务端运行时监控窗口。
        /// </summary>
        [MenuItem("StellarNetLite/服务端运行时监控", false, 4)]
        public static void ShowWindow()
        {
            var window = GetWindow<ServerMonitorWindow>("Server Monitor");
            window.minSize = new Vector2(600, 500);
            window.Show();
        }

        /// <summary>
        /// 运行态下周期刷新监控窗口。
        /// </summary>
        private void OnInspectorUpdate()
        {
            if (Application.isPlaying)
            {
                Repaint();
            }
        }

        /// <summary>
        /// 绘制监控窗口主体。
        /// </summary>
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
            lock (serverApp.SyncRoot)
            {
                DrawOverview(serverApp);
                EditorGUILayout.Space(10);

                EditorGUILayout.BeginHorizontal();
                DrawRoomList(serverApp);
                DrawSessionList(serverApp);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(10);
                DrawRoomDetails(serverApp);
            }
        }

        /// <summary>
        /// 绘制服务端全局概览信息。
        /// </summary>
        private void DrawOverview(ServerApp serverApp)
        {
            RoomRuntimeSnapshot[] rooms = serverApp.CaptureRoomRuntimeSnapshots();
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("全局概览", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"当前在线连接数: {serverApp.Sessions.Count}");
            EditorGUILayout.LabelField($"当前活跃房间数: {rooms.Length}");
            EditorGUILayout.LabelField($"TickRate: {serverApp.Config.TickRate} Hz");
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 绘制房间列表，并支持切换当前选中房间。
        /// </summary>
        private void DrawRoomList(ServerApp serverApp)
        {
            RoomRuntimeSnapshot[] rooms = serverApp.CaptureRoomRuntimeSnapshots();
            EditorGUILayout.BeginVertical("box", GUILayout.Width(position.width * 0.45f));
            EditorGUILayout.LabelField($"房间列表 ({rooms.Length})", EditorStyles.boldLabel);
            _roomScroll = EditorGUILayout.BeginScrollView(_roomScroll, GUILayout.Height(150));

            for (int i = 0; i < rooms.Length; i++)
            {
                RoomRuntimeSnapshot room = rooms[i];
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

        /// <summary>
        /// 绘制当前所有会话信息。
        /// </summary>
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

        /// <summary>
        /// 绘制当前选中房间的运行时详情。
        /// </summary>
        private void DrawRoomDetails(ServerApp serverApp)
        {
            RoomDetailedSnapshot room = string.IsNullOrEmpty(_selectedRoomId)
                ? null
                : serverApp.CaptureRoomDetailedSnapshot(_selectedRoomId);
            if (room == null || room.Runtime == null)
            {
                EditorGUILayout.HelpBox("请在上方选择一个房间查看详情。", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"房间详情: {room.Runtime.RoomName} ({room.Runtime.RoomId})", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"状态: {room.Runtime.State} | Tick: {room.Runtime.CurrentTick} | 录制中: {room.Runtime.IsRecording}");
            EditorGUILayout.LabelField($"Worker: {room.Runtime.AssignedWorkerId} | AvgTickMs: {room.Runtime.WorkerAverageTickMs:F3}");

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("成员列表:", EditorStyles.boldLabel);
            foreach (RoomMemberSnapshot member in room.Members)
            {
                EditorGUILayout.LabelField($"- {member.AccountId} (Ready: {member.IsRoomReady})");
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("挂载组件:", EditorStyles.boldLabel);
            if (room.Runtime.ComponentIds != null)
            {
                foreach (int compId in room.Runtime.ComponentIds)
                {
                    EditorGUILayout.LabelField($"- Component ID: {compId}");
                }
            }

            EditorGUILayout.EndVertical();
        }
    }
}
#endif
