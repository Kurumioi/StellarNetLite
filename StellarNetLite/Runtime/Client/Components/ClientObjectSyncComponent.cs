using System.Collections.Generic;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.ObjectSync;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;

namespace StellarNet.Lite.Client.Components
{
    /// <summary>
    /// 客户端空间预测数据结构。
    /// </summary>
    public struct PredictedTransformData
    {
        public Vector3 Position;
        public Vector3 Rotation;
        public Vector3 Velocity;
        public Vector3 Scale;
        public float TimeSinceLastSync;
        public float PlaybackSpeed;
    }

    /// <summary>
    /// 客户端动画预测数据结构。
    /// </summary>
    public struct PredictedAnimatorData
    {
        public int AnimStateHash;
        public float AnimNormalizedTime;
        public float FloatParam1;
        public float FloatParam2;
        public float FloatParam3;
        public float PlaybackSpeed;
        public float ServerTimeDelta;
    }

    [RoomComponent(200, "ObjectSync", "空间与动画同步核心服务")]
    public sealed class ClientObjectSyncComponent : ClientRoomComponent
    {
        private readonly ClientApp _app;

        /// <summary>
        /// 客户端对象缓存。
        /// 这里只存最近一次权威数据，不直接生成/销毁 GameObject。
        /// </summary>
        private sealed class SyncEntityData
        {
            public int PrefabHash;
            public byte Mask;
            public string OwnerSessionId;
            public Vector3 RawPos;
            public Vector3 RawRot;
            public Vector3 RawVel;
            public Vector3 RawScale;
            public int AnimStateHash;
            public float AnimNormalizedTime;
            public float FloatParam1;
            public float FloatParam2;
            public float FloatParam3;
            public float LocalReceiveTime;
            public float ServerTime;
        }

        // NetId -> 最近一份同步缓存。
        private readonly Dictionary<int, SyncEntityData> _entities = new Dictionary<int, SyncEntityData>();
        // 回放倍速会影响表现层预测速度。
        private float _replayTimeScale = 1f;
        private IUnRegister _timeScaleEventToken;
        private bool _isInitialized;

        public ClientObjectSyncComponent(ClientApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            if (_isInitialized)
            {
                NetLogger.LogWarning("ClientObjectSyncComponent", $"重复初始化已忽略: RoomId:{Room?.RoomId ?? "-"}");
                return;
            }

            _entities.Clear();
            _replayTimeScale = 1f;

            // 先清旧监听，再注册新监听，避免重复订阅。
            _timeScaleEventToken?.UnRegister();
            _timeScaleEventToken = GlobalTypeNetEvent.Register<Local_ReplayTimeScaleChanged>(OnReplayTimeScaleChanged);

            _isInitialized = true;
            NetLogger.LogInfo("ClientObjectSyncComponent", "空间同步服务初始化完毕，等待实体生成");
        }

        public override void OnDestroy()
        {
            _isInitialized = false;
            ClearAllEntities(false);
            _timeScaleEventToken?.UnRegister();
            _timeScaleEventToken = null;
        }

        [NetHandler]
        public void OnS2C_ObjectSpawn(S2C_ObjectSpawn msg)
        {
            if (msg == null)
            {
                NetLogger.LogError("ClientObjectSyncComponent", $"处理对象生成失败: msg 为空, RoomId:{Room?.RoomId ?? "-"}");
                return;
            }

            ApplySpawnState(msg.State, true);
        }

        [NetHandler]
        public void OnS2C_ObjectDestroy(S2C_ObjectDestroy msg)
        {
            if (msg == null)
            {
                NetLogger.LogError("ClientObjectSyncComponent", $"处理对象销毁失败: msg 为空, RoomId:{Room?.RoomId ?? "-"}");
                return;
            }

            if (Room == null)
            {
                NetLogger.LogError("ClientObjectSyncComponent", $"处理对象销毁失败: Room 为空, NetId:{msg.NetId}");
                return;
            }

            if (_entities.Remove(msg.NetId))
            {
                Local_ObjectDestroyed destroyEvent = new Local_ObjectDestroyed { NetId = msg.NetId };
                Room.NetEventSystem.Broadcast(destroyEvent);
            }
        }

        [NetHandler]
        public void OnS2C_ObjectSync(S2C_ObjectSync msg)
        {
            if (msg == null)
            {
                NetLogger.LogError("ClientObjectSyncComponent", $"处理对象同步失败: msg 为空, RoomId:{Room?.RoomId ?? "-"}");
                return;
            }

            if (msg.States == null)
            {
                NetLogger.LogError("ClientObjectSyncComponent", $"处理对象同步失败: States 为空, RoomId:{Room?.RoomId ?? "-"}, ValidCount:{msg.ValidCount}");
                return;
            }

            // 增量同步只刷新已有对象缓存，不负责补建对象。
            float currentLocalTime = Time.realtimeSinceStartup;
            int count = Mathf.Min(msg.ValidCount, msg.States.Length);
            for (int i = 0; i < count; i++)
            {
                ObjectSyncState state = msg.States[i];
                if (!_entities.TryGetValue(state.NetId, out SyncEntityData data))
                {
                    continue;
                }

                // 只更新消息里声明过的同步域。
                if ((state.Mask & (byte)EntitySyncMask.Transform) != 0)
                {
                    data.RawPos.x = state.PosX;
                    data.RawPos.y = state.PosY;
                    data.RawPos.z = state.PosZ;
                    data.RawRot.x = state.RotX;
                    data.RawRot.y = state.RotY;
                    data.RawRot.z = state.RotZ;
                    data.RawVel.x = state.VelX;
                    data.RawVel.y = state.VelY;
                    data.RawVel.z = state.VelZ;
                    data.RawScale.x = state.ScaleX;
                    data.RawScale.y = state.ScaleY;
                    data.RawScale.z = state.ScaleZ;
                }

                if ((state.Mask & (byte)EntitySyncMask.Animator) != 0)
                {
                    data.AnimStateHash = state.AnimStateHash;
                    data.AnimNormalizedTime = state.AnimNormalizedTime;
                    data.FloatParam1 = state.FloatParam1;
                    data.FloatParam2 = state.FloatParam2;
                    data.FloatParam3 = state.FloatParam3;
                }

                data.ServerTime = state.ServerTime;
                data.LocalReceiveTime = currentLocalTime;
            }
        }

        /// <summary>
        /// 应用回放关键帧。
        /// 我先清空当前对象世界，再按关键帧完整生成态重建全部对象，是为了保证 Seek 到任意中段时不会保留上一段时刻的脏对象。
        /// </summary>
        public void ApplyReplaySnapshot(ObjectSpawnState[] states)
        {
            if (Room == null)
            {
                NetLogger.LogError("ClientObjectSyncComponent", $"应用回放关键帧失败: Room 为空, StateCount:{states?.Length ?? 0}");
                return;
            }

            // Seek 时先清空对象世界，再按完整生成态重建。
            ClearAllEntities(true);
            if (states == null || states.Length == 0)
            {
                return;
            }

            for (int i = 0; i < states.Length; i++)
            {
                ApplySpawnState(states[i], true);
            }
        }

        /// <summary>
        /// 清空当前全部对象世界。
        /// 我独立保留这个入口，是为了让回放 Seek、沙盒重置和房间销毁都能统一走一条干净的对象清场逻辑。
        /// </summary>
        public void ClearAllEntities(bool broadcastDestroyEvent)
        {
            if (_entities.Count == 0)
            {
                return;
            }

            if (broadcastDestroyEvent && Room == null)
            {
                NetLogger.LogError("ClientObjectSyncComponent", $"清空对象世界失败: Room 为空, EntityCount:{_entities.Count}");
                return;
            }

            // 在线销毁和回放重建都复用这条清场链。
            if (broadcastDestroyEvent)
            {
                List<int> netIds = new List<int>(_entities.Keys);
                for (int i = 0; i < netIds.Count; i++)
                {
                    Room.NetEventSystem.Broadcast(new Local_ObjectDestroyed { NetId = netIds[i] });
                }
            }

            _entities.Clear();
        }

        public bool TryGetTransformData(int netId, out PredictedTransformData result)
        {
            if (!_entities.TryGetValue(netId, out SyncEntityData data) || (data.Mask & (byte)EntitySyncMask.Transform) == 0)
            {
                result = default;
                return false;
            }

            // 在线态做简单外推；回放态直接使用快照值。
            float timeSinceLastPacket = Time.realtimeSinceStartup - data.LocalReceiveTime;
            if (_app.State == ClientAppState.ReplayRoom)
            {
                result = new PredictedTransformData
                {
                    Position = data.RawPos,
                    Rotation = data.RawRot,
                    Velocity = data.RawVel,
                    Scale = data.RawScale,
                    TimeSinceLastSync = 0f,
                    PlaybackSpeed = _replayTimeScale
                };
                return true;
            }

            Vector3 predictedPos = data.RawPos + (data.RawVel * timeSinceLastPacket);
            result = new PredictedTransformData
            {
                Position = predictedPos,
                Rotation = data.RawRot,
                Velocity = data.RawVel,
                Scale = data.RawScale,
                TimeSinceLastSync = timeSinceLastPacket,
                PlaybackSpeed = 1f
            };
            return true;
        }

        public bool TryGetAnimatorData(int netId, out PredictedAnimatorData result)
        {
            if (!_entities.TryGetValue(netId, out SyncEntityData data) || (data.Mask & (byte)EntitySyncMask.Animator) == 0)
            {
                result = default;
                return false;
            }

            // 动画层也区分在线预测和回放只读两种模式。
            float timeSinceLastPacket = Time.realtimeSinceStartup - data.LocalReceiveTime;
            if (_app.State == ClientAppState.ReplayRoom)
            {
                result = new PredictedAnimatorData
                {
                    AnimStateHash = data.AnimStateHash,
                    AnimNormalizedTime = data.AnimNormalizedTime,
                    FloatParam1 = data.FloatParam1,
                    FloatParam2 = data.FloatParam2,
                    FloatParam3 = data.FloatParam3,
                    PlaybackSpeed = _replayTimeScale,
                    ServerTimeDelta = 0f
                };
                return true;
            }

            result = new PredictedAnimatorData
            {
                AnimStateHash = data.AnimStateHash,
                AnimNormalizedTime = data.AnimNormalizedTime,
                FloatParam1 = data.FloatParam1,
                FloatParam2 = data.FloatParam2,
                FloatParam3 = data.FloatParam3,
                PlaybackSpeed = 1f,
                ServerTimeDelta = timeSinceLastPacket
            };
            return true;
        }

        /// <summary>
        /// 应用单个完整生成态。
        /// 我统一从共享完整结构写入缓存和抛本地生成事件，是为了让在线生成与回放恢复走完全相同的客户端落地路径。
        /// </summary>
        private void ApplySpawnState(ObjectSpawnState state, bool broadcastLocalEvent)
        {
            if (Room == null)
            {
                NetLogger.LogError("ClientObjectSyncComponent", $"应用对象生成态失败: Room 为空, NetId:{state.NetId}, PrefabHash:{state.PrefabHash}");
                return;
            }

            if (state.NetId <= 0)
            {
                NetLogger.LogError("ClientObjectSyncComponent", $"应用对象生成态失败: NetId 非法, RoomId:{Room.RoomId}, NetId:{state.NetId}, PrefabHash:{state.PrefabHash}");
                return;
            }

            // 这里既写入缓存，也负责向表现层抛出本地生成事件。
            if (!_entities.TryGetValue(state.NetId, out SyncEntityData data))
            {
                data = new SyncEntityData();
                _entities[state.NetId] = data;
            }

            data.PrefabHash = state.PrefabHash;
            data.Mask = state.Mask;
            data.OwnerSessionId = state.OwnerSessionId ?? string.Empty;
            data.RawPos = new Vector3(state.PosX, state.PosY, state.PosZ);
            data.RawRot = new Vector3(state.RotX, state.RotY, state.RotZ);
            data.RawVel = new Vector3(state.DirX, state.DirY, state.DirZ);
            data.RawScale = new Vector3(
                Mathf.Approximately(state.ScaleX, 0f) ? 1f : state.ScaleX,
                Mathf.Approximately(state.ScaleY, 0f) ? 1f : state.ScaleY,
                Mathf.Approximately(state.ScaleZ, 0f) ? 1f : state.ScaleZ);
            data.AnimStateHash = state.AnimStateHash;
            data.AnimNormalizedTime = state.AnimNormalizedTime;
            data.FloatParam1 = state.FloatParam1;
            data.FloatParam2 = state.FloatParam2;
            data.FloatParam3 = state.FloatParam3;
            data.LocalReceiveTime = Time.realtimeSinceStartup;
            data.ServerTime = 0f;

            if (!broadcastLocalEvent)
            {
                return;
            }

            Local_ObjectSpawned spawnEvent = new Local_ObjectSpawned
            {
                State = state
            };
            Room.NetEventSystem.Broadcast(spawnEvent);
        }

        private void OnReplayTimeScaleChanged(Local_ReplayTimeScaleChanged evt)
        {
            _replayTimeScale = evt.TimeScale;
        }
    }
}
