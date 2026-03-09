using System.Collections.Generic;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.GameDemo.Shared;

namespace StellarNet.Lite.GameDemo.Client
{
    /// <summary>
    /// 客户端胶囊对战表现层 (View层)。
    /// 职责：监听 EventBus 驱动场景表现；捕获玩家输入并封装为网络请求发往服务端。
    /// 架构说明：采用内部类 CapsuleViewData 聚合表现层状态，避免频繁的 GetComponent 调用，降低 CPU 消耗。
    /// </summary>
    public class DemoGameView : MonoBehaviour
    {
        private StellarNetMirrorManager _manager;
        private Camera _mainCamera;
        private Plane _groundPlane = new Plane(Vector3.up, Vector3.zero);

        // 内部聚合表现层数据，拒绝分散的组件挂载
        private class CapsuleViewData
        {
            public GameObject RootGo;
            public TextMesh HpText;
            public Vector3 TargetPosition;
            public int CurrentHp;
        }

        private readonly Dictionary<string, CapsuleViewData> _views = new Dictionary<string, CapsuleViewData>();
        private string _winnerSessionId = string.Empty;

        private void Start()
        {
            _manager = FindObjectOfType<StellarNetMirrorManager>();
            if (_manager == null)
            {
                Debug.LogError("[DemoGameView] 初始化失败：场景中缺失 StellarNetMirrorManager");
                return;
            }

            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                Debug.LogError("[DemoGameView] 初始化失败：场景中缺失 MainCamera");
            }
        }

        private void OnEnable()
        {
            LiteEventBus<DemoSnapshotEvent>.OnEvent += HandleSnapshot;
            LiteEventBus<DemoPlayerJoinedEvent>.OnEvent += HandlePlayerJoined;
            LiteEventBus<DemoPlayerLeftEvent>.OnEvent += HandlePlayerLeft;
            LiteEventBus<DemoMoveEvent>.OnEvent += HandleMoveSync;
            LiteEventBus<DemoHpEvent>.OnEvent += HandleHpSync;
            LiteEventBus<DemoGameOverEvent>.OnEvent += HandleGameOver;
        }

        private void OnDisable()
        {
            LiteEventBus<DemoSnapshotEvent>.OnEvent -= HandleSnapshot;
            LiteEventBus<DemoPlayerJoinedEvent>.OnEvent -= HandlePlayerJoined;
            LiteEventBus<DemoPlayerLeftEvent>.OnEvent -= HandlePlayerLeft;
            LiteEventBus<DemoMoveEvent>.OnEvent -= HandleMoveSync;
            LiteEventBus<DemoHpEvent>.OnEvent -= HandleHpSync;
            LiteEventBus<DemoGameOverEvent>.OnEvent -= HandleGameOver;
        }

        private void Update()
        {
            if (_manager == null || _manager.ClientApp == null || _manager.ClientApp.State != ClientAppState.OnlineRoom)
            {
                return;
            }

            ProcessInput();
            InterpolateMovement();
        }

        private void OnGUI()
        {
            if (!string.IsNullOrEmpty(_winnerSessionId))
            {
                GUI.color = Color.yellow;
                GUI.skin.label.fontSize = 30;
                GUI.Label(new Rect(Screen.width / 2 - 150, Screen.height / 2 - 50, 300, 100), $"游戏结束!\n胜利者: {_winnerSessionId}");
                GUI.skin.label.fontSize = 0;
                GUI.color = Color.white;
            }
        }

        #region ================= 输入与网络请求 =================

        private void ProcessInput()
        {
            if (!string.IsNullOrEmpty(_winnerSessionId)) return; // 游戏结束后禁止输入

            string mySessionId = _manager.ClientApp.Session.SessionId;
            if (!_views.TryGetValue(mySessionId, out var myView) || myView.CurrentHp <= 0)
            {
                return; // 自身不存在或已死亡，禁止操作
            }

            // 右键移动 (基于射线检测地平面)
            if (Input.GetMouseButtonDown(1))
            {
                Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);
                if (_groundPlane.Raycast(ray, out float distance))
                {
                    Vector3 hitPoint = ray.GetPoint(distance);
                    SendMoveRequest(hitPoint);
                }
            }

            // 左键攻击 (基于射线检测胶囊体)
            if (Input.GetMouseButtonDown(0))
            {
                Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    // 利用 GameObject.name 存储 SessionId 的零 GC 寻址技巧
                    string targetSessionId = hit.collider.gameObject.name;
                    if (targetSessionId != mySessionId && _views.ContainsKey(targetSessionId))
                    {
                        SendAttackRequest(targetSessionId);
                    }
                }
            }
        }

        private void SendMoveRequest(Vector3 targetPos)
        {
            var msg = new C2S_DemoMoveReq { TargetX = targetPos.x, TargetY = targetPos.y, TargetZ = targetPos.z };
            byte[] payload = _manager.SerializeFunc(msg);
            var packet = new Packet(1001, NetScope.Room, _manager.ClientApp.CurrentRoom.RoomId, payload);
            _manager.ClientApp.SendRoom(packet);
        }

        private void SendAttackRequest(string targetSessionId)
        {
            var msg = new C2S_DemoAttackReq { TargetSessionId = targetSessionId };
            byte[] payload = _manager.SerializeFunc(msg);
            var packet = new Packet(1002, NetScope.Room, _manager.ClientApp.CurrentRoom.RoomId, payload);
            _manager.ClientApp.SendRoom(packet);
        }

        #endregion

        #region ================= 表现层插值与渲染 =================

        private void InterpolateMovement()
        {
            float deltaTime = Time.deltaTime;
            foreach (var kvp in _views)
            {
                var view = kvp.Value;
                if (view.RootGo != null && view.CurrentHp > 0)
                {
                    // 简单的线性插值平滑移动，掩盖网络抖动
                    view.RootGo.transform.position = Vector3.Lerp(view.RootGo.transform.position, view.TargetPosition, deltaTime * 10f);
                }
            }
        }

        private void CreateOrUpdateCapsule(DemoPlayerInfo info)
        {
            if (info == null) return;

            if (!_views.TryGetValue(info.SessionId, out var view))
            {
                // 动态生成胶囊体，避免依赖外部 Prefab，保持 Demo 的独立性
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = info.SessionId; // 核心技巧：利用 Name 绑定 SessionId 供射线检测使用
                
                // 动态生成血条文本
                GameObject textGo = new GameObject("HpText");
                textGo.transform.SetParent(go.transform);
                textGo.transform.localPosition = new Vector3(0, 1.5f, 0);
                var tm = textGo.AddComponent<TextMesh>();
                tm.anchor = TextAnchor.MiddleCenter;
                tm.characterSize = 0.1f;
                tm.fontSize = 60;
                tm.color = Color.red;

                // 区分自己和其他玩家的颜色
                var renderer = go.GetComponent<Renderer>();
                if (_manager.ClientApp.Session.SessionId == info.SessionId)
                {
                    renderer.material.color = Color.green;
                }

                view = new CapsuleViewData
                {
                    RootGo = go,
                    HpText = tm
                };
                _views[info.SessionId] = view;
            }

            view.TargetPosition = new Vector3(info.PosX, info.PosY, info.PosZ);
            view.RootGo.transform.position = view.TargetPosition; // 初始强制同步位置
            UpdateHpDisplay(view, info.Hp);
        }

        private void UpdateHpDisplay(CapsuleViewData view, int hp)
        {
            view.CurrentHp = hp;
            if (hp <= 0)
            {
                view.HpText.text = "DEAD";
                view.HpText.color = Color.gray;
                // 死亡后放倒胶囊体作为表现
                view.RootGo.transform.rotation = Quaternion.Euler(90, 0, 0);
                view.RootGo.transform.position = new Vector3(view.TargetPosition.x, 0.5f, view.TargetPosition.z);
            }
            else
            {
                view.HpText.text = $"HP: {hp}";
            }
        }

        private void DestroyAllCapsules()
        {
            foreach (var kvp in _views)
            {
                if (kvp.Value.RootGo != null)
                {
                    Destroy(kvp.Value.RootGo);
                }
            }
            _views.Clear();
            _winnerSessionId = string.Empty;
        }

        #endregion

        #region ================= 事件总线响应 =================

        private void HandleSnapshot(DemoSnapshotEvent evt)
        {
            DestroyAllCapsules();
            if (evt.Players == null) return;

            for (int i = 0; i < evt.Players.Length; i++)
            {
                CreateOrUpdateCapsule(evt.Players[i]);
            }
            Debug.Log($"[DemoGameView] 收到全量快照，重建 {evt.Players.Length} 个实体");
        }

        private void HandlePlayerJoined(DemoPlayerJoinedEvent evt)
        {
            CreateOrUpdateCapsule(evt.Player);
            Debug.Log($"[DemoGameView] 玩家加入: {evt.Player?.SessionId}");
        }

        private void HandlePlayerLeft(DemoPlayerLeftEvent evt)
        {
            if (_views.TryGetValue(evt.SessionId, out var view))
            {
                if (view.RootGo != null) Destroy(view.RootGo);
                _views.Remove(evt.SessionId);
                Debug.Log($"[DemoGameView] 玩家离开，销毁实体: {evt.SessionId}");
            }
        }

        private void HandleMoveSync(DemoMoveEvent evt)
        {
            if (_views.TryGetValue(evt.SessionId, out var view))
            {
                view.TargetPosition = new Vector3(evt.TargetX, evt.TargetY, evt.TargetZ);
            }
        }

        private void HandleHpSync(DemoHpEvent evt)
        {
            if (_views.TryGetValue(evt.SessionId, out var view))
            {
                UpdateHpDisplay(view, evt.Hp);
            }
        }

        private void HandleGameOver(DemoGameOverEvent evt)
        {
            _winnerSessionId = evt.WinnerSessionId;
            Debug.Log($"[DemoGameView] 接收到游戏结束事件，胜利者: {_winnerSessionId}");
        }

        #endregion
    }
}
