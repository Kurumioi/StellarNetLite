using System;
using System.Collections.Generic;
using StellarNet.Lite.Game.Shared.Protocol;
using StellarNet.Lite.Server.Components;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.ObjectSync;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;
using Random = System.Random;

namespace StellarNet.Lite.Game.Server.Components
{
    /// <summary>
    /// Demo 社交房间服务端组件。
    /// 当前采用：
    /// 1. 客户端本地产生真实移动/动作结果
    /// 2. 服务端做轻量合法性校验
    /// 3. 服务端立刻转发最新状态给房间成员
    /// 4. 周期 ObjectSync 仍负责兜底校正与回放录制
    /// </summary>
    [RoomComponent(102, "SocialRoom", "简易交友房间")]
    public sealed class ServerSocialRoomComponent : ServerRoomComponent, ITickableComponent
    {
        private readonly ServerApp _app;
        private ServerObjectSyncComponent _syncService;
        private readonly Dictionary<string, int> _sessionToNetId = new Dictionary<string, int>();
        private readonly Dictionary<int, float> _actionEndTimes = new Dictionary<int, float>();
        private readonly Random _random = new Random(Guid.NewGuid().GetHashCode());

        private const int PlayerPrefabHash = NetPrefabConsts.NetPrefabs_SocialPlayer;
        private static readonly int AnimHash_Idle = ObjectSyncAnimHashUtility.GetStableStringHash("Idle");
        private static readonly int AnimHash_Walk = ObjectSyncAnimHashUtility.GetStableStringHash("Walk");
        private static readonly int AnimHash_Wave = ObjectSyncAnimHashUtility.GetStableStringHash("Wave");
        private static readonly int AnimHash_Dance = ObjectSyncAnimHashUtility.GetStableStringHash("Dance");

        private const float PlayerMoveSpeed = 4.0f;
        private const float WaveDurationSeconds = 2.5f;
        private const float DanceDurationSeconds = 4.0f;

        public ServerSocialRoomComponent(ServerApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            _sessionToNetId.Clear();
            _actionEndTimes.Clear();
            _syncService = Room.GetComponent<ServerObjectSyncComponent>();
            if (_syncService == null)
            {
                NetLogger.LogError("ServerSocialRoomComponent", "初始化失败: 缺少 ServerObjectSyncComponent", Room.RoomId);
            }
        }

        public override void OnGameStart()
        {
            if (_syncService == null)
            {
                NetLogger.LogError("ServerSocialRoomComponent", "开局生成跳过: _syncService 为空", Room.RoomId);
                return;
            }

            if (PlayerPrefabHash == 0)
            {
                NetLogger.LogError("ServerSocialRoomComponent", "开局生成失败: PlayerPrefabHash 为 0，请检查 NetPrefabConsts 是否已重新生成", Room.RoomId);
                return;
            }

            _sessionToNetId.Clear();
            _actionEndTimes.Clear();
            foreach (KeyValuePair<string, Session> kvp in Room.Members)
            {
                SpawnPlayerForSession(kvp.Value);
            }
        }

        public override void OnMemberJoined(Session session)
        {
            if (Room.State == RoomState.Playing)
            {
                SpawnPlayerForSession(session);
            }
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
                foreach (KeyValuePair<string, int> kvp in _sessionToNetId)
                {
                    _syncService.DestroyObject(kvp.Value);
                }
            }

            _sessionToNetId.Clear();
            _actionEndTimes.Clear();
        }

        private void SpawnPlayerForSession(Session session)
        {
            if (_syncService == null || session == null || _sessionToNetId.ContainsKey(session.SessionId))
            {
                return;
            }

            if (PlayerPrefabHash == 0)
            {
                NetLogger.LogError("ServerSocialRoomComponent", $"角色生成失败: PlayerPrefabHash 为 0, SessionId:{session.SessionId}", Room.RoomId, session.SessionId);
                return;
            }

            Vector3 spawnPos = GetRandomSpawnPosition(3f);
            ServerSyncEntity syncEntity = _syncService.SpawnObject(
                PlayerPrefabHash,
                EntitySyncMask.All,
                spawnPos,
                Vector3.zero,
                Vector3.zero,
                session.SessionId);

            if (syncEntity == null)
            {
                NetLogger.LogError("ServerSocialRoomComponent", $"角色生成失败: SpawnObject 返回 null, PrefabHash:{PlayerPrefabHash}", Room.RoomId, session.SessionId);
                return;
            }

            syncEntity.SetAnimState("Idle");
            _sessionToNetId.Add(session.SessionId, syncEntity.NetId);
        }

        private Vector3 GetRandomSpawnPosition(float radius)
        {
            double angle = _random.NextDouble() * Math.PI * 2.0d;
            double distance = Math.Sqrt(_random.NextDouble()) * radius;
            float x = (float)(Math.Cos(angle) * distance);
            float z = (float)(Math.Sin(angle) * distance);
            return new Vector3(x, 0f, z);
        }

        public void OnTick()
        {
            if (Room.State != RoomState.Playing || _syncService == null)
            {
                return;
            }

            float deltaTime = 1f / _app.Config.TickRate;
            float currentTime = Room.CurrentRealtimeSinceStartup;

            foreach (KeyValuePair<string, int> kvp in _sessionToNetId)
            {
                ServerSyncEntity playerSync = _syncService.GetEntity(kvp.Value);
                if (playerSync == null)
                {
                    continue;
                }

                if (playerSync.Velocity.sqrMagnitude > 0.01f)
                {
                    playerSync.Position += playerSync.Velocity * deltaTime;
                }
                else if ((playerSync.AnimStateHash == AnimHash_Wave || playerSync.AnimStateHash == AnimHash_Dance) &&
                         _actionEndTimes.TryGetValue(playerSync.NetId, out float endTime) &&
                         currentTime > endTime)
                {
                    _actionEndTimes.Remove(playerSync.NetId);
                    playerSync.SetAnimState("Idle");
                    BroadcastLiveState(playerSync);
                }
            }
        }

        [NetHandler]
        public void OnC2S_SocialMoveReq(Session session, C2S_SocialMoveReq msg)
        {
            if (Room.State != RoomState.Playing || _syncService == null || session == null)
            {
                return;
            }

            if (!_sessionToNetId.TryGetValue(session.SessionId, out int netId))
            {
                return;
            }

            ServerSyncEntity playerSync = _syncService.GetEntity(netId);
            if (playerSync == null)
            {
                return;
            }

            Vector3 newPos = new Vector3(msg.PosX, msg.PosY, msg.PosZ);
            Vector3 newVel = new Vector3(msg.VelX, msg.VelY, msg.VelZ);
            float distance = Vector3.Distance(playerSync.Position, newPos);
            float maxAllowedDistance = PlayerMoveSpeed * 1.5f;

            if (distance > maxAllowedDistance && playerSync.Position != Vector3.zero)
            {
                NetLogger.LogWarning("ServerSocialRoomComponent", $"移动校验失败，疑似作弊或严重延迟。Distance:{distance}", Room.RoomId, session.SessionId);
                return;
            }

            playerSync.Position = newPos;
            playerSync.Velocity = newVel;
            playerSync.Rotation = new Vector3(0f, msg.RotY, 0f);

            if (newVel.sqrMagnitude > 0.01f)
            {
                _actionEndTimes.Remove(netId);
                if (playerSync.AnimStateHash != AnimHash_Walk)
                {
                    playerSync.SetAnimState("Walk");
                }
            }
            else if (playerSync.AnimStateHash == AnimHash_Walk)
            {
                playerSync.SetAnimState("Idle");
            }

            BroadcastLiveState(playerSync);
        }

        [NetHandler]
        public void OnC2S_SocialActionReq(Session session, C2S_SocialActionReq msg)
        {
            if (Room.State != RoomState.Playing || _syncService == null || session == null)
            {
                return;
            }

            if (!_sessionToNetId.TryGetValue(session.SessionId, out int netId))
            {
                return;
            }

            ServerSyncEntity playerSync = _syncService.GetEntity(netId);
            if (playerSync == null)
            {
                return;
            }

            playerSync.Velocity = Vector3.zero;

            if (msg.ActionId == 1)
            {
                playerSync.SetAnimState("Wave");
                _actionEndTimes[netId] = Room.CurrentRealtimeSinceStartup + WaveDurationSeconds;
                BroadcastLiveState(playerSync);
            }
            else if (msg.ActionId == 2)
            {
                playerSync.SetAnimState("Dance");
                _actionEndTimes[netId] = Room.CurrentRealtimeSinceStartup + DanceDurationSeconds;
                BroadcastLiveState(playerSync);
            }
        }

        [NetHandler]
        public void OnC2S_SocialBubbleReq(Session session, C2S_SocialBubbleReq msg)
        {
            if (string.IsNullOrWhiteSpace(msg.Content) || Room.State != RoomState.Playing || session == null)
            {
                return;
            }

            if (!_sessionToNetId.TryGetValue(session.SessionId, out int netId))
            {
                return;
            }

            string safeContent = msg.Content.Length > 30 ? msg.Content.Substring(0, 30) + "..." : msg.Content;
            Room.BroadcastMessage(new S2C_SocialBubbleSync { NetId = netId, Content = safeContent }, false);
        }

        private void BroadcastLiveState(ServerSyncEntity entity)
        {
            if (entity == null)
            {
                return;
            }

            Room.BroadcastMessage(new S2C_SocialStateSync
            {
                ServerTime = Room.CurrentRealtimeSinceStartup,
                State = BuildLiveState(entity)
            }, true);
        }

        private static ObjectSyncState BuildLiveState(ServerSyncEntity entity)
        {
            return new ObjectSyncState
            {
                NetId = entity.NetId,
                Mask = entity.Mask,
                DirtyMask = BuildFullDirtyMask(entity.Mask),
                PosX = entity.Position.x,
                PosY = entity.Position.y,
                PosZ = entity.Position.z,
                RotX = entity.Rotation.x,
                RotY = entity.Rotation.y,
                RotZ = entity.Rotation.z,
                VelX = entity.Velocity.x,
                VelY = entity.Velocity.y,
                VelZ = entity.Velocity.z,
                ScaleX = Mathf.Approximately(entity.Scale.x, 0f) ? 1f : entity.Scale.x,
                ScaleY = Mathf.Approximately(entity.Scale.y, 0f) ? 1f : entity.Scale.y,
                ScaleZ = Mathf.Approximately(entity.Scale.z, 0f) ? 1f : entity.Scale.z,
                AnimStateHash = entity.AnimStateHash,
                AnimNormalizedTime = entity.AnimNormalizedTime,
                AnimParamCount = entity.AnimParamCount,
                AnimParams = entity.AnimParamCount > 0 ? CloneAnimParams(entity.AnimParams, entity.AnimParamCount) : Array.Empty<AnimatorParamValue>()
            };
        }

        private static ushort BuildFullDirtyMask(byte entityMask)
        {
            ObjectSyncDirtyMask dirtyMask = ObjectSyncDirtyMask.None;
            if ((entityMask & (byte)EntitySyncMask.Transform) != 0)
            {
                dirtyMask |= ObjectSyncDirtyMask.AllTransform;
            }

            if ((entityMask & (byte)EntitySyncMask.Animator) != 0)
            {
                dirtyMask |= ObjectSyncDirtyMask.AllAnimator;
            }

            return (ushort)dirtyMask;
        }

        private static AnimatorParamValue[] CloneAnimParams(AnimatorParamValue[] source, int count)
        {
            if (count <= 0 || source == null || source.Length <= 0)
            {
                return Array.Empty<AnimatorParamValue>();
            }

            AnimatorParamValue[] cloned = new AnimatorParamValue[count];
            Array.Copy(source, cloned, count);
            return cloned;
        }
    }
}
