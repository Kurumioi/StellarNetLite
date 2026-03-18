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
        public string OwnerSessionId;
        public Vector3 Position;
        public Vector3 Velocity;
        public Vector3 Scale = Vector3.one;
        public int AnimStateHash;
        public float AnimNormalizedTime;
    }

    [RoomComponent(200, "ObjectSync", "空间与动画同步核心服务")]
    public sealed class ServerObjectSyncComponent : RoomComponent
    {
        private readonly ServerApp _app;
        private readonly Dictionary<int, ServerSyncEntity> _entities = new Dictionary<int, ServerSyncEntity>();
        private int _netIdCounter = 0;

        private const int SyncIntervalTicks = 3;
        private const int RecordIntervalTicks = 3;

        // 核心修复 P1-4：真·0GC 数组复用
        private ObjectSyncState[] _syncStateBuffer = new ObjectSyncState[64];

        // 预先 new 好的消息对象，每帧只修改它的字段，绝不重新 new
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
                    PosX = entity.Position.x,
                    PosY = entity.Position.y,
                    PosZ = entity.Position.z,
                    DirX = entity.Velocity.x,
                    DirY = entity.Velocity.y,
                    DirZ = entity.Velocity.z,
                    ScaleX = entity.Scale.x,
                    ScaleY = entity.Scale.y,
                    ScaleZ = entity.Scale.z,
                    OwnerSessionId = entity.OwnerSessionId
                };
                Room.SendMessageTo(session, spawnMsg, false);
            }
        }

        public override void OnTick()
        {
            if (Room.State != RoomState.Playing || _entities.Count == 0) return;

            bool shouldSyncOnline = (Room.CurrentTick % SyncIntervalTicks == 0);
            bool shouldRecord = (Room.CurrentTick % RecordIntervalTicks == 0);

            if (!shouldSyncOnline && !shouldRecord) return;

            // 动态扩容 Buffer
            if (_entities.Count > _syncStateBuffer.Length)
            {
                int newSize = Mathf.NextPowerOfTwo(_entities.Count);
                _syncStateBuffer = new ObjectSyncState[newSize];
                _reusableSyncMsg.States = _syncStateBuffer; // 扩容后重新绑定引用
            }

            int index = 0;
            float currentServerTime = Time.realtimeSinceStartup;

            foreach (var kvp in _entities)
            {
                var entity = kvp.Value;
                _syncStateBuffer[index] = new ObjectSyncState
                {
                    NetId = entity.NetId,
                    PosX = entity.Position.x,
                    PosY = entity.Position.y,
                    PosZ = entity.Position.z,
                    VelX = entity.Velocity.x,
                    VelY = entity.Velocity.y,
                    VelZ = entity.Velocity.z,
                    ScaleX = entity.Scale.x,
                    ScaleY = entity.Scale.y,
                    ScaleZ = entity.Scale.z,
                    AnimStateHash = entity.AnimStateHash,
                    AnimNormalizedTime = entity.AnimNormalizedTime,
                    ServerTime = currentServerTime
                };
                index++;
            }

            // 核心修复 P1-4：直接复用消息对象，并通过 ValidCount 告诉底层序列化器只处理前 index 个元素
            _reusableSyncMsg.ValidCount = index;
            Room.BroadcastMessage(_reusableSyncMsg, shouldRecord);
        }

        #region ================= 权威业务 API =================

        public ServerSyncEntity SpawnObject(int prefabHash, Vector3 position, Vector3 velocity, string ownerSessionId = "")
        {
            _netIdCounter++;
            var entity = new ServerSyncEntity
            {
                NetId = _netIdCounter,
                PrefabHash = prefabHash,
                Position = position,
                Velocity = velocity,
                OwnerSessionId = ownerSessionId ?? string.Empty
            };

            _entities.Add(entity.NetId, entity);

            var spawnMsg = new S2C_ObjectSpawn
            {
                NetId = entity.NetId,
                PrefabHash = entity.PrefabHash,
                PosX = entity.Position.x,
                PosY = entity.Position.y,
                PosZ = entity.Position.z,
                DirX = entity.Velocity.x,
                DirY = entity.Velocity.y,
                DirZ = entity.Velocity.z,
                ScaleX = entity.Scale.x,
                ScaleY = entity.Scale.y,
                ScaleZ = entity.Scale.z,
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