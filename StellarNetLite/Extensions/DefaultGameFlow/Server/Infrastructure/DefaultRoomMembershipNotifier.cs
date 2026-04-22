using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Server.Infrastructure
{
    /// <summary>
    /// 默认房间成员通知器。
    /// 由 DefaultGameFlow 扩展提供，把强制离房通知转换为默认协议。
    /// </summary>
    public sealed class DefaultRoomMembershipNotifier : IRoomMembershipNotifier
    {
        public static DefaultRoomMembershipNotifier Instance { get; } = new DefaultRoomMembershipNotifier();

        private DefaultRoomMembershipNotifier()
        {
        }

        public void NotifyForcedLeave(Room room, Session session)
        {
            if (room == null || session == null)
            {
                return;
            }

            room.SendMessageTo(session, new S2C_LeaveRoomResult { Success = true });
        }
    }
}
