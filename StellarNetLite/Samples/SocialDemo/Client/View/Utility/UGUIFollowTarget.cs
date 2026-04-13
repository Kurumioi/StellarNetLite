using UnityEngine;

namespace UGUIFollow
{
    /// <summary>
    /// 跟随模式。
    /// </summary>
    public enum FollowMode
    {
        Direct, // 直接跟随：适用于血条、名字板等需要严格对齐的元素
        Smooth // 平滑跟随：适用于交互提示、动态标签等需要阻尼缓冲的元素
    }

    /// <summary>
    /// 世界目标对应的 UGUI 跟随组件。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class UGUIFollowTarget : MonoBehaviour
    {
        [Header("核心引用")] [Tooltip("需要跟随的3D世界目标")]
        public Transform targetTransform;

        [Tooltip("UI所在的Canvas，用于判断渲染模式")] public Canvas parentCanvas;
        [Tooltip("用于计算坐标转换的相机，通常为世界主相机")] public Camera worldCamera;

        [Header("跟随配置")] public FollowMode followMode = FollowMode.Direct;
        [Tooltip("世界坐标系下的偏移量（例如头顶偏移）")] public Vector3 worldOffset = Vector3.zero;
        [Tooltip("屏幕坐标系下的偏移量")] public Vector2 screenOffset = Vector2.zero;

        [Header("平滑配置")] [Tooltip("平滑跟随的插值速度")]
        public float smoothSpeed = 10f;

        [Header("表现配置")] [Tooltip("当目标在相机背后时，是否自动隐藏UI")]
        public bool autoHideBehindCamera = true;

        [Tooltip("控制显示的CanvasGroup（可选，强烈建议挂载以优化性能）")]
        public CanvasGroup canvasGroup;

        // 运行时缓存。
        private RectTransform _rectTransform;
        private RectTransform _canvasRectTransform;
        private bool _isInitialized = false;
        private Vector2 _targetAnchoredPosition;

        // 暴露给业务层的初始化接口，业务层在实例化 UI 后主动调用。
        public void Init()
        {
            _rectTransform = GetComponent<RectTransform>();
            if (_rectTransform == null)
            {
                Debug.LogError($"[UGUIFollowTarget] 初始化失败: 物体 {gameObject.name} 缺失 RectTransform 组件。");
                return;
            }

            if (parentCanvas == null)
            {
                parentCanvas = GetComponentInParent<Canvas>();
                if (parentCanvas == null)
                {
                    Debug.LogError($"[UGUIFollowTarget] 初始化失败: 物体 {gameObject.name} 无法在父节点中找到 Canvas 组件。");
                    return;
                }
            }

            _canvasRectTransform = parentCanvas.GetComponent<RectTransform>();
            if (_canvasRectTransform == null)
            {
                Debug.LogError($"[UGUIFollowTarget] 初始化失败: Canvas {parentCanvas.name} 缺失 RectTransform 组件。");
                return;
            }

            if (worldCamera == null)
            {
                worldCamera = Camera.main;
                if (worldCamera == null)
                {
                    Debug.LogError($"[UGUIFollowTarget] 初始化失败: 物体 {gameObject.name} 未指定 worldCamera 且场景中无主相机。");
                    return;
                }
            }

            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }

            // 初始化成功后交给全局 FollowManager 统一驱动。
            _isInitialized = true;
            UGUIFollowManager.Instance.Register(this);
        }

        private void OnDestroy()
        {
            if (UGUIFollowManager.HasInstance)
            {
                UGUIFollowManager.Instance.Unregister(this);
            }
        }

        // 由 UGUIFollowManager 统一驱动，不使用 Unity 内置的 Update。
        public void OnUpdatePosition(float deltaTime)
        {
            if (!_isInitialized)
            {
                return;
            }

            // 若目标丢失，自动隐藏 UI 并中断计算。
            if (targetTransform == null)
            {
                SetVisibility(false);
                return;
            }

            Vector3 worldPos = targetTransform.position + worldOffset;

            // 通过点乘判断目标是否处于相机背后。
            Vector3 toTarget = worldPos - worldCamera.transform.position;
            bool isBehindCamera = Vector3.Dot(worldCamera.transform.forward, toTarget) < 0f;

            if (autoHideBehindCamera && isBehindCamera)
            {
                SetVisibility(false);
                return;
            }

            SetVisibility(true);

            // 将 3D 世界坐标转换为屏幕空间坐标。
            Vector2 screenPoint = worldCamera.WorldToScreenPoint(worldPos);
            screenPoint += screenOffset;

            // 根据 Canvas 渲染模式决定是否需要 UI Camera。
            Camera uiCamera = parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : parentCanvas.worldCamera;

            // 将屏幕坐标转换为 Canvas 局部坐标系下的 AnchoredPosition。
            bool success = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRectTransform,
                screenPoint,
                uiCamera,
                out Vector2 localPoint);

            if (!success)
            {
                return;
            }

            _targetAnchoredPosition = localPoint;

            // 根据跟随模式决定是硬跟随还是平滑跟随。
            if (followMode == FollowMode.Direct)
            {
                _rectTransform.anchoredPosition = _targetAnchoredPosition;
            }
            else if (followMode == FollowMode.Smooth)
            {
                _rectTransform.anchoredPosition = Vector2.Lerp(_rectTransform.anchoredPosition, _targetAnchoredPosition, deltaTime * smoothSpeed);
            }
        }

        // 统一的可见性控制接口，优先使用 CanvasGroup。
        private void SetVisibility(bool isVisible)
        {
            if (canvasGroup != null)
            {
                float targetAlpha = isVisible ? 1f : 0f;
                if (Mathf.Abs(canvasGroup.alpha - targetAlpha) > 0.01f)
                {
                    canvasGroup.alpha = targetAlpha;
                    canvasGroup.interactable = isVisible;
                    canvasGroup.blocksRaycasts = isVisible;
                }
            }
            else
            {
                // 退化处理：若未挂载 CanvasGroup，则回退至 GameObject 显隐控制。
                if (gameObject.activeSelf != isVisible)
                {
                    gameObject.SetActive(isVisible);
                }
            }
        }
    }
}
