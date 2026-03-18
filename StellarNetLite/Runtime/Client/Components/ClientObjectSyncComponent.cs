using System.Collections.Generic;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;

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

        private class SyncEntityData
        {
            public byte Mask;

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
            _timeScaleEventToken = GlobalTypeNetEvent.Register<Local_ReplayTimeScaleChanged>(evt => _replayTimeScale = evt.TimeScale);
            NetLogger.LogInfo("ClientObjectSync", "空间同步服务初始化完毕，等待实体生成");
        }

        public override void OnDestroy()
        {
            _entities.Clear();
            _timeScaleEventToken?.UnRegister();
        }

        [NetHandler]
        public void OnS2C_ObjectSpawn(S2C_ObjectSpawn msg)
        {
            if (msg == null) return;

            if (!_entities.TryGetValue(msg.NetId, out var data))
            {
                data = new SyncEntityData();
                _entities[msg.NetId] = data;
            }

            data.Mask = msg.Mask;

            if ((msg.Mask & (byte)EntitySyncMask.Transform) != 0)
            {
                data.RawPos = new Vector3(msg.PosX, msg.PosY, msg.PosZ);
                data.RawRot = new Vector3(msg.RotX, msg.RotY, msg.RotZ);
                data.RawVel = new Vector3(msg.DirX, msg.DirY, msg.DirZ);
                data.RawScale = new Vector3(msg.ScaleX, msg.ScaleY, msg.ScaleZ);
            }

            if ((msg.Mask & (byte)EntitySyncMask.Animator) != 0)
            {
                data.AnimStateHash = msg.AnimStateHash;
                data.AnimNormalizedTime = msg.AnimNormalizedTime;
                data.FloatParam1 = msg.FloatParam1;
                data.FloatParam2 = msg.FloatParam2;
                data.FloatParam3 = msg.FloatParam3;
            }

            data.LocalReceiveTime = Time.realtimeSinceStartup;
            data.ServerTime = 0f;

            var spawnEvent = new Local_ObjectSpawned
            {
                NetId = msg.NetId,
                PrefabHash = msg.PrefabHash,
                Mask = msg.Mask,
                PosX = msg.PosX, PosY = msg.PosY, PosZ = msg.PosZ,
                RotX = msg.RotX, RotY = msg.RotY, RotZ = msg.RotZ,
                DirX = msg.DirX, DirY = msg.DirY, DirZ = msg.DirZ,
                ScaleX = msg.ScaleX, ScaleY = msg.ScaleY, ScaleZ = msg.ScaleZ,
                AnimStateHash = msg.AnimStateHash,
                AnimNormalizedTime = msg.AnimNormalizedTime,
                FloatParam1 = msg.FloatParam1,
                FloatParam2 = msg.FloatParam2,
                FloatParam3 = msg.FloatParam3,
                OwnerSessionId = msg.OwnerSessionId
            };

            Room.NetEventSystem.Broadcast(spawnEvent);
        }

        [NetHandler]
        public void OnS2C_ObjectDestroy(S2C_ObjectDestroy msg)
        {
            if (msg == null) return;

            if (_entities.Remove(msg.NetId))
            {
                var destroyEvent = new Local_ObjectDestroyed { NetId = msg.NetId };
                Room.NetEventSystem.Broadcast(destroyEvent);
            }
        }

        [NetHandler]
        public void OnS2C_ObjectSync(S2C_ObjectSync msg)
        {
            if (msg == null || msg.States == null) return;

            float currentLocalTime = Time.realtimeSinceStartup;
            int count = Mathf.Min(msg.ValidCount, msg.States.Length);

            for (int i = 0; i < count; i++)
            {
                var state = msg.States[i];
                if (_entities.TryGetValue(state.NetId, out var data))
                {
                    // 核心逻辑：根据 Mask 按需更新缓存数据
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
        }

        public bool TryGetTransformData(int netId, out PredictedTransformData result)
        {
            if (!_entities.TryGetValue(netId, out var data) || (data.Mask & (byte)EntitySyncMask.Transform) == 0)
            {
                result = default;
                return false;
            }

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
            if (!_entities.TryGetValue(netId, out var data) || (data.Mask & (byte)EntitySyncMask.Animator) == 0)
            {
                result = default;
                return false;
            }

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
    }
}