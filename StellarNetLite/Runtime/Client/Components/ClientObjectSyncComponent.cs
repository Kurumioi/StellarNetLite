using System.Collections.Generic;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.ObjectSync;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;

namespace StellarNet.Lite.Client.Components
{
    public struct PredictedTransformData
    {
        public Vector3 Position;
        public Vector3 Rotation;
        public Vector3 Velocity;
        public Vector3 Scale;
        public float TimeSinceLastSync;
        public float PlaybackSpeed;
    }

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

        private readonly Dictionary<int, SyncEntityData> _entities = new Dictionary<int, SyncEntityData>();
        private float _replayTimeScale = 1f;
        private IUnRegister _timeScaleEventToken;

        public ClientObjectSyncComponent(ClientApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            _entities.Clear();
            _replayTimeScale = 1f;
            _timeScaleEventToken?.UnRegister();
            _timeScaleEventToken = GlobalTypeNetEvent.Register<Local_ReplayTimeScaleChanged>(evt => _replayTimeScale = evt.TimeScale);
        }

        public override void OnDestroy()
        {
            ClearAllEntities(false);
            _timeScaleEventToken?.UnRegister();
            _timeScaleEventToken = null;
        }

        [NetHandler]
        public void OnS2C_ObjectSpawn(S2C_ObjectSpawn msg) => ApplySpawnState(msg.State, true);

        [NetHandler]
        public void OnS2C_ObjectDestroy(S2C_ObjectDestroy msg)
        {
            if (_entities.Remove(msg.NetId)) Room.NetEventSystem.Broadcast(new Local_ObjectDestroyed { NetId = msg.NetId });
        }

        [NetHandler]
        public void OnS2C_ObjectSync(S2C_ObjectSync msg)
        {
            if (msg.States == null) return;

            float currentLocalTime = Time.realtimeSinceStartup;
            int count = Mathf.Min(msg.ValidCount, msg.States.Length);

            for (int i = 0; i < count; i++)
            {
                ObjectSyncState state = msg.States[i];
                if (!_entities.TryGetValue(state.NetId, out SyncEntityData data)) continue;

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

        public void ApplyReplaySnapshot(ObjectSpawnState[] states)
        {
            ClearAllEntities(true);
            if (states == null) return;
            for (int i = 0; i < states.Length; i++) ApplySpawnState(states[i], true);
        }

        public void ClearAllEntities(bool broadcastDestroyEvent)
        {
            if (_entities.Count == 0) return;
            if (broadcastDestroyEvent)
            {
                foreach (int netId in _entities.Keys) Room.NetEventSystem.Broadcast(new Local_ObjectDestroyed { NetId = netId });
            }

            _entities.Clear();
        }

        // 获取当前所有实体状态（用于 UI 延迟打开时的补发）
        public List<ObjectSpawnState> GetAllSpawnStates()
        {
            var list = new List<ObjectSpawnState>(_entities.Count);
            foreach (var kvp in _entities)
            {
                var data = kvp.Value;
                list.Add(new ObjectSpawnState
                {
                    NetId = kvp.Key,
                    PrefabHash = data.PrefabHash,
                    Mask = data.Mask,
                    PosX = data.RawPos.x, PosY = data.RawPos.y, PosZ = data.RawPos.z,
                    RotX = data.RawRot.x, RotY = data.RawRot.y, RotZ = data.RawRot.z,
                    DirX = data.RawVel.x, DirY = data.RawVel.y, DirZ = data.RawVel.z,
                    ScaleX = data.RawScale.x, ScaleY = data.RawScale.y, ScaleZ = data.RawScale.z,
                    AnimStateHash = data.AnimStateHash,
                    AnimNormalizedTime = data.AnimNormalizedTime,
                    FloatParam1 = data.FloatParam1, FloatParam2 = data.FloatParam2, FloatParam3 = data.FloatParam3,
                    OwnerSessionId = data.OwnerSessionId
                });
            }

            return list;
        }

        public bool TryGetTransformData(int netId, out PredictedTransformData result)
        {
            if (!_entities.TryGetValue(netId, out SyncEntityData data) || (data.Mask & (byte)EntitySyncMask.Transform) == 0)
            {
                result = default;
                return false;
            }

            float timeSinceLastPacket = Time.realtimeSinceStartup - data.LocalReceiveTime;
            if (_app.State == ClientAppState.ReplayRoom)
            {
                result = new PredictedTransformData
                {
                    Position = data.RawPos, Rotation = data.RawRot, Velocity = data.RawVel, Scale = data.RawScale, TimeSinceLastSync = 0f,
                    PlaybackSpeed = _replayTimeScale
                };
                return true;
            }

            result = new PredictedTransformData
            {
                Position = data.RawPos + (data.RawVel * timeSinceLastPacket), Rotation = data.RawRot, Velocity = data.RawVel, Scale = data.RawScale,
                TimeSinceLastSync = timeSinceLastPacket, PlaybackSpeed = 1f
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

            float timeSinceLastPacket = Time.realtimeSinceStartup - data.LocalReceiveTime;
            if (_app.State == ClientAppState.ReplayRoom)
            {
                result = new PredictedAnimatorData
                {
                    AnimStateHash = data.AnimStateHash, AnimNormalizedTime = data.AnimNormalizedTime, FloatParam1 = data.FloatParam1,
                    FloatParam2 = data.FloatParam2, FloatParam3 = data.FloatParam3, PlaybackSpeed = _replayTimeScale, ServerTimeDelta = 0f
                };
                return true;
            }

            result = new PredictedAnimatorData
            {
                AnimStateHash = data.AnimStateHash, AnimNormalizedTime = data.AnimNormalizedTime, FloatParam1 = data.FloatParam1,
                FloatParam2 = data.FloatParam2, FloatParam3 = data.FloatParam3, PlaybackSpeed = 1f, ServerTimeDelta = timeSinceLastPacket
            };
            return true;
        }

        private void ApplySpawnState(ObjectSpawnState state, bool broadcastLocalEvent)
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
            data.FloatParam1 = state.FloatParam1;
            data.FloatParam2 = state.FloatParam2;
            data.FloatParam3 = state.FloatParam3;
            data.LocalReceiveTime = Time.realtimeSinceStartup;
            data.ServerTime = 0f;

            if (broadcastLocalEvent) Room.NetEventSystem.Broadcast(new Local_ObjectSpawned { State = state });
        }
    }
}