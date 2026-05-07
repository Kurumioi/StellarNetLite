using System;
using System.Collections.Generic;
using System.IO;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.ObjectSync;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Shared.Replay;
using UnityEngine;

namespace StellarNet.Lite.Client.Components
{
    /// <summary>
    /// 客户端预测后的位移数据。
    /// </summary>
    public struct PredictedTransformData
    {
        /// <summary>
        /// 预测后的位置。
        /// </summary>
        public Vector3 Position;

        /// <summary>
        /// 预测后的欧拉旋转。
        /// </summary>
        public Vector3 Rotation;

        /// <summary>
        /// 当前速度。
        /// </summary>
        public Vector3 Velocity;

        /// <summary>
        /// 当前缩放。
        /// </summary>
        public Vector3 Scale;

        /// <summary>
        /// 距离上次同步已经过去的本地时间。
        /// </summary>
        public float TimeSinceLastSync;

        /// <summary>
        /// 当前播放倍率或预测倍率。
        /// </summary>
        public float PlaybackSpeed;
    }

    /// <summary>
    /// 客户端预测后的动画数据。
    /// </summary>
        public struct PredictedAnimatorData
        {
        /// <summary>
        /// 动画状态 Hash。
        /// </summary>
        public int AnimStateHash;

        /// <summary>
        /// 动画归一化时间。
        /// </summary>
        public float AnimNormalizedTime;

            /// <summary>
            /// 当前动画参数数量。
            /// </summary>
            public int AnimParamCount;

            /// <summary>
            /// 当前动画参数列表。
            /// </summary>
            public AnimatorParamValue[] AnimParams;

        /// <summary>
        /// 当前播放倍率。
        /// </summary>
        public float PlaybackSpeed;

        /// <summary>
        /// 服务端时间差补偿。
        /// </summary>
        public float ServerTimeDelta;
    }

    /// <summary>
    /// 客户端对象同步组件。
    /// 负责缓存服务端同步状态、提供预测结果，并消费录像快照。
    /// </summary>
    [RoomComponent(200, "ObjectSync", "空间与动画同步核心服务")]
    public sealed class ClientObjectSyncComponent : ClientRoomComponent, IReplaySnapshotConsumer
    {
        /// <summary>
        /// 所属客户端逻辑宿主。
        /// </summary>
        private readonly ClientApp _app;

        /// <summary>
        /// 在线房间远端实体默认预测前置时间。
        /// </summary>
        private const float RemoteOnlinePredictionLeadSeconds = 0.05f;

        /// <summary>
        /// 在线房间最大预测窗口。
        /// </summary>
        private const float MaxOnlinePredictionWindow = 0.2f;

        /// <summary>
        /// 单个同步实体的客户端缓存状态。
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
            public int AnimParamCount;
            public AnimatorParamValue[] AnimParams;
            public float LocalReceiveTime;
            public float ServerTime;
        }

        /// <summary>
        /// 当前所有同步实体缓存。
        /// </summary>
        private readonly Dictionary<int, SyncEntityData> _entities = new Dictionary<int, SyncEntityData>();

        /// <summary>
        /// 当前回放倍率。
        /// </summary>
        private float _replayTimeScale = 1f;

        /// <summary>
        /// 回放倍率事件注销句柄。
        /// </summary>
        private IUnRegister _timeScaleEventToken;

        /// <summary>
        /// 回放基准本地时间。
        /// </summary>
        private float _replayBaseLocalTime = -1f;

        /// <summary>
        /// 回放基准服务端时间。
        /// </summary>
        private float _replayBaseServerTime = -1f;

        /// <summary>
        /// 当前组件负责消费的快照组件 Id。
        /// </summary>
        public int SnapshotComponentId => 200;

        /// <summary>
        /// 创建客户端对象同步组件。
        /// </summary>
        public ClientObjectSyncComponent(ClientApp app)
        {
            _app = app;
        }

        /// <summary>
        /// 初始化对象缓存和回放倍率监听。
        /// </summary>
        public override void OnInit()
        {
            _entities.Clear();
            _replayTimeScale = 1f;
            _replayBaseLocalTime = -1f;
            _replayBaseServerTime = -1f;
            _timeScaleEventToken?.UnRegister();
            _timeScaleEventToken = GlobalTypeNetEvent.Register<Local_ReplayTimeScaleChanged>(OnReplayTimeScaleChanged);
        }

        /// <summary>
        /// 销毁组件时清理实体缓存和事件监听。
        /// </summary>
        public override void OnDestroy()
        {
            ClearAllEntities(false);
            _timeScaleEventToken?.UnRegister();
            _timeScaleEventToken = null;
        }

        /// <summary>
        /// 收到回放倍率变化后修正本地时间基线。
        /// </summary>
        private void OnReplayTimeScaleChanged(Local_ReplayTimeScaleChanged evt)
        {
            if (_replayBaseLocalTime >= 0f)
            {
                float currentEstimated = _replayBaseServerTime + (Time.realtimeSinceStartup - _replayBaseLocalTime) * _replayTimeScale;
                _replayBaseServerTime = currentEstimated;
                _replayBaseLocalTime = Time.realtimeSinceStartup;
            }

            _replayTimeScale = evt.TimeScale;
        }

        /// <summary>
        /// 处理对象生成消息。
        /// </summary>
        [NetHandler]
        public void OnS2C_ObjectSpawn(S2C_ObjectSpawn msg)
        {
            if (msg == null)
            {
                return;
            }

            ApplySpawnState(msg.State);
            Room?.NetEventSystem.Broadcast(msg);
        }

        /// <summary>
        /// 处理对象销毁消息。
        /// </summary>
        [NetHandler]
        public void OnS2C_ObjectDestroy(S2C_ObjectDestroy msg)
        {
            if (msg == null)
            {
                return;
            }

            if (_entities.Remove(msg.NetId))
            {
                Room?.NetEventSystem.Broadcast(msg);
            }
        }

        /// <summary>
        /// 处理对象同步消息并更新预测缓存。
        /// </summary>
        [NetHandler]
        public void OnS2C_ObjectSync(S2C_ObjectSync msg)
        {
            if (msg.States == null) return;
            float currentLocalTime = Time.realtimeSinceStartup;

            if (_app.State == ClientAppState.SandboxRoom && msg.ValidCount > 0)
            {
                float packetServerTime = msg.ServerTime;
                if (_replayBaseLocalTime < 0f || packetServerTime < _replayBaseServerTime || packetServerTime > _replayBaseServerTime + 5f)
                {
                    _replayBaseLocalTime = currentLocalTime;
                    _replayBaseServerTime = packetServerTime;
                }
            }

            int count = Mathf.Min(msg.ValidCount, msg.States.Length);
            for (int i = 0; i < count; i++)
            {
                ObjectSyncState state = msg.States[i];
                if (!_entities.TryGetValue(state.NetId, out SyncEntityData data)) continue;
                ApplySyncState(ref data, in state);
                data.ServerTime = msg.ServerTime;
                data.LocalReceiveTime = currentLocalTime;
            }
        }

        /// <summary>
        /// 消费录像系统传来的快照字节流。
        /// </summary>
        public void ApplySnapshot(byte[] payload)
        {
            if (payload == null || payload.Length == 0)
            {
                ApplyReplaySnapshot(Array.Empty<ObjectSpawnState>());
                return;
            }

            using (MemoryStream ms = new MemoryStream(payload))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                int count = reader.ReadInt32();
                ObjectSpawnState[] states = new ObjectSpawnState[count];
                for (int i = 0; i < count; i++)
                {
                    states[i] = new ObjectSpawnState();
                    states[i].Deserialize(reader);
                }

                ApplyReplaySnapshot(states);
            }
        }

        /// <summary>
        /// 直接应用一组回放快照状态。
        /// </summary>
        public void ApplyReplaySnapshot(ObjectSpawnState[] states)
        {
            if (states == null)
            {
                ClearAllEntities(true);
                return;
            }

            var snapshotNetIds = new HashSet<int>();
            for (int i = 0; i < states.Length; i++) snapshotNetIds.Add(states[i].NetId);

            var toDestroy = new List<int>();
            foreach (var netId in _entities.Keys)
            {
                if (!snapshotNetIds.Contains(netId)) toDestroy.Add(netId);
            }

            foreach (var netId in toDestroy)
            {
                _entities.Remove(netId);
                Room?.NetEventSystem.Broadcast(new S2C_ObjectDestroy { NetId = netId });
            }

            for (int i = 0; i < states.Length; i++)
            {
                bool isNew = !_entities.ContainsKey(states[i].NetId);
                ApplySpawnState(states[i]);
                if (isNew)
                {
                    Room?.NetEventSystem.Broadcast(new S2C_ObjectSpawn { State = states[i] });
                }
            }
        }

        /// <summary>
        /// 清空全部实体缓存。
        /// </summary>
        public void ClearAllEntities(bool broadcastDestroyEvent)
        {
            if (_entities.Count == 0) return;
            if (broadcastDestroyEvent)
            {
                foreach (int netId in _entities.Keys)
                {
                    Room?.NetEventSystem.Broadcast(new S2C_ObjectDestroy { NetId = netId });
                }
            }

            _entities.Clear();
            _replayBaseLocalTime = -1f;
            _replayBaseServerTime = -1f;
        }

        /// <summary>
        /// 读取单个实体的完整生成态。
        /// </summary>
        public bool TryGetSpawnState(int netId, out ObjectSpawnState state)
        {
            if (!_entities.TryGetValue(netId, out SyncEntityData data))
            {
                state = default;
                return false;
            }

            state = BuildSpawnState(netId, data);
            return true;
        }

        /// <summary>
        /// 读取当前全部实体的完整生成态列表。
        /// </summary>
        public List<ObjectSpawnState> GetAllSpawnStates()
        {
            var list = new List<ObjectSpawnState>(_entities.Count);
            foreach (var kvp in _entities)
            {
                list.Add(BuildSpawnState(kvp.Key, kvp.Value));
            }

            return list;
        }

        /// <summary>
        /// 直接应用一条实时同步状态。
        /// </summary>
        public void ApplyLiveState(ObjectSyncState state, float serverTime)
        {
            if (state.NetId <= 0)
            {
                return;
            }

            if (!_entities.TryGetValue(state.NetId, out SyncEntityData data))
            {
                return;
            }

            ApplySyncState(ref data, in state);
            data.ServerTime = serverTime;
            data.LocalReceiveTime = Time.realtimeSinceStartup;

            if (_app.State == ClientAppState.SandboxRoom)
            {
                _replayBaseLocalTime = data.LocalReceiveTime;
                _replayBaseServerTime = serverTime;
            }
        }

        public bool IsLocalOwnedEntity(int netId)
        {
            if (!_entities.TryGetValue(netId, out SyncEntityData data) || NetClient.Session == null)
            {
                return false;
            }

            return data.OwnerSessionId == NetClient.Session.SessionId;
        }

        public bool TryGetTransformData(int netId, out PredictedTransformData result)
        {
            if (!_entities.TryGetValue(netId, out SyncEntityData data) || (data.Mask & (byte)EntitySyncMask.Transform) == 0)
            {
                result = default;
                return false;
            }

            if (_app.State == ClientAppState.SandboxRoom)
            {
                float replayDelta = 0f;
                if (_replayBaseLocalTime >= 0f)
                {
                    float estimatedServerTime = _replayBaseServerTime + (Time.realtimeSinceStartup - _replayBaseLocalTime) * _replayTimeScale;
                    replayDelta = estimatedServerTime - data.ServerTime;
                }

                if (replayDelta < 0f) replayDelta = 0f;
                if (replayDelta > 0.15f) replayDelta = 0.15f;

                result = new PredictedTransformData
                {
                    Position = data.RawPos + (data.RawVel * replayDelta),
                    Rotation = data.RawRot,
                    Velocity = data.RawVel,
                    Scale = data.RawScale,
                    TimeSinceLastSync = replayDelta,
                    PlaybackSpeed = _replayTimeScale
                };
                return true;
            }

            float timeSinceLastPacket = Time.realtimeSinceStartup - data.LocalReceiveTime;
            bool isLocalOwned = NetClient.Session != null && data.OwnerSessionId == NetClient.Session.SessionId;
            if (!isLocalOwned)
            {
                timeSinceLastPacket += RemoteOnlinePredictionLeadSeconds;
            }

            if (timeSinceLastPacket > MaxOnlinePredictionWindow)
            {
                timeSinceLastPacket = MaxOnlinePredictionWindow;
            }

            result = new PredictedTransformData
            {
                Position = data.RawPos + (data.RawVel * timeSinceLastPacket),
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

            if (_app.State == ClientAppState.SandboxRoom)
            {
                float replayDelta = 0f;
                if (_replayBaseLocalTime >= 0f)
                {
                    float estimatedServerTime = _replayBaseServerTime + (Time.realtimeSinceStartup - _replayBaseLocalTime) * _replayTimeScale;
                    replayDelta = estimatedServerTime - data.ServerTime;
                }

                if (replayDelta < 0f) replayDelta = 0f;
                if (replayDelta > 0.15f) replayDelta = 0.15f;

                result = new PredictedAnimatorData
                {
                    AnimStateHash = data.AnimStateHash,
                    AnimNormalizedTime = data.AnimNormalizedTime,
                    AnimParamCount = data.AnimParamCount,
                    AnimParams = data.AnimParams,
                    PlaybackSpeed = _replayTimeScale,
                    ServerTimeDelta = replayDelta
                };
                return true;
            }

            float timeSinceLastPacket = Time.realtimeSinceStartup - data.LocalReceiveTime;
            bool isLocalOwned = NetClient.Session != null && data.OwnerSessionId == NetClient.Session.SessionId;
            if (!isLocalOwned)
            {
                timeSinceLastPacket += RemoteOnlinePredictionLeadSeconds;
            }

            if (timeSinceLastPacket > MaxOnlinePredictionWindow)
            {
                timeSinceLastPacket = MaxOnlinePredictionWindow;
            }

            result = new PredictedAnimatorData
            {
                AnimStateHash = data.AnimStateHash,
                AnimNormalizedTime = data.AnimNormalizedTime,
                AnimParamCount = data.AnimParamCount,
                AnimParams = data.AnimParams,
                PlaybackSpeed = 1f,
                ServerTimeDelta = timeSinceLastPacket
            };
            return true;
        }

        private void ApplySpawnState(ObjectSpawnState state)
        {
            if (state.NetId <= 0) return;

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
            data.RawScale = new Vector3(Mathf.Approximately(state.ScaleX, 0f) ? 1f : state.ScaleX,
                Mathf.Approximately(state.ScaleY, 0f) ? 1f : state.ScaleY, Mathf.Approximately(state.ScaleZ, 0f) ? 1f : state.ScaleZ);
            data.AnimStateHash = state.AnimStateHash;
            data.AnimNormalizedTime = state.AnimNormalizedTime;
            CopyAnimParams(state.AnimParams, state.AnimParamCount, ref data.AnimParams, out data.AnimParamCount);
            data.LocalReceiveTime = Time.realtimeSinceStartup;
            data.ServerTime = 0f;
        }

        private static void ApplySyncState(ref SyncEntityData data, in ObjectSyncState state)
        {
            if ((state.DirtyMask & (ushort)ObjectSyncDirtyMask.Position) != 0)
            {
                data.RawPos.x = state.PosX;
                data.RawPos.y = state.PosY;
                data.RawPos.z = state.PosZ;
            }

            if ((state.DirtyMask & (ushort)ObjectSyncDirtyMask.Rotation) != 0)
            {
                data.RawRot.x = state.RotX;
                data.RawRot.y = state.RotY;
                data.RawRot.z = state.RotZ;
            }

            if ((state.DirtyMask & (ushort)ObjectSyncDirtyMask.Velocity) != 0)
            {
                data.RawVel.x = state.VelX;
                data.RawVel.y = state.VelY;
                data.RawVel.z = state.VelZ;
            }

            if ((state.DirtyMask & (ushort)ObjectSyncDirtyMask.Scale) != 0)
            {
                data.RawScale.x = state.ScaleX;
                data.RawScale.y = state.ScaleY;
                data.RawScale.z = state.ScaleZ;
            }

            if ((state.DirtyMask & (ushort)ObjectSyncDirtyMask.AnimState) != 0)
            {
                data.AnimStateHash = state.AnimStateHash;
            }

            if ((state.DirtyMask & (ushort)ObjectSyncDirtyMask.AnimNormalizedTime) != 0)
            {
                data.AnimNormalizedTime = state.AnimNormalizedTime;
            }

            if ((state.DirtyMask & (ushort)ObjectSyncDirtyMask.AnimParams) != 0)
            {
                CopyAnimParams(state.AnimParams, state.AnimParamCount, ref data.AnimParams, out data.AnimParamCount);
            }
        }

        private ObjectSpawnState BuildSpawnState(int netId, SyncEntityData data)
        {
            return new ObjectSpawnState
            {
                NetId = netId,
                PrefabHash = data.PrefabHash,
                Mask = data.Mask,
                PosX = data.RawPos.x,
                PosY = data.RawPos.y,
                PosZ = data.RawPos.z,
                RotX = data.RawRot.x,
                RotY = data.RawRot.y,
                RotZ = data.RawRot.z,
                DirX = data.RawVel.x,
                DirY = data.RawVel.y,
                DirZ = data.RawVel.z,
                ScaleX = data.RawScale.x,
                ScaleY = data.RawScale.y,
                ScaleZ = data.RawScale.z,
                AnimStateHash = data.AnimStateHash,
                AnimNormalizedTime = data.AnimNormalizedTime,
                AnimParamCount = data.AnimParamCount,
                AnimParams = CloneAnimParams(data.AnimParams, data.AnimParamCount),
                OwnerSessionId = data.OwnerSessionId
            };
        }

        private static void CopyAnimParams(
            AnimatorParamValue[] source,
            int sourceCount,
            ref AnimatorParamValue[] target,
            out int targetCount)
        {
            if (sourceCount <= 0 || source == null || source.Length <= 0)
            {
                target = Array.Empty<AnimatorParamValue>();
                targetCount = 0;
                return;
            }

            if (target == null || target.Length < sourceCount)
            {
                target = new AnimatorParamValue[sourceCount];
            }

            Array.Copy(source, target, sourceCount);
            targetCount = sourceCount;
        }

        private static AnimatorParamValue[] CloneAnimParams(AnimatorParamValue[] source, int count)
        {
            if (count <= 0 || source == null || source.Length <= 0)
            {
                return Array.Empty<AnimatorParamValue>();
            }

            var cloned = new AnimatorParamValue[count];
            Array.Copy(source, cloned, count);
            return cloned;
        }
    }
}
