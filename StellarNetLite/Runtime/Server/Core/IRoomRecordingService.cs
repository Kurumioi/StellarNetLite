using System.Collections.Generic;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Core
{
    /// <summary>
    /// 房间录制服务接口。
    /// Runtime 只感知“房间消息与状态可被记录”，不直接依赖具体回放实现。
    /// </summary>
    public interface IRoomRecordingService
    {
        bool CanStartRecording(Room room);
        void StartRecording(Room room);
        string StopRecordingAndSave(Room room, string displayName, int totalTicks, int[] componentIds, int recordedTickRate);
        void AbortRecording(Room room);
        void RecordMessage(Room room, int relativeTick, int msgId, byte[] payload, int payloadLength);
        void RecordInitialSnapshot(Room room, IReadOnlyList<ServerRoomComponent> components, int relativeTick);
        void RecordPeriodicSnapshot(Room room, IReadOnlyList<ServerRoomComponent> components, int relativeTick);
        void RecordFinalSnapshot(Room room, IReadOnlyList<ServerRoomComponent> components, int relativeTick);
    }
}
