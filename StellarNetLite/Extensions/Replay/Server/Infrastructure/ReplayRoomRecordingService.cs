using System;
using System.Collections.Generic;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Replay;

namespace StellarNet.Lite.Server.Infrastructure
{
    /// <summary>
    /// 默认房间录像录制服务。
    /// 由 Replay 扩展提供，实现 Runtime 侧的房间录制接口。
    /// </summary>
    public sealed class ReplayRoomRecordingService : IRoomRecordingService
    {
        private const int ReplaySnapshotIntervalTicks = 150;
        public static ReplayRoomRecordingService Instance { get; } = new ReplayRoomRecordingService();

        private ReplayRoomRecordingService()
        {
        }

        public bool CanStartRecording(Room room)
        {
            if (room == null)
            {
                return false;
            }

            if (!ReplayConfigLoader.Current.EnableReplayRecording)
            {
                return false;
            }

            return room.Config.EnableReplayRecording;
        }

        public void StartRecording(Room room)
        {
            if (room == null)
            {
                return;
            }

            ServerReplayStorage.StartRecord(room.RoomId);
        }

        public string StopRecordingAndSave(Room room, string displayName, int totalTicks, int[] componentIds, int recordedTickRate)
        {
            if (room == null)
            {
                return string.Empty;
            }

            string replayId = Guid.NewGuid().ToString("N");
            ServerReplayStorage.StopRecordAndSave(
                room.RoomId,
                replayId,
                displayName,
                componentIds,
                recordedTickRate,
                ReplayConfigLoader.Current.MaxReplayFiles,
                totalTicks);
            return replayId;
        }

        public void AbortRecording(Room room)
        {
            if (room == null)
            {
                return;
            }

            ServerReplayStorage.AbortRecord(room.RoomId);
        }

        public void RecordMessage(Room room, int relativeTick, int msgId, byte[] payload, int payloadLength)
        {
            if (room == null || relativeTick < 0 || payload == null || payloadLength <= 0)
            {
                return;
            }

            ServerReplayStorage.RecordFrame(room.RoomId, relativeTick, msgId, payload, payloadLength);
        }

        public void RecordInitialSnapshot(Room room, IReadOnlyList<ServerRoomComponent> components, int relativeTick)
        {
            RecordSnapshot(room, components, relativeTick);
        }

        public void RecordPeriodicSnapshot(Room room, IReadOnlyList<ServerRoomComponent> components, int relativeTick)
        {
            if (relativeTick <= 0 || relativeTick % ReplaySnapshotIntervalTicks != 0)
            {
                return;
            }

            RecordSnapshot(room, components, relativeTick);
        }

        public void RecordFinalSnapshot(Room room, IReadOnlyList<ServerRoomComponent> components, int relativeTick)
        {
            RecordSnapshot(room, components, relativeTick);
        }

        private static void RecordSnapshot(Room room, IReadOnlyList<ServerRoomComponent> components, int relativeTick)
        {
            if (room == null || components == null || relativeTick < 0)
            {
                return;
            }

            var snapshots = new List<ComponentSnapshotData>();
            for (int i = 0; i < components.Count; i++)
            {
                if (!(components[i] is IReplaySnapshotProvider provider))
                {
                    continue;
                }

                byte[] payload = provider.ExportSnapshot();
                if (payload == null)
                {
                    continue;
                }

                snapshots.Add(new ComponentSnapshotData
                {
                    ComponentId = provider.SnapshotComponentId,
                    Payload = payload
                });
            }

            if (snapshots.Count <= 0)
            {
                return;
            }

            ServerReplayStorage.RecordSnapshotFrame(room.RoomId, new ReplaySnapshotFrame
            {
                Tick = relativeTick,
                ComponentSnapshots = snapshots.ToArray()
            });
        }
    }
}
