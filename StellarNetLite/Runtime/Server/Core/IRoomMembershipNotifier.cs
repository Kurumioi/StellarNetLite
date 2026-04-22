namespace StellarNet.Lite.Server.Core
{
    /// <summary>
    /// 房间成员通知器。
    /// Runtime 只声明“房间系统需要通知某个成员被强制移出”，具体协议由扩展实现。
    /// </summary>
    public interface IRoomMembershipNotifier
    {
        void NotifyForcedLeave(Room room, Session session);
    }
}
