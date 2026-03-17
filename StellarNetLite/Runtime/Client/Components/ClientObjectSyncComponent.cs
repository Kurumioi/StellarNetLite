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
    /// 供 View 层读取的完美预测数据结构 (值类型，0GC)
    /// </summary>
    public struct PredictedSyncData
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public Vector3 Scale;
        public int AnimStateHash;

        public float AnimNormalizedTime;

        // 距离上次收到同步包流逝的本地时间。
        public float TimeSinceLastSync;

        // 核心新增：回放倍速透传
        public float PlaybackSpeed;
    }

    [RoomComponent(200, "ObjectSync", "空间与动画同步核心服务")]
    public sealed class ClientObjectSyncComponent : ClientRoomComponent
    {
        private readonly ClientApp _app;

        private class SyncEntityData
        {
            public Vector3 RawPos;
            public Vector3 RawVel;
            public Vector3 RawScale;
            public int AnimStateHash;
            public float AnimNormalizedTime;
            public float LocalReceiveTime;
        }

        private readonly Dictionary<int, SyncEntityData> _entities = new Dictionary<int, SyncEntityData>();

        // 缓存当前的回放倍速
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
            NetLogger.LogInfo("[ClientObjectSync]", "空间同步服务初始化完毕，等待实体生成");
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

            data.RawPos = new Vector3(msg.PosX, msg.PosY, msg.PosZ);
            data.RawVel = new Vector3(msg.DirX, msg.DirY, msg.DirZ);
            data.RawScale = new Vector3(msg.ScaleX, msg.ScaleY, msg.ScaleZ);
            data.AnimStateHash = 0;
            data.AnimNormalizedTime = 0f;
            data.LocalReceiveTime = Time.realtimeSinceStartup;

            var spawnEvent = new Local_ObjectSpawned
            {
                NetId = msg.NetId,
                PrefabHash = msg.PrefabHash,
                PosX = msg.PosX, PosY = msg.PosY, PosZ = msg.PosZ,
                DirX = msg.DirX, DirY = msg.DirY, DirZ = msg.DirZ,
                ScaleX = msg.ScaleX, ScaleY = msg.ScaleY, ScaleZ = msg.ScaleZ,
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

            for (int i = 0; i < msg.States.Length; i++)
            {
                var state = msg.States[i];
                if (_entities.TryGetValue(state.NetId, out var data))
                {
                    data.RawPos.x = state.PosX;
                    data.RawPos.y = state.PosY;
                    data.RawPos.z = state.PosZ;
                    data.RawVel.x = state.VelX;
                    data.RawVel.y = state.VelY;
                    data.RawVel.z = state.VelZ;
                    data.RawScale.x = state.ScaleX;
                    data.RawScale.y = state.ScaleY;
                    data.RawScale.z = state.ScaleZ;
                    data.AnimStateHash = state.AnimStateHash;
                    data.AnimNormalizedTime = state.AnimNormalizedTime;
                    data.LocalReceiveTime = currentLocalTime;
                }
            }
        }

        public bool TryGetPredictedData(int netId, out PredictedSyncData result)
        {
            if (!_entities.TryGetValue(netId, out var data))
            {
                result = default;
                return false;
            }

            float timeSinceLastPacket = Time.realtimeSinceStartup - data.LocalReceiveTime;

            if (_app.State == ClientAppState.ReplayRoom)
            {
                result = new PredictedSyncData
                {
                    Position = data.RawPos,
                    Velocity = data.RawVel,
                    Scale = data.RawScale,
                    AnimStateHash = data.AnimStateHash,
                    AnimNormalizedTime = data.AnimNormalizedTime,
                    TimeSinceLastSync = 0f,
                    PlaybackSpeed = _replayTimeScale // 透传当前倍速
                };
                return true;
            }

            Vector3 predictedPos = data.RawPos + (data.RawVel * timeSinceLastPacket);

            result = new PredictedSyncData
            {
                Position = predictedPos,
                Velocity = data.RawVel,
                Scale = data.RawScale,
                AnimStateHash = data.AnimStateHash,
                AnimNormalizedTime = data.AnimNormalizedTime,
                TimeSinceLastSync = timeSinceLastPacket,
                PlaybackSpeed = 1f // 在线模式永远是 1 倍速
            };
            return true;
        }
    }
}