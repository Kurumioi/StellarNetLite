using System.Collections.Generic;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Components
{
    public class ServerSyncEntity
    {
        public int NetId;
        public int PrefabHash;
        public byte Mask;
        public string OwnerSessionId;
        public Vector3 Position;
        public Vector3 Rotation;
        public Vector3 Velocity;
        public Vector3 Scale = Vector3.one;
        public int AnimStateHash;
        public float AnimNormalizedTime;
        public float FloatParam1;
        public float FloatParam2;
        public float FloatParam3;
    }

    [RoomComponent(200, "ObjectSync", "空间与动画同步核心服务")]
    public sealed class ServerObjectSyncComponent : RoomComponent, ITickableComponent
    {
        private readonly ServerApp _app;
        private readonly Dictionary<int, ServerSyncEntity> _entities = new Dictionary<int, ServerSyncEntity>();
        private int _netIdCounter = 0;

        private const int SyncIntervalTicks = 3;
        private const int RecordIntervalTicks = 3;

        private ObjectSyncState[] _syncStateBuffer = new ObjectSyncState[64];
        private readonly S2C_ObjectSync _reusableSyncMsg = new S2C_ObjectSync();

        public ServerObjectSyncComponent(ServerApp app)
        {
            _app = app;
            _reusableSyncMsg.States = _syncStateBuffer;
        }

        public override void OnInit()
        {
            _entities.Clear();
            _netIdCounter = 0;
        }

        public override void OnDestroy()
        {
            _entities.Clear();
        }

        public override void OnSendSnapshot(Session session)
        {
            if (session == null) return;

            foreach (var kvp in _entities)
            {
                var entity = kvp.Value;
                var spawnMsg = new S2C_ObjectSpawn
                {
                    NetId = entity.NetId,
                    PrefabHash = entity.PrefabHash,
                    Mask = entity.Mask,
                    PosX = entity.Position.x,
                    PosY = entity.Position.y,
                    PosZ = entity.Position.z,
                    RotX = entity.Rotation.x,
                    RotY = entity.Rotation.y,
                    RotZ = entity.Rotation.z,
                    DirX = entity.Velocity.x,
                    DirY = entity.Velocity.y,
                    DirZ = entity.Velocity.z,
                    ScaleX = entity.Scale.x,
                    ScaleY = entity.Scale.y,
                    ScaleZ = entity.Scale.z,
                    AnimStateHash = entity.AnimStateHash,
                    AnimNormalizedTime = entity.AnimNormalizedTime,
                    FloatParam1 = entity.FloatParam1,
                    FloatParam2 = entity.FloatParam2,
                    FloatParam3 = entity.FloatParam3,
                    OwnerSessionId = entity.OwnerSessionId
                };
                Room.SendMessageTo(session, spawnMsg, false);
            }
        }

        public void OnTick()
        {
            if (Room.State != RoomState.Playing || _entities.Count == 0) return;

            bool shouldSyncOnline = (Room.CurrentTick % SyncIntervalTicks == 0);
            bool shouldRecord = (Room.CurrentTick % RecordIntervalTicks == 0);

            if (!shouldSyncOnline && !shouldRecord) return;

            if (_entities.Count > _syncStateBuffer.Length)
            {
                int newSize = Mathf.NextPowerOfTwo(_entities.Count);
                _syncStateBuffer = new ObjectSyncState[newSize];
                _reusableSyncMsg.States = _syncStateBuffer;
            }

            int index = 0;
            float currentServerTime = Time.realtimeSinceStartup;

            foreach (var kvp in _entities)
            {
                var entity = kvp.Value;
                _syncStateBuffer[index] = new ObjectSyncState
                {
                    NetId = entity.NetId,
                    Mask = entity.Mask,
                    PosX = entity.Position.x,
                    PosY = entity.Position.y,
                    PosZ = entity.Position.z,
                    RotX = entity.Rotation.x,
                    RotY = entity.Rotation.y,
                    RotZ = entity.Rotation.z,
                    VelX = entity.Velocity.x,
                    VelY = entity.Velocity.y,
                    VelZ = entity.Velocity.z,
                    ScaleX = entity.Scale.x,
                    ScaleY = entity.Scale.y,
                    ScaleZ = entity.Scale.z,
                    AnimStateHash = entity.AnimStateHash,
                    AnimNormalizedTime = entity.AnimNormalizedTime,
                    FloatParam1 = entity.FloatParam1,
                    FloatParam2 = entity.FloatParam2,
                    FloatParam3 = entity.FloatParam3,
                    ServerTime = currentServerTime
                };
                index++;
            }

            _reusableSyncMsg.ValidCount = index;
            Room.BroadcastMessage(_reusableSyncMsg, shouldRecord);
        }

        #region ================= 权威业务 API =================

        public ServerSyncEntity SpawnObject(int prefabHash, EntitySyncMask mask, Vector3 position, Vector3 rotation, Vector3 velocity, string ownerSessionId = "")
        {
            _netIdCounter++;
            var entity = new ServerSyncEntity
            {
                NetId = _netIdCounter,
                PrefabHash = prefabHash,
                Mask = (byte)mask,
                Position = position,
                Rotation = rotation,
                Velocity = velocity,
                OwnerSessionId = ownerSessionId ?? string.Empty
            };
            _entities.Add(entity.NetId, entity);

            var spawnMsg = new S2C_ObjectSpawn
            {
                NetId = entity.NetId,
                PrefabHash = entity.PrefabHash,
                Mask = entity.Mask,
                PosX = entity.Position.x,
                PosY = entity.Position.y,
                PosZ = entity.Position.z,
                RotX = entity.Rotation.x,
                RotY = entity.Rotation.y,
                RotZ = entity.Rotation.z,
                DirX = entity.Velocity.x,
                DirY = entity.Velocity.y,
                DirZ = entity.Velocity.z,
                ScaleX = entity.Scale.x,
                ScaleY = entity.Scale.y,
                ScaleZ = entity.Scale.z,
                AnimStateHash = entity.AnimStateHash,
                AnimNormalizedTime = entity.AnimNormalizedTime,
                FloatParam1 = entity.FloatParam1,
                FloatParam2 = entity.FloatParam2,
                FloatParam3 = entity.FloatParam3,
                OwnerSessionId = entity.OwnerSessionId
            };
            Room.BroadcastMessage(spawnMsg, true);
            return entity;
        }

        public void DestroyObject(int netId)
        {
            if (_entities.Remove(netId))
            {
                var destroyMsg = new S2C_ObjectDestroy { NetId = netId };
                Room.BroadcastMessage(destroyMsg, true);
            }
        }

        public ServerSyncEntity GetEntity(int netId)
        {
            _entities.TryGetValue(netId, out var entity);
            return entity;
        }

        #endregion
    }
}