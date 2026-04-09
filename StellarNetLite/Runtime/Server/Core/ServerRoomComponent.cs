namespace StellarNet.Lite.Server.Core
{
    /// <summary>
    /// 服务端房间组件基类。
    /// 所有房间业务都通过覆写这些生命周期钩子接入。
    /// </summary>
    public abstract class ServerRoomComponent
    {
        // 当前组件所属房间，由工厂装配时注入。
        public Room Room { get; internal set; }

        // 组件初始化。
        public virtual void OnInit()
        {
        }

        // 组件销毁收尾。
        public virtual void OnDestroy()
        {
        }

        // 成员正式加入房间。
        public virtual void OnMemberJoined(Session session)
        {
        }

        // 成员正式离开房间。
        public virtual void OnMemberLeft(Session session)
        {
        }

        // 成员物理离线。
        public virtual void OnMemberOffline(Session session)
        {
        }

        // 成员物理恢复在线。
        public virtual void OnMemberOnline(Session session)
        {
        }

        // 向某个成员补发重连快照。
        public virtual void OnSendSnapshot(Session session)
        {
        }

        // 对局开始回调。
        public virtual void OnGameStart()
        {
        }

        // 对局结束回调。
        public virtual void OnGameEnd()
        {
        }
    }
}
