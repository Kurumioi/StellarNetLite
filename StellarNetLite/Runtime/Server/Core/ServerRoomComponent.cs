namespace StellarNet.Lite.Server.Core
{
    /// <summary>
    /// 服务端房间组件基类。
    /// </summary>
    public abstract class ServerRoomComponent
    {
        /// <summary>
        /// 当前组件所属房间。
        /// </summary>
        public Room Room { get; internal set; }

        /// <summary>
        /// 组件初始化回调。
        /// </summary>
        public virtual void OnInit()
        {
        }

        /// <summary>
        /// 组件销毁回调。
        /// </summary>
        public virtual void OnDestroy()
        {
        }

        /// <summary>
        /// 成员正式加入房间时回调。
        /// </summary>
        public virtual void OnMemberJoined(Session session)
        {
        }

        /// <summary>
        /// 成员正式离开房间时回调。
        /// </summary>
        public virtual void OnMemberLeft(Session session)
        {
        }

        /// <summary>
        /// 成员物理离线时回调。
        /// </summary>
        public virtual void OnMemberOffline(Session session)
        {
        }

        /// <summary>
        /// 成员物理恢复在线时回调。
        /// </summary>
        public virtual void OnMemberOnline(Session session)
        {
        }

        /// <summary>
        /// 向某个成员补发重连快照。
        /// </summary>
        public virtual void OnSendSnapshot(Session session)
        {
        }

        /// <summary>
        /// 对局开始时回调。
        /// </summary>
        public virtual void OnGameStart()
        {
        }

        /// <summary>
        /// 对局结束时回调。
        /// </summary>
        public virtual void OnGameEnd()
        {
        }
    }
}
