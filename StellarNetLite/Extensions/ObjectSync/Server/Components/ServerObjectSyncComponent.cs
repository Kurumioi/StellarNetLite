using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.ObjectSync;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Replay;

namespace StellarNet.Lite.Server.Components
{
    /// <summary>
    /// 服务端同步实体。
    /// </summary>
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

    /// <summary>
    /// 服务端对象同步组件。
    /// 负责实体创建、销毁、周期同步和回放快照导出。
    /// </summary>
    [RoomComponent(200, "ObjectSync", "空间与动画同步核心服务")]
    public sealed class ServerObjectSyncComponent : ServerRoomComponent, ITickableComponent, IReplaySnapshotProvider
    {
        private readonly ServerApp _app;
        private readonly Dictionary<int, ServerSyncEntity> _entities = new Dictionary<int, ServerSyncEntity>();
        private int _netIdCounter = 0;

        // 无配置开发态默认优先保证在线观感，在线同步默认每 Tick 广播一次。
        private const int SyncIntervalTicks = 1;

        private ObjectSyncState[] _syncStateBuffer = new ObjectSyncState[64];
        private readonly S2C_ObjectSync _reusableSyncMsg = new S2C_ObjectSync();

        public int SnapshotComponentId => 200;

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

        public override void OnDestroy() => _entities.Clear();

        public override void OnSendSnapshot(Session session)
        {
            foreach (var kvp in _entities)
            {
                Room.SendMessageTo(session, new S2C_ObjectSpawn { State = BuildSpawnState(kvp.Value) }, false);
            }
        }

        public void OnTick()
        {
            if (Room.State != RoomState.Playing || _entities.Count == 0) return;

            bool shouldSyncOnline = Room.CurrentTick % SyncIntervalTicks == 0;
            int recordIntervalTicks = ResolveReplayRecordIntervalTicks();
            bool shouldRecord = recordIntervalTicks > 0 && Room.CurrentTick % recordIntervalTicks == 0;

            if (!shouldSyncOnline && !shouldRecord) return;

            if (_entities.Count > _syncStateBuffer.Length)
            {
                _syncStateBuffer = new ObjectSyncState[Mathf.NextPowerOfTwo(_entities.Count)];
                _reusableSyncMsg.States = _syncStateBuffer;
            }

            int index = 0;
            float currentServerTime = Room.CurrentRealtimeSinceStartup;

            foreach (var kvp in _entities)
            {
                ServerSyncEntity entity = kvp.Value;

                // 房主长期未发送房间业务包时，清空速度以避免滑行残留。
                if (!string.IsNullOrEmpty(entity.OwnerSessionId))
                {
                    Session owner = Room.GetMember(entity.OwnerSessionId);
                    if (owner != null && (currentServerTime - owner.LastRoomActiveRealtime) > 0.5f)
                    {
                        if (entity.Velocity.sqrMagnitude > 0.01f)
                        {
                            entity.Velocity = Vector3.zero;
                        }
                    }
                }

                _syncStateBuffer[index++] = new ObjectSyncState
                {
                    NetId = entity.NetId,
                    Mask = entity.Mask,
                    PosX = entity.Position.x, PosY = entity.Position.y, PosZ = entity.Position.z,
                    RotX = entity.Rotation.x, RotY = entity.Rotation.y, RotZ = entity.Rotation.z,
                    VelX = entity.Velocity.x, VelY = entity.Velocity.y, VelZ = entity.Velocity.z,
                    ScaleX = entity.Scale.x, ScaleY = entity.Scale.y, ScaleZ = entity.Scale.z,
                    AnimStateHash = entity.AnimStateHash,
                    AnimNormalizedTime = entity.AnimNormalizedTime,
                    FloatParam1 = entity.FloatParam1, FloatParam2 = entity.FloatParam2, FloatParam3 = entity.FloatParam3,
                    ServerTime = currentServerTime
                };
            }

            _reusableSyncMsg.ValidCount = index;
            Room.BroadcastMessage(_reusableSyncMsg, shouldRecord);
        }

        private int ResolveReplayRecordIntervalTicks()
        {
            if (_app == null || _app.Config == null)
            {
                return 3;
            }

            return _app.Config.ReplayObjectSyncRecordIntervalTicks;
        }

        public override void OnMemberJoined(Session session)
        {
            base.OnMemberJoined(session);
            if (Room.State == RoomState.Playing) OnSendSnapshot(session);
        }

        public ServerSyncEntity SpawnObject(int prefabHash, EntitySyncMask mask, Vector3 position, Vector3 rotation, Vector3 velocity,
            string ownerSessionId = "")
        {
            if (prefabHash == 0) return null;
            _netIdCounter++;
            var entity = new ServerSyncEntity
            {
                NetId = _netIdCounter,
                PrefabHash = prefabHash,
                Mask = (byte)mask,
                Position = position,
                Rotation = rotation,
                Velocity = velocity,
                Scale = Vector3.one,
                OwnerSessionId = ownerSessionId ?? string.Empty
            };
            _entities.Add(entity.NetId, entity);
            Room.BroadcastMessage(new S2C_ObjectSpawn { State = BuildSpawnState(entity) }, true);
            return entity;
        }

        public void DestroyObject(int netId)
        {
            if (_entities.Remove(netId))
            {
                Room.BroadcastMessage(new S2C_ObjectDestroy { NetId = netId }, true);
            }
        }

        public ServerSyncEntity GetEntity(int netId)
        {
            _entities.TryGetValue(netId, out ServerSyncEntity entity);
            return entity;
        }

        public ObjectSpawnState[] ExportSpawnStates()
        {
            if (_entities.Count == 0) return Array.Empty<ObjectSpawnState>();
            var result = new ObjectSpawnState[_entities.Count];
            int index = 0;
            foreach (var kvp in _entities) result[index++] = BuildSpawnState(kvp.Value);
            return result;
        }

        public byte[] ExportSnapshot()
        {
            ObjectSpawnState[] states = ExportSpawnStates();
            if (states == null || states.Length == 0) return Array.Empty<byte>();

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                writer.Write(states.Length);
                for (int i = 0; i < states.Length; i++)
                {
                    states[i].Serialize(writer);
                }
                return ms.ToArray();
            }
        }

        private ObjectSpawnState BuildSpawnState(ServerSyncEntity entity)
        {
            return new ObjectSpawnState
            {
                NetId = entity.NetId,
                PrefabHash = entity.PrefabHash,
                Mask = entity.Mask,
                PosX = entity.Position.x, PosY = entity.Position.y, PosZ = entity.Position.z,
                RotX = entity.Rotation.x, RotY = entity.Rotation.y, RotZ = entity.Rotation.z,
                DirX = entity.Velocity.x, DirY = entity.Velocity.y, DirZ = entity.Velocity.z,
                ScaleX = Mathf.Approximately(entity.Scale.x, 0f) ? 1f : entity.Scale.x,
                ScaleY = Mathf.Approximately(entity.Scale.y, 0f) ? 1f : entity.Scale.y,
                ScaleZ = Mathf.Approximately(entity.Scale.z, 0f) ? 1f : entity.Scale.z,
                AnimStateHash = entity.AnimStateHash,
                AnimNormalizedTime = entity.AnimNormalizedTime,
                FloatParam1 = entity.FloatParam1, FloatParam2 = entity.FloatParam2, FloatParam3 = entity.FloatParam3,
                OwnerSessionId = entity.OwnerSessionId ?? string.Empty
            };
        }
    }
}
