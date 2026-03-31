namespace StellarNet.Lite.Client.Core
{
    /// <summary>
    /// 客户端房间组件基类 (ClientRoomComponent)
    /// 职责：定义客户端房间表现与状态同步模块的生命周期。
    /// </summary>
    public abstract class ClientRoomComponent
    {
        // 当前组件所属的客户端房间。
        public ClientRoom Room { get; internal set; }

        // 房间组件初始化入口。
        public virtual void OnInit()
        {
        }

        // 房间组件销毁入口。
        public virtual void OnDestroy()
        {
        }
    }
}
