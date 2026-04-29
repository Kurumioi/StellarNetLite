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
        /// <summary>
        /// 判断当前房间是否允许开启录制。
        /// </summary>
        bool CanStartRecording(Room room);

        /// <summary>
        /// 启动房间录制。
        /// </summary>
        void StartRecording(Room room);

        /// <summary>
        /// 停止录制并落盘保存。
        /// 返回生成的录像 Id。
        /// </summary>
        string StopRecordingAndSave(Room room, string displayName, int totalTicks, int[] componentIds, int recordedTickRate);

        /// <summary>
        /// 放弃当前录制并清理中间状态。
        /// </summary>
        void AbortRecording(Room room);

        /// <summary>
        /// 记录一条房间消息。
        /// </summary>
        void RecordMessage(Room room, int relativeTick, int msgId, byte[] payload, int payloadLength);

        /// <summary>
        /// 记录初始快照。
        /// </summary>
        void RecordInitialSnapshot(Room room, IReadOnlyList<ServerRoomComponent> components, int relativeTick);

        /// <summary>
        /// 记录周期快照。
        /// </summary>
        void RecordPeriodicSnapshot(Room room, IReadOnlyList<ServerRoomComponent> components, int relativeTick);

        /// <summary>
        /// 记录结束前的最终快照。
        /// </summary>
        void RecordFinalSnapshot(Room room, IReadOnlyList<ServerRoomComponent> components, int relativeTick);
    }
}
