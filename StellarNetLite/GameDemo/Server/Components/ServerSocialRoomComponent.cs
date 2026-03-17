using System;
using System.Collections.Generic;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Game.Shared.Protocol;
using StellarNet.Lite.Server.Components;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Game.Server.Components
{
    /// <summary>
    /// 服务端交友房间核心组件
    /// 职责：管理玩家实体的生成与销毁、处理移动与社交动作、广播聊天气泡。
    /// 架构特点：支持中途加入(Drop-in)，完全依赖 ObjectSync 进行状态下发。
    /// </summary>
    [RoomComponent(102, "SocialRoom", "简易交友房间")]
    public sealed class ServerSocialRoomComponent : RoomComponent
    {
        private readonly ServerApp _app;
        private ServerObjectSyncComponent _syncService;

        // 映射关系：SessionId -> 场景中的 NetId
        private readonly Dictionary<string, int> _sessionToNetId = new Dictionary<string, int>();

        // 预制体 Hash
        private const int PlayerPrefabHash = NetPrefabConsts.NetPrefabs_SocialPlayer;

        // 定义与 Unity Animator Controller 严格对应的状态 Hash
        private static readonly int AnimHash_Idle = Animator.StringToHash("Idle");
        private static readonly int AnimHash_Walk = Animator.StringToHash("Walk");
        private static readonly int AnimHash_Wave = Animator.StringToHash("Wave");
        private static readonly int AnimHash_Dance = Animator.StringToHash("Dance");

        // 玩家基础移动速度
        private const float PlayerMoveSpeed = 4.0f;

        public ServerSocialRoomComponent(ServerApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            _sessionToNetId.Clear();
            _syncService = Room.GetComponent<ServerObjectSyncComponent>();

            if (_syncService == null)
            {
                NetLogger.LogError("[ServerSocialRoom]", "初始化失败: 房间缺失 ServerObjectSyncComponent，无法进行实体同步", Room.RoomId);
            }
        }

        public override void OnGameStart()
        {
            if (_syncService == null) return;
            _sessionToNetId.Clear();

            // 游戏开始时，为房间内所有已准备的玩家生成实体
            foreach (var kvp in Room.Members)
            {
                SpawnPlayerForSession(kvp.Value);
            }

            NetLogger.LogInfo("[ServerSocialRoom]", $"交友房间已启动，已为 {Room.MemberCount} 名玩家生成虚拟形象", Room.RoomId);
        }

        public override void OnMemberJoined(Session session)
        {
            if (session == null) return;

            // 支持中途加入：如果房间已经在运行中，新玩家加入直接生成实体
            if (Room.State == RoomState.Playing)
            {
                SpawnPlayerForSession(session);
                NetLogger.LogInfo("[ServerSocialRoom]", $"玩家中途加入，已为其生成虚拟形象", Room.RoomId, session.SessionId);
            }
        }

        public override void OnMemberLeft(Session session)
        {
            if (session == null) return;

            // 玩家离开时，销毁其对应的物理实体
            if (_sessionToNetId.TryGetValue(session.SessionId, out int netId))
            {
                _syncService?.DestroyObject(netId);
                _sessionToNetId.Remove(session.SessionId);
                NetLogger.LogInfo("[ServerSocialRoom]", $"玩家离开，已销毁其虚拟形象", Room.RoomId, session.SessionId);
            }
        }

        public override void OnGameEnd()
        {
            // 核心修复：游戏结束时，必须主动调用底层服务销毁所有实体，触发 S2C_ObjectDestroy 广播
            if (_syncService != null)
            {
                foreach (var kvp in _sessionToNetId)
                {
                    _syncService.DestroyObject(kvp.Value);
                }
            }

            _sessionToNetId.Clear();
            NetLogger.LogInfo("[ServerSocialRoom]", $"交友房间已结束，已清理所有虚拟形象", Room.RoomId);
        }

        private void SpawnPlayerForSession(Session session)
        {
            if (_sessionToNetId.ContainsKey(session.SessionId)) return;

            // 随机一个出生点 (围绕原点半径 3 米的圆内)
            Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * 3f;
            Vector3 spawnPos = new Vector3(randomCircle.x, 0, randomCircle.y);

            var syncEntity = _syncService.SpawnObject(PlayerPrefabHash, spawnPos, Vector3.zero, session.SessionId);
            syncEntity.AnimStateHash = AnimHash_Idle;

            _sessionToNetId.Add(session.SessionId, syncEntity.NetId);
        }

        public override void OnTick()
        {
            if (Room.State != RoomState.Playing || _syncService == null) return;

            float deltaTime = 1f / _app.Config.TickRate;

            // 遍历所有玩家实体，根据当前的速度 (Velocity) 更新位置
            foreach (var kvp in _sessionToNetId)
            {
                var playerSync = _syncService.GetEntity(kvp.Value);
                if (playerSync == null) continue;

                if (playerSync.Velocity.sqrMagnitude > 0.01f)
                {
                    // 积分计算新位置
                    playerSync.Position += playerSync.Velocity * deltaTime;

                    // 如果正在移动，且当前不是特殊的社交动作，则切为走路动画
                    if (playerSync.AnimStateHash != AnimHash_Wave && playerSync.AnimStateHash != AnimHash_Dance)
                    {
                        playerSync.AnimStateHash = AnimHash_Walk;
                    }
                }
                else
                {
                    // 停止移动时，如果当前是走路状态，则切回待机
                    if (playerSync.AnimStateHash == AnimHash_Walk)
                    {
                        playerSync.AnimStateHash = AnimHash_Idle;
                    }
                }
            }
        }

        [NetHandler]
        public void OnC2S_SocialMoveReq(Session session, C2S_SocialMoveReq msg)
        {
            if (session == null || msg == null) return;
            if (Room.State != RoomState.Playing || _syncService == null) return;

            if (!_sessionToNetId.TryGetValue(session.SessionId, out int netId)) return;
            var playerSync = _syncService.GetEntity(netId);
            if (playerSync == null) return;

            Vector3 inputDir = new Vector3(msg.DirX, 0, msg.DirZ);
            if (inputDir.sqrMagnitude > 1f) inputDir.Normalize();

            // 更新权威速度，位置的推演交由 OnTick 处理
            playerSync.Velocity = inputDir * PlayerMoveSpeed;

            // 如果玩家开始移动，强制打断当前的社交动作 (如跳舞)
            if (inputDir.sqrMagnitude > 0.01f)
            {
                if (playerSync.AnimStateHash == AnimHash_Wave || playerSync.AnimStateHash == AnimHash_Dance)
                {
                    playerSync.AnimStateHash = AnimHash_Walk;
                }
            }
        }

        [NetHandler]
        public void OnC2S_SocialActionReq(Session session, C2S_SocialActionReq msg)
        {
            if (session == null || msg == null) return;
            if (Room.State != RoomState.Playing || _syncService == null) return;

            if (!_sessionToNetId.TryGetValue(session.SessionId, out int netId)) return;
            var playerSync = _syncService.GetEntity(netId);
            if (playerSync == null) return;

            // 停止移动
            playerSync.Velocity = Vector3.zero;

            // 触发对应的社交动作动画
            if (msg.ActionId == 1)
            {
                playerSync.AnimStateHash = AnimHash_Wave;
                playerSync.AnimNormalizedTime = 0f; // 从头开始播
            }
            else if (msg.ActionId == 2)
            {
                playerSync.AnimStateHash = AnimHash_Dance;
                playerSync.AnimNormalizedTime = 0f;
            }
        }

        [NetHandler]
        public void OnC2S_SocialBubbleReq(Session session, C2S_SocialBubbleReq msg)
        {
            if (session == null || msg == null || string.IsNullOrEmpty(msg.Content)) return;
            if (Room.State != RoomState.Playing) return;

            if (!_sessionToNetId.TryGetValue(session.SessionId, out int netId)) return;

            // 限制气泡长度防恶意刷屏
            string safeContent = msg.Content.Length > 30 ? msg.Content.Substring(0, 30) + "..." : msg.Content;

            var syncMsg = new S2C_SocialBubbleSync
            {
                NetId = netId,
                Content = safeContent
            };

            // 广播给房间内所有人（包括自己），气泡属于表现层事件，不强制录入 Replay
            Room.BroadcastMessage(syncMsg, false);
        }
    }
}