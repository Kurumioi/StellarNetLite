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
    [RoomComponent(102, "SocialRoom", "简易交友房间")]
    public sealed class ServerSocialRoomComponent : RoomComponent
    {
        private readonly ServerApp _app;
        private ServerObjectSyncComponent _syncService;

        private readonly Dictionary<string, int> _sessionToNetId = new Dictionary<string, int>();

        // 核心修复 1：记录每个实体动作的结束时间，用于自动切回 Idle
        private readonly Dictionary<int, float> _actionEndTimes = new Dictionary<int, float>();

        private const int PlayerPrefabHash = NetPrefabConsts.NetPrefabs_SocialPlayer;

        private static readonly int AnimHash_Idle = Animator.StringToHash("Idle");
        private static readonly int AnimHash_Walk = Animator.StringToHash("Walk");
        private static readonly int AnimHash_Wave = Animator.StringToHash("Wave");
        private static readonly int AnimHash_Dance = Animator.StringToHash("Dance");

        private const float PlayerMoveSpeed = 4.0f;

        public ServerSocialRoomComponent(ServerApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            _sessionToNetId.Clear();
            _actionEndTimes.Clear();
            _syncService = Room.GetComponent<ServerObjectSyncComponent>();
        }

        public override void OnGameStart()
        {
            if (_syncService == null) return;
            _sessionToNetId.Clear();
            _actionEndTimes.Clear();

            foreach (var kvp in Room.Members)
            {
                SpawnPlayerForSession(kvp.Value);
            }
        }

        public override void OnMemberJoined(Session session)
        {
            if (session == null) return;
            if (Room.State == RoomState.Playing)
            {
                SpawnPlayerForSession(session);
            }
        }

        public override void OnMemberLeft(Session session)
        {
            if (session == null) return;
            if (_sessionToNetId.TryGetValue(session.SessionId, out int netId))
            {
                _syncService?.DestroyObject(netId);
                _sessionToNetId.Remove(session.SessionId);
                _actionEndTimes.Remove(netId);
            }
        }

        public override void OnGameEnd()
        {
            if (_syncService != null)
            {
                foreach (var kvp in _sessionToNetId)
                {
                    _syncService.DestroyObject(kvp.Value);
                }
            }

            _sessionToNetId.Clear();
            _actionEndTimes.Clear();
        }

        private void SpawnPlayerForSession(Session session)
        {
            if (_sessionToNetId.ContainsKey(session.SessionId)) return;

            Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * 3f;
            Vector3 spawnPos = new Vector3(randomCircle.x, 0, randomCircle.y);

            var syncEntity = _syncService.SpawnObject(PlayerPrefabHash, spawnPos, Vector3.zero, Vector3.zero, session.SessionId);
            syncEntity.AnimStateHash = AnimHash_Idle;
            syncEntity.AnimNormalizedTime = 0f;

            _sessionToNetId.Add(session.SessionId, syncEntity.NetId);
        }

        public override void OnTick()
        {
            if (Room.State != RoomState.Playing || _syncService == null) return;

            float deltaTime = 1f / _app.Config.TickRate;
            float currentTime = Time.realtimeSinceStartup;

            foreach (var kvp in _sessionToNetId)
            {
                var playerSync = _syncService.GetEntity(kvp.Value);
                if (playerSync == null) continue;

                if (playerSync.Velocity.sqrMagnitude > 0.01f)
                {
                    playerSync.Position += playerSync.Velocity * deltaTime;

                    // 只要在移动，强制打断任何动作，切为 Walk
                    if (playerSync.AnimStateHash != AnimHash_Walk)
                    {
                        playerSync.AnimStateHash = AnimHash_Walk;
                        playerSync.AnimNormalizedTime = 0f;
                    }
                }
                else
                {
                    // 核心修复 2：停止移动时的状态机流转
                    if (playerSync.AnimStateHash == AnimHash_Walk)
                    {
                        // 从走路停下，立刻切回 Idle
                        playerSync.AnimStateHash = AnimHash_Idle;
                        playerSync.AnimNormalizedTime = 0f;
                    }
                    else if (playerSync.AnimStateHash == AnimHash_Wave || playerSync.AnimStateHash == AnimHash_Dance)
                    {
                        // 如果正在播动作，检查是否达到动作时长，超时则自动切回 Idle
                        if (_actionEndTimes.TryGetValue(playerSync.NetId, out float endTime) && currentTime > endTime)
                        {
                            playerSync.AnimStateHash = AnimHash_Idle;
                            playerSync.AnimNormalizedTime = 0f;
                        }
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

            playerSync.Velocity = inputDir * PlayerMoveSpeed;

            if (inputDir.sqrMagnitude > 0.01f)
            {
                playerSync.Rotation = Quaternion.LookRotation(inputDir).eulerAngles;
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

            playerSync.Velocity = Vector3.zero;

            if (msg.ActionId == 1)
            {
                playerSync.AnimStateHash = AnimHash_Wave;
                playerSync.AnimNormalizedTime = 0f;
                // 假设挥手动作长 2.5 秒
                _actionEndTimes[netId] = Time.realtimeSinceStartup + 2.5f;
            }
            else if (msg.ActionId == 2)
            {
                playerSync.AnimStateHash = AnimHash_Dance;
                playerSync.AnimNormalizedTime = 0f;
                // 假设跳舞动作长 4.0 秒
                _actionEndTimes[netId] = Time.realtimeSinceStartup + 4.0f;
            }
        }

        [NetHandler]
        public void OnC2S_SocialBubbleReq(Session session, C2S_SocialBubbleReq msg)
        {
            if (session == null || msg == null || string.IsNullOrEmpty(msg.Content)) return;
            if (Room.State != RoomState.Playing) return;
            if (!_sessionToNetId.TryGetValue(session.SessionId, out int netId)) return;

            string safeContent = msg.Content.Length > 30 ? msg.Content.Substring(0, 30) + "..." : msg.Content;

            var syncMsg = new S2C_SocialBubbleSync { NetId = netId, Content = safeContent };
            Room.BroadcastMessage(syncMsg, false);
        }
    }
}