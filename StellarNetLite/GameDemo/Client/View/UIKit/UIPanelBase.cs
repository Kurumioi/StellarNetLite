using UnityEngine;

namespace StellarNet.UI
{
    /// <summary>
    /// 所有 UI 面板的抽象基类
    /// 纯同步设计，负责生命周期管理与基础组件缓存
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UIPanelBase : MonoBehaviour
    {
        // 面板挂载层级。
        public enum PanelLayer
        {
            Bottom = 0,
            Middle = 1,
            Top = 2,
            Popup = 3,
            System = 4
        }

        [Header("UI Settings")] [SerializeField]
        protected PanelLayer layer = PanelLayer.Middle;

        // 关闭时是否直接销毁实例。
        [SerializeField] protected bool destroyOnClose = false;

        // 惰性缓存常用组件。
        private CanvasGroup _canvasGroup;
        private RectTransform _rectTransform;

        public PanelLayer Layer => layer;
        public bool DestroyOnClose => destroyOnClose;

        public CanvasGroup CanvasGroup
        {
            get
            {
                if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
                return _canvasGroup;
            }
        }

        public RectTransform RectTransform
        {
            get
            {
                if (_rectTransform == null) _rectTransform = GetComponent<RectTransform>();
                return _rectTransform;
            }
        }

        /// <summary>
        /// 面板初次实例化时的初始化逻辑
        /// </summary>
        public virtual void OnInit()
        {
        }

        /// <summary>
        /// 打开面板的同步逻辑流
        /// </summary>
        public virtual void OnOpen(object uiData = null)
        {
            // 打开时统一恢复交互和显隐状态。
            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            CanvasGroup.alpha = 1;
            CanvasGroup.interactable = true;
            CanvasGroup.blocksRaycasts = true;

            RefreshData(uiData);
        }

        /// <summary>
        /// 刷新面板数据，不触发入场逻辑
        /// </summary>
        public virtual void RefreshData(object uiData = null)
        {
        }

        /// <summary>
        /// 关闭面板的同步逻辑流
        /// </summary>
        public virtual void OnClose()
        {
            // 基类只做隐藏，不销毁实例。
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 快捷关闭自身
        /// </summary>
        protected void CloseSelf()
        {
            UIKit.ClosePanel(this.GetType());
        }
    }
}
