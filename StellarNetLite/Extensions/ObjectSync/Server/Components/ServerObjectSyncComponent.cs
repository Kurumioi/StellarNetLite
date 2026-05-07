using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.ObjectSync;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Replay;
using StellarNet.Lite.Shared.Infrastructure;

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
        public AnimatorParamValue[] AnimParams = Array.Empty<AnimatorParamValue>();
        public int AnimParamCount;

        /// <summary>
        /// 用逻辑状态名设置动画状态。
        /// </summary>
        public void SetAnimState(string logicStateName, float normalizedTime = 0f)
        {
            AnimStateHash = ObjectSyncAnimHashUtility.GetStableStringHash(logicStateName);
            AnimNormalizedTime = normalizedTime;
        }

        /// <summary>
        /// 用逻辑参数名设置动画参数。
        /// </summary>
        public void SetAnimParam(string logicParamName, float value)
        {
            SetAnimParam(ObjectSyncAnimHashUtility.GetStableStringHash(logicParamName), value);
        }

        /// <summary>
        /// 用逻辑参数哈希设置动画参数。
        /// </summary>
        public void SetAnimParam(int logicParamHash, float value)
        {
            if (logicParamHash == 0)
            {
                return;
            }

            for (int i = 0; i < AnimParamCount; i++)
            {
                int currentHash = AnimParams[i].ParamHash;
                if (currentHash == logicParamHash)
                {
                    AnimParams[i].Value = value;
                    return;
                }

                if (currentHash > logicParamHash)
                {
                    InsertAnimParamAt(i, logicParamHash, value);
                    return;
                }
            }

            InsertAnimParamAt(AnimParamCount, logicParamHash, value);
        }

        /// <summary>
        /// 清空全部动画参数。
        /// </summary>
        public void ClearAnimParams()
        {
            AnimParamCount = 0;
        }

        private void EnsureAnimParamCapacity(int requiredCount)
        {
            if (requiredCount <= 0)
            {
                return;
            }

            if (AnimParams == null || AnimParams.Length == 0)
            {
                AnimParams = new AnimatorParamValue[Mathf.NextPowerOfTwo(requiredCount)];
                return;
            }

            if (AnimParams.Length >= requiredCount)
            {
                return;
            }

            int nextSize = Mathf.NextPowerOfTwo(requiredCount);
            var newArray = new AnimatorParamValue[nextSize];
            Array.Copy(AnimParams, newArray, AnimParamCount);
            AnimParams = newArray;
        }

        private void InsertAnimParamAt(int index, int logicParamHash, float value)
        {
            EnsureAnimParamCapacity(AnimParamCount + 1);

            if (index < AnimParamCount)
            {
                Array.Copy(AnimParams, index, AnimParams, index + 1, AnimParamCount - index);
            }

            AnimParams[index] = new AnimatorParamValue
            {
                ParamHash = logicParamHash,
                Value = value
            };
            AnimParamCount++;
        }
    }

    /// <summary>
    /// 服务端对象同步组件。
    /// 负责实体创建、销毁、周期同步和回放快照导出。
    /// </summary>
    [RoomComponent(200, "ObjectSync", "空间与动画同步核心服务")]
    public sealed class ServerObjectSyncComponent : ServerRoomComponent, ITickableComponent, IReplaySnapshotProvider
    {
        private struct SyncSnapshot
        {
            public Vector3 Position;
            public Vector3 Rotation;
            public Vector3 Velocity;
            public Vector3 Scale;
            public int AnimStateHash;
            public float AnimNormalizedTime;
            public AnimatorParamValue[] AnimParams;
            public int AnimParamCount;
            public byte Mask;
        }

        private readonly ServerApp _app;
        private readonly Dictionary<int, ServerSyncEntity> _entities = new Dictionary<int, ServerSyncEntity>();
        private readonly Dictionary<int, SyncSnapshot> _lastSentSnapshots = new Dictionary<int, SyncSnapshot>();
        private int _netIdCounter = 0;

        private const int MediumRoomMemberThreshold = 8;
        private const int LargeRoomMemberThreshold = 16;
        private const int MediumRoomIntervalTicks = 2;
        private const int LargeRoomIntervalTicks = 4;
        private const float PositionDirtyThresholdSqr = 0.0004f;
        private const float RotationDirtyThreshold = 0.5f;
        private const float VelocityDirtyThresholdSqr = 0.0004f;
        private const float ScaleDirtyThresholdSqr = 0.0001f;
        private const float FloatParamDirtyThreshold = 0.01f;
        private const float AnimNormalizedTimeDirtyThreshold = 0.01f;

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
            _lastSentSnapshots.Clear();
            _netIdCounter = 0;
        }

        public override void OnDestroy()
        {
            _entities.Clear();
            _lastSentSnapshots.Clear();
        }

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

            int onlineIntervalTicks = ResolveOnlineSyncIntervalTicks();
            int replayRecordIntervalTicks = ResolveReplayRecordIntervalTicks();
            bool shouldSyncOnline = Room.CurrentTick % onlineIntervalTicks == 0;
            bool shouldFullResync = ResolveFullResyncIntervalTicks() > 0 && Room.CurrentTick % ResolveFullResyncIntervalTicks() == 0;
            bool shouldRecord = replayRecordIntervalTicks > 0 && Room.CurrentTick % replayRecordIntervalTicks == 0;

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

                if (!shouldFullResync && !HasMeaningfulChange(entity))
                {
                    continue;
                }

                ushort dirtyMask = BuildDirtyMask(entity, shouldFullResync);
                _syncStateBuffer[index++] = new ObjectSyncState
                {
                    NetId = entity.NetId,
                    Mask = entity.Mask,
                    DirtyMask = dirtyMask,
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
                    AnimParamCount = entity.AnimParamCount,
                    AnimParams = CloneAnimParams(entity.AnimParams, entity.AnimParamCount)
                };

                _lastSentSnapshots[entity.NetId] = CaptureSnapshot(entity);
            }

            if (index <= 0)
            {
                return;
            }

            _reusableSyncMsg.ServerTime = currentServerTime;
            _reusableSyncMsg.ValidCount = index;
            if (shouldSyncOnline)
            {
                Room.BroadcastMessage(_reusableSyncMsg, shouldRecord);
            }
            else if (shouldRecord)
            {
                Room.RecordMessageToReplay(_reusableSyncMsg);
            }
        }

        private int ResolveReplayRecordIntervalTicks()
        {
            return ObjectSyncConfigLoader.Current.ReplayObjectSyncRecordIntervalTicks;
        }

        private int ResolveFullResyncIntervalTicks()
        {
            return ObjectSyncConfigLoader.Current.ObjectSyncFullResyncIntervalTicks;
        }

        private int ResolveOnlineSyncIntervalTicks()
        {
            ObjectSyncGlobalConfig config = ObjectSyncConfigLoader.Current;
            int baseInterval = config.ObjectSyncOnlineIntervalTicks;
            bool enableAdaptive = config.EnableAdaptiveObjectSync;

            if (!enableAdaptive)
            {
                return baseInterval;
            }

            int memberCount = Room != null ? Room.MemberCount : 0;
            if (memberCount >= LargeRoomMemberThreshold)
            {
                return Mathf.Max(baseInterval, LargeRoomIntervalTicks);
            }

            if (memberCount >= MediumRoomMemberThreshold)
            {
                return Mathf.Max(baseInterval, MediumRoomIntervalTicks);
            }

            return baseInterval;
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
            _lastSentSnapshots[entity.NetId] = CaptureSnapshot(entity);
            Room.BroadcastMessage(new S2C_ObjectSpawn { State = BuildSpawnState(entity) }, true);
            return entity;
        }

        public void DestroyObject(int netId)
        {
            if (_entities.Remove(netId))
            {
                _lastSentSnapshots.Remove(netId);
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
                AnimParamCount = entity.AnimParamCount,
                AnimParams = CloneAnimParams(entity.AnimParams, entity.AnimParamCount),
                OwnerSessionId = entity.OwnerSessionId ?? string.Empty
            };
        }

        private SyncSnapshot CaptureSnapshot(ServerSyncEntity entity)
        {
            return new SyncSnapshot
            {
                Position = entity.Position,
                Rotation = entity.Rotation,
                Velocity = entity.Velocity,
                Scale = entity.Scale,
                AnimStateHash = entity.AnimStateHash,
                AnimNormalizedTime = entity.AnimNormalizedTime,
                AnimParamCount = entity.AnimParamCount,
                AnimParams = CloneAnimParams(entity.AnimParams, entity.AnimParamCount),
                Mask = entity.Mask
            };
        }

        private bool HasMeaningfulChange(ServerSyncEntity entity)
        {
            if (!_lastSentSnapshots.TryGetValue(entity.NetId, out SyncSnapshot snapshot))
            {
                return true;
            }

            if (snapshot.Mask != entity.Mask)
            {
                return true;
            }

            if ((entity.Position - snapshot.Position).sqrMagnitude > PositionDirtyThresholdSqr)
            {
                return true;
            }

            if ((entity.Velocity - snapshot.Velocity).sqrMagnitude > VelocityDirtyThresholdSqr)
            {
                return true;
            }

            if ((entity.Scale - snapshot.Scale).sqrMagnitude > ScaleDirtyThresholdSqr)
            {
                return true;
            }

            if (Mathf.Abs(entity.Rotation.x - snapshot.Rotation.x) > RotationDirtyThreshold ||
                Mathf.Abs(entity.Rotation.y - snapshot.Rotation.y) > RotationDirtyThreshold ||
                Mathf.Abs(entity.Rotation.z - snapshot.Rotation.z) > RotationDirtyThreshold)
            {
                return true;
            }

            if (entity.AnimStateHash != snapshot.AnimStateHash)
            {
                return true;
            }

            if (Mathf.Abs(entity.AnimNormalizedTime - snapshot.AnimNormalizedTime) > AnimNormalizedTimeDirtyThreshold)
            {
                return true;
            }

            if (HasAnimParamMeaningfulChange(entity.AnimParams, entity.AnimParamCount, snapshot.AnimParams, snapshot.AnimParamCount))
            {
                return true;
            }

            return false;
        }

        private ushort BuildDirtyMask(ServerSyncEntity entity, bool forceFull)
        {
            if (!_lastSentSnapshots.TryGetValue(entity.NetId, out SyncSnapshot snapshot) || forceFull)
            {
                return BuildFullDirtyMask(entity.Mask);
            }

            ObjectSyncDirtyMask dirtyMask = ObjectSyncDirtyMask.None;
            if ((entity.Mask & (byte)EntitySyncMask.Transform) != 0)
            {
                if ((entity.Position - snapshot.Position).sqrMagnitude > PositionDirtyThresholdSqr)
                {
                    dirtyMask |= ObjectSyncDirtyMask.Position;
                }

                if (Mathf.Abs(entity.Rotation.x - snapshot.Rotation.x) > RotationDirtyThreshold ||
                    Mathf.Abs(entity.Rotation.y - snapshot.Rotation.y) > RotationDirtyThreshold ||
                    Mathf.Abs(entity.Rotation.z - snapshot.Rotation.z) > RotationDirtyThreshold)
                {
                    dirtyMask |= ObjectSyncDirtyMask.Rotation;
                }

                if ((entity.Velocity - snapshot.Velocity).sqrMagnitude > VelocityDirtyThresholdSqr)
                {
                    dirtyMask |= ObjectSyncDirtyMask.Velocity;
                }

                if ((entity.Scale - snapshot.Scale).sqrMagnitude > ScaleDirtyThresholdSqr)
                {
                    dirtyMask |= ObjectSyncDirtyMask.Scale;
                }
            }

            if ((entity.Mask & (byte)EntitySyncMask.Animator) != 0)
            {
                if (entity.AnimStateHash != snapshot.AnimStateHash)
                {
                    dirtyMask |= ObjectSyncDirtyMask.AnimState;
                }

                if (Mathf.Abs(entity.AnimNormalizedTime - snapshot.AnimNormalizedTime) > AnimNormalizedTimeDirtyThreshold)
                {
                    dirtyMask |= ObjectSyncDirtyMask.AnimNormalizedTime;
                }

                if (HasAnimParamMeaningfulChange(entity.AnimParams, entity.AnimParamCount, snapshot.AnimParams, snapshot.AnimParamCount))
                {
                    dirtyMask |= ObjectSyncDirtyMask.AnimParams;
                }
            }

            return (ushort)dirtyMask;
        }

        private static bool HasAnimParamMeaningfulChange(
            AnimatorParamValue[] currentParams,
            int currentCount,
            AnimatorParamValue[] snapshotParams,
            int snapshotCount)
        {
            if (currentCount != snapshotCount)
            {
                return true;
            }

            for (int i = 0; i < currentCount; i++)
            {
                AnimatorParamValue current = currentParams[i];
                AnimatorParamValue snapshot = snapshotParams[i];
                if (current.ParamHash != snapshot.ParamHash)
                {
                    return true;
                }

                if (Mathf.Abs(current.Value - snapshot.Value) > FloatParamDirtyThreshold)
                {
                    return true;
                }
            }

            return false;
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
    }
}
