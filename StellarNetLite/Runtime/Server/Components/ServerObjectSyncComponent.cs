using System.Collections.Generic;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Components
{
    /// <summary>
    /// 服务端网络实体领域模型
    /// 职责：在服务端内存中维护实体的权威状态。
    /// </summary>
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

    /// <summary>
    /// 服务端空间与动画同步核心组件 (权威层)
    /// 职责：提供实体生成/销毁的权威 API，并在 OnTick 中执行降频状态同步与录像抽帧。
    /// </summary>
    [RoomComponent(200, "ObjectSync", "空间与动画同步核心服务")]
    public sealed class ServerObjectSyncComponent : RoomComponent
    {
        private readonly ServerApp _app;
        private readonly Dictionary<int, ServerSyncEntity> _entities = new Dictionary<int, ServerSyncEntity>();
        private int _netIdCounter = 0;

        // 频率控制配置
        private const int SyncIntervalTicks = 3; // 在线同步频率：每 3 帧同步一次 (约 20Hz)
        private const int RecordIntervalTicks = 3; // 核心修复：录像抽帧频率对齐在线同步，保证回放平滑度

        public ServerObjectSyncComponent(ServerApp app)
        {
            _app = app;
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
        /// 断线重连与中途加入支持：
        /// 当玩家重连就绪时，将当前服务端存在的所有实体以 Spawn 的形式定向下发给该玩家。
        /// </summary>
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
                // 定向发送，不需要录入 Replay
                Room.SendMessageTo(session, spawnMsg, false);
            }
        }

        /// <summary>
        /// 核心生命周期：每帧轮询。
        /// 架构意图：在此处收集所有实体的脏数据，打包成数组进行高频广播，并执行录像抽帧策略。
        /// </summary>
        public override void OnTick()
        {
            if (Room.State != RoomState.Playing || _entities.Count == 0) return;

            // 1. 频率控制：是否需要向在线玩家广播
            bool shouldSyncOnline = (Room.CurrentTick % SyncIntervalTicks == 0);
            // 2. 频率控制：是否需要将本帧同步包写入 Replay 录像
            bool shouldRecord = (Room.CurrentTick % RecordIntervalTicks == 0);

            if (!shouldSyncOnline && !shouldRecord) return;

            // 收集当前状态
            var states = new ObjectSyncState[_entities.Count];
            int index = 0;
            float currentServerTime = Time.realtimeSinceStartup;

            foreach (var kvp in _entities)
            {
                var entity = kvp.Value;
                states[index] = new ObjectSyncState
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

            var syncMsg = new S2C_ObjectSync { States = states };
            // 核心机制：利用补丁中新增的 recordToReplay 参数，实现降频录制
            Room.BroadcastMessage(syncMsg, shouldRecord);
        }

        #region ================= 权威业务 API (供其他 ServerComponent 调用) =================

        /// <summary>
        /// 权威生成网络实体
        /// </summary>
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

            // 生成事件属于低频关键帧，必须强制录入 Replay
            Room.BroadcastMessage(spawnMsg, true);
            return entity;
        }

        /// <summary>
        /// 权威销毁网络实体
        /// </summary>
        public void DestroyObject(int netId)
        {
            if (_entities.Remove(netId))
            {
                var destroyMsg = new S2C_ObjectDestroy { NetId = netId };
                // 销毁事件属于低频关键帧，必须强制录入 Replay
                Room.BroadcastMessage(destroyMsg, true);
            }
        }

        /// <summary>
        /// 获取实体以更新其权威状态 (如被技能击退、播放受击动画)
        /// </summary>
        public ServerSyncEntity GetEntity(int netId)
        {
            _entities.TryGetValue(netId, out var entity);
            return entity;
        }

        #endregion
    }
}