using System.Collections.Generic;
using StellarNet.Lite.Game.Shared.Protocol;
using StellarNet.Lite.Server.Components;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;

namespace StellarNet.Lite.Game.Server.Components
{
    [RoomComponent(102, "SocialRoom", "简易交友房间")]
    public sealed class SocialServerRoomComponent : ServerRoomComponent, ITickableComponent
    {
        private readonly ServerApp _app;
        private ServerObjectSyncComponent _syncService;
        private readonly Dictionary<string, int> _sessionToNetId = new Dictionary<string, int>();
        private readonly Dictionary<int, float> _actionEndTimes = new Dictionary<int, float>();

        private const int PlayerPrefabHash = NetPrefabConsts.NetPrefabs_SocialPlayer;
        private static readonly int AnimHash_Idle = Animator.StringToHash("Idle");
        private static readonly int AnimHash_Walk = Animator.StringToHash("Walk");
        private static readonly int AnimHash_Wave = Animator.StringToHash("Wave");
        private static readonly int AnimHash_Dance = Animator.StringToHash("Dance");

        private const float PlayerMoveSpeed = 4.0f;

        public SocialServerRoomComponent(ServerApp app)
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
            foreach (var kvp in Room.Members) SpawnPlayerForSession(kvp.Value);
        }

        public override void OnMemberJoined(Session session)
        {
            if (Room.State == RoomState.Playing) SpawnPlayerForSession(session);
        }

        public override void OnMemberLeft(Session session)
        {
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
                foreach (var kvp in _sessionToNetId) _syncService.DestroyObject(kvp.Value);
            }

            _sessionToNetId.Clear();
            _actionEndTimes.Clear();
        }

        private void SpawnPlayerForSession(Session session)
        {
            if (_syncService == null || _sessionToNetId.ContainsKey(session.SessionId)) return;

            Vector2 randomCircle = Random.insideUnitCircle * 3f;
            Vector3 spawnPos = new Vector3(randomCircle.x, 0f, randomCircle.y);

            ServerSyncEntity syncEntity = _syncService.SpawnObject(
                PlayerPrefabHash, EntitySyncMask.All, spawnPos, Vector3.zero, Vector3.zero, session.SessionId);

            if (syncEntity != null)
            {
                syncEntity.AnimStateHash = AnimHash_Idle;
                syncEntity.AnimNormalizedTime = 0f;
                _sessionToNetId.Add(session.SessionId, syncEntity.NetId);
            }
        }

        public void OnTick()
        {
            if (Room.State != RoomState.Playing || _syncService == null) return;

            float deltaTime = 1f / _app.Config.TickRate;
            float currentTime = Time.realtimeSinceStartup;

            foreach (var kvp in _sessionToNetId)
            {
                ServerSyncEntity playerSync = _syncService.GetEntity(kvp.Value);
                if (playerSync == null) continue;

                // 修复1：恢复服务端的航位推测（Dead Reckoning），让广播出去的坐标无限接近客户端真实位置
                if (playerSync.Velocity.sqrMagnitude > 0.01f)
                {
                    playerSync.Position += playerSync.Velocity * deltaTime;
                }
                else
                {
                    if (playerSync.AnimStateHash == AnimHash_Wave || playerSync.AnimStateHash == AnimHash_Dance)
                    {
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
            if (Room.State != RoomState.Playing || _syncService == null) return;
            if (!_sessionToNetId.TryGetValue(session.SessionId, out int netId)) return;

            ServerSyncEntity playerSync = _syncService.GetEntity(netId);
            if (playerSync == null) return;

            Vector3 newPos = new Vector3(msg.PosX, msg.PosY, msg.PosZ);
            Vector3 newVel = new Vector3(msg.VelX, msg.VelY, msg.VelZ);

            // 标量校验：因为服务端恢复了积分，此时 playerSync.Position 是服务端预测的位置
            // 客户端发来的 newPos 是客户端的真实位置，两者距离应该极小。
            float distance = Vector3.Distance(playerSync.Position, newPos);

            // 允许的最大位移容差 (适当放宽以应对网络抖动)
            float maxAllowedDistance = PlayerMoveSpeed * 1.5f;

            if (distance > maxAllowedDistance && playerSync.Position != Vector3.zero)
            {
                NetLogger.LogWarning("ServerSocialRoomComponent", $"移动校验失败，疑似作弊或严重延迟。Distance:{distance}", Room.RoomId, session.SessionId);
                return;
            }

            // 校验通过：用客户端的绝对精确坐标覆盖服务端的预测坐标，消除累积误差
            playerSync.Position = newPos;
            playerSync.Velocity = newVel;
            playerSync.Rotation = new Vector3(0f, msg.RotY, 0f);

            if (newVel.sqrMagnitude > 0.01f)
            {
                if (playerSync.AnimStateHash != AnimHash_Walk)
                {
                    playerSync.AnimStateHash = AnimHash_Walk;
                    playerSync.AnimNormalizedTime = 0f;
                }
            }
            else
            {
                if (playerSync.AnimStateHash == AnimHash_Walk)
                {
                    playerSync.AnimStateHash = AnimHash_Idle;
                    playerSync.AnimNormalizedTime = 0f;
                }
            }
        }

        [NetHandler]
        public void OnC2S_SocialActionReq(Session session, C2S_SocialActionReq msg)
        {
            if (Room.State != RoomState.Playing || _syncService == null) return;
            if (!_sessionToNetId.TryGetValue(session.SessionId, out int netId)) return;

            ServerSyncEntity playerSync = _syncService.GetEntity(netId);
            if (playerSync == null) return;

            playerSync.Velocity = Vector3.zero;

            if (msg.ActionId == 1)
            {
                playerSync.AnimStateHash = AnimHash_Wave;
                playerSync.AnimNormalizedTime = 0f;
                _actionEndTimes[netId] = Time.realtimeSinceStartup + 2.5f;
            }
            else if (msg.ActionId == 2)
            {
                playerSync.AnimStateHash = AnimHash_Dance;
                playerSync.AnimNormalizedTime = 0f;
                _actionEndTimes[netId] = Time.realtimeSinceStartup + 4.0f;
            }
        }

        [NetHandler]
        public void OnC2S_SocialBubbleReq(Session session, C2S_SocialBubbleReq msg)
        {
            if (string.IsNullOrWhiteSpace(msg.Content) || Room.State != RoomState.Playing) return;
            if (!_sessionToNetId.TryGetValue(session.SessionId, out int netId)) return;

            string safeContent = msg.Content.Length > 30 ? msg.Content.Substring(0, 30) + "..." : msg.Content;
            Room.BroadcastMessage(new S2C_SocialBubbleSync { NetId = netId, Content = safeContent }, false);
        }
    }
}