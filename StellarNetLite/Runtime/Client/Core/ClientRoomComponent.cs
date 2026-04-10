namespace StellarNet.Lite.Client.Core
{
    /// <summary>
    /// 客户端房间组件基类。
    /// </summary>
    public abstract class ClientRoomComponent
    {
        /// <summary>
        /// 当前组件所属的客户端房间。
        /// </summary>
        public ClientRoom Room { get; internal set; }

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
    }
}
