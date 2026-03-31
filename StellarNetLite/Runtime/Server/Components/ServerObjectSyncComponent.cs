using System;
using System.Collections.Generic;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.ObjectSync;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Components
{
    /// <summary>
    /// 服务端权威对象状态。
    /// 保存可同步实体的运行时真相。
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
    /// 负责生成对象、增量同步、重连快照和回放关键帧导出。
    /// </summary>
    [RoomComponent(200, "ObjectSync", "空间与动画同步核心服务")]
    public sealed class ServerObjectSyncComponent : RoomComponent, ITickableComponent
    {
        private readonly ServerApp _app;
        // 当前房间内全部权威实体。
        private readonly Dictionary<int, ServerSyncEntity> _entities = new Dictionary<int, ServerSyncEntity>();
        private int _netIdCounter = 0;

        private const int SyncIntervalTicks = 3;
        private const int RecordIntervalTicks = 3;

        // 增量同步消息做复用，降低频繁分配成本。
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

        /// <summary>
        /// 断线重连快照下发。
        /// 我让这里直接复用共享完整生成态构建 Spawn 消息，是为了确保重连恢复和回放恢复依赖完全一致的对象字段语义。
        /// </summary>
        public override void OnSendSnapshot(Session session)
        {
            if (session == null)
            {
                NetLogger.LogError("ServerObjectSyncComponent", $"发送对象快照失败: session 为空, RoomId:{Room?.RoomId ?? "-"}");
                return;
            }

            if (Room == null)
            {
                NetLogger.LogError("ServerObjectSyncComponent", $"发送对象快照失败: Room 为空, SessionId:{session.SessionId}");
                return;
            }

            foreach (KeyValuePair<int, ServerSyncEntity> kvp in _entities)
            {
                ServerSyncEntity entity = kvp.Value;
                if (entity == null)
                {
                    NetLogger.LogError("ServerObjectSyncComponent", $"发送对象快照失败: entity 为空, NetId:{kvp.Key}, RoomId:{Room.RoomId}, SessionId:{session.SessionId}");
                    continue;
                }

                S2C_ObjectSpawn spawnMsg = BuildSpawnMessage(entity);
                if (spawnMsg == null)
                {
                    NetLogger.LogError("ServerObjectSyncComponent",
                        $"发送对象快照失败: BuildSpawnMessage 返回 null, NetId:{entity.NetId}, RoomId:{Room.RoomId}, SessionId:{session.SessionId}");
                    continue;
                }

                Room.SendMessageTo(session, spawnMsg, false);
            }
        }

        public void OnTick()
        {
            if (Room == null)
            {
                NetLogger.LogError("ServerObjectSyncComponent", "对象同步 Tick 失败: Room 为空");
                return;
            }

            // 非 Playing 态或无实体时不做同步。
            if (Room.State != RoomState.Playing || _entities.Count == 0)
            {
                return;
            }

            // 在线广播和录像采样都按固定 Tick 间隔执行。
            bool shouldSyncOnline = Room.CurrentTick % SyncIntervalTicks == 0;
            bool shouldRecord = Room.CurrentTick % RecordIntervalTicks == 0;
            if (!shouldSyncOnline && !shouldRecord)
            {
                return;
            }

            // 实体数量增长时扩容缓存数组，避免每帧 new。
            if (_entities.Count > _syncStateBuffer.Length)
            {
                int newSize = Mathf.NextPowerOfTwo(_entities.Count);
                _syncStateBuffer = new ObjectSyncState[newSize];
                _reusableSyncMsg.States = _syncStateBuffer;
            }

            int index = 0;
            float currentServerTime = Time.realtimeSinceStartup;

            // 将权威实体压成连续同步状态数组。
            foreach (KeyValuePair<int, ServerSyncEntity> kvp in _entities)
            {
                ServerSyncEntity entity = kvp.Value;
                if (entity == null)
                {
                    NetLogger.LogError("ServerObjectSyncComponent", $"对象增量同步失败: entity 为空, NetId:{kvp.Key}, RoomId:{Room.RoomId}, CurrentTick:{Room.CurrentTick}");
                    continue;
                }

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

        #region 重写

        public override void OnMemberJoined(Session session)
        {
            base.OnMemberJoined(session);
            if (session == null) return;

            // 关键逻辑：如果房间已经在游戏中，必须立即为新加入的玩家补发当前所有实体的快照
            if (Room != null && Room.State == RoomState.Playing)
            {
                // 直接复用已有的快照下发逻辑
                OnSendSnapshot(session);
            }
        }

        #endregion

        #region ================= 权威业务 API =================

        public ServerSyncEntity SpawnObject(int prefabHash, EntitySyncMask mask, Vector3 position, Vector3 rotation, Vector3 velocity, string ownerSessionId = "")
        {
            if (Room == null)
            {
                NetLogger.LogError("ServerObjectSyncComponent", $"生成对象失败: Room 为空, PrefabHash:{prefabHash}, OwnerSessionId:{ownerSessionId}");
                return null;
            }

            if (prefabHash == 0)
            {
                NetLogger.LogError("ServerObjectSyncComponent", $"生成对象失败: prefabHash 非法, RoomId:{Room.RoomId}, PrefabHash:{prefabHash}, OwnerSessionId:{ownerSessionId}");
                return null;
            }

            // 服务端生成对象后会立即广播完整生成态。
            _netIdCounter++;
            ServerSyncEntity entity = new ServerSyncEntity
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

            S2C_ObjectSpawn spawnMsg = BuildSpawnMessage(entity);
            if (spawnMsg == null)
            {
                NetLogger.LogError("ServerObjectSyncComponent", $"生成对象失败: BuildSpawnMessage 返回 null, RoomId:{Room.RoomId}, NetId:{entity.NetId}, PrefabHash:{prefabHash}");
                return entity;
            }

            Room.BroadcastMessage(spawnMsg, true);
            return entity;
        }

        public void DestroyObject(int netId)
        {
            if (Room == null)
            {
                NetLogger.LogError("ServerObjectSyncComponent", $"销毁对象失败: Room 为空, NetId:{netId}");
                return;
            }

            if (netId <= 0)
            {
                NetLogger.LogError("ServerObjectSyncComponent", $"销毁对象失败: netId 非法, RoomId:{Room.RoomId}, NetId:{netId}");
                return;
            }

            // 销毁对象只需广播 NetId，客户端自行清理表现层。
            if (_entities.Remove(netId))
            {
                S2C_ObjectDestroy destroyMsg = new S2C_ObjectDestroy { NetId = netId };
                Room.BroadcastMessage(destroyMsg, true);
            }
        }

        public ServerSyncEntity GetEntity(int netId)
        {
            _entities.TryGetValue(netId, out ServerSyncEntity entity);
            return entity;
        }

        /// <summary>
        /// 导出当前全部对象的完整生成态。
        /// 我把这层接口集中到对象同步组件，是为了让重连快照、回放关键帧和在线 Spawn 共享同一份对象完整态转换逻辑。
        /// </summary>
        public ObjectSpawnState[] ExportSpawnStates()
        {
            if (_entities.Count == 0)
            {
                return Array.Empty<ObjectSpawnState>();
            }

            ObjectSpawnState[] result = new ObjectSpawnState[_entities.Count];
            int index = 0;

            // 导出的是“完整生成态”，专供重连和回放关键帧使用。
            foreach (KeyValuePair<int, ServerSyncEntity> kvp in _entities)
            {
                ServerSyncEntity entity = kvp.Value;
                if (entity == null)
                {
                    NetLogger.LogError("ServerObjectSyncComponent", $"导出完整生成态失败: entity 为空, NetId:{kvp.Key}, EntityCount:{_entities.Count}");
                    continue;
                }

                ObjectSpawnState state = BuildSpawnState(entity);
                result[index] = state;
                index++;
            }

            if (index == result.Length)
            {
                return result;
            }

            ObjectSpawnState[] trimmed = new ObjectSpawnState[index];
            if (index > 0)
            {
                Array.Copy(result, trimmed, index);
            }

            return trimmed;
        }

        #endregion

        #region ================= 内部构建逻辑 =================

        /// <summary>
        /// 构建共享完整生成态。
        /// 我在这里统一补齐默认缩放和值清洗，是为了避免在线 Spawn、重连快照、回放关键帧分别写出不一致的数据。
        /// </summary>
        private ObjectSpawnState BuildSpawnState(ServerSyncEntity entity)
        {
            if (entity == null)
            {
                NetLogger.LogError("ServerObjectSyncComponent", "构建完整生成态失败: entity 为空");
                return ObjectSpawnState.CreateDefault();
            }

            float scaleX = Mathf.Approximately(entity.Scale.x, 0f) ? 1f : entity.Scale.x;
            float scaleY = Mathf.Approximately(entity.Scale.y, 0f) ? 1f : entity.Scale.y;
            float scaleZ = Mathf.Approximately(entity.Scale.z, 0f) ? 1f : entity.Scale.z;

            return new ObjectSpawnState
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

                ScaleX = scaleX,
                ScaleY = scaleY,
                ScaleZ = scaleZ,

                AnimStateHash = entity.AnimStateHash,
                AnimNormalizedTime = entity.AnimNormalizedTime,
                FloatParam1 = entity.FloatParam1,
                FloatParam2 = entity.FloatParam2,
                FloatParam3 = entity.FloatParam3,

                OwnerSessionId = entity.OwnerSessionId ?? string.Empty
            };
        }

        /// <summary>
        /// 构建在线 Spawn 消息。
        /// 我让在线消息只作为共享完整结构的包装壳，避免以后字段扩展时消息层继续维护一份平行定义。
        /// </summary>
        private S2C_ObjectSpawn BuildSpawnMessage(ServerSyncEntity entity)
        {
            if (entity == null)
            {
                NetLogger.LogError("ServerObjectSyncComponent", "构建 Spawn 消息失败: entity 为空");
                return null;
            }

            return new S2C_ObjectSpawn
            {
                State = BuildSpawnState(entity)
            };
        }

        #endregion
    }
}
