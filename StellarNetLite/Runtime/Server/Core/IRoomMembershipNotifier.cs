namespace StellarNet.Lite.Server.Core
{
    /// <summary>
    /// 房间成员通知器。
    /// Runtime 只声明“房间系统需要通知某个成员被强制移出”，具体协议由扩展实现。
    /// </summary>
    public interface IRoomMembershipNotifier
    {
        /// <summary>
        /// 通知指定成员被房间强制移出。
        /// </summary>
        void NotifyForcedLeave(Room room, Session session);
    }
}
