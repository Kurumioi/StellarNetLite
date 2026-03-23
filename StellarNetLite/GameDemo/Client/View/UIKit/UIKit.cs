using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarNet.View;

namespace StellarNet.UI
{
    /// <summary>
    /// UI 框架核心管理器 (纯同步版)
    /// </summary>
    public class UIKit : MonoSingleton<UIKit>
    {
        private const string RELATIVE_ROOT_PATH = "UIPanel/UIRoot";
        private const string RELATIVE_PANEL_PREFIX = "UIPanel/";

        private bool _isInitialized;

        private readonly Dictionary<UIPanelBase.PanelLayer, Transform> _layers =
            new Dictionary<UIPanelBase.PanelLayer, Transform>();

        private readonly Dictionary<Type, UIPanelBase> _panelCache = new Dictionary<Type, UIPanelBase>();

        public Canvas RootCanvas { get; private set; }
        public CanvasScaler RootScaler { get; private set; }
        public Camera UICamera { get; private set; }

        public void Init()
        {
            if (_isInitialized) return;

            GameObject rootPrefab = Resources.Load<GameObject>(RELATIVE_ROOT_PATH);
            if (rootPrefab == null)
            {
                Debug.LogError($"[UIKit] 初始化失败: 无法在 Resources/{RELATIVE_ROOT_PATH} 找到 UIRoot 预制体");
                return;
            }

            SetupUIRoot(rootPrefab);
            _isInitialized = true;
        }

        private void SetupUIRoot(GameObject rootPrefab)
        {
            GameObject rootGo = Instantiate(rootPrefab);
            rootGo.name = "UIRoot";
            rootGo.transform.SetParent(transform);

            RootCanvas = rootGo.GetComponent<Canvas>();
            RootScaler = rootGo.GetComponent<CanvasScaler>();
            UICamera = rootGo.GetComponentInChildren<Camera>();

            foreach (UIPanelBase.PanelLayer layer in Enum.GetValues(typeof(UIPanelBase.PanelLayer)))
            {
                string layerName = layer.ToString();
                Transform layerTrans = rootGo.transform.Find(layerName);
                if (layerTrans == null)
                {
                    GameObject go = new GameObject(layerName);
                    RectTransform rt = go.AddComponent<RectTransform>();
                    layerTrans = rt.transform;
                    layerTrans.SetParent(rootGo.transform, false);

                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                }

                layerTrans.SetSiblingIndex((int)layer);
                _layers[layer] = layerTrans;
            }
        }

        public static void OpenPanel<T>(object uiData = null) where T : UIPanelBase
        {
            Instance.OpenPanelInternal(typeof(T), uiData);
        }

        public static void ClosePanel<T>() where T : UIPanelBase
        {
            Instance.ClosePanelInternal(typeof(T));
        }

        public static void ClosePanel(Type type)
        {
            Instance.ClosePanelInternal(type);
        }

        public static T GetPanel<T>() where T : UIPanelBase
        {
            if (Instance._panelCache.TryGetValue(typeof(T), out var panel))
                return panel as T;
            return null;
        }

        public static void CloseAllPanels()
        {
            List<Type> keys = new List<Type>(Instance._panelCache.Keys);
            foreach (var type in keys)
            {
                Instance.ClosePanelInternal(type);
            }
        }

        private void OpenPanelInternal(Type type, object uiData)
        {
            if (!_isInitialized) Init();

            if (_panelCache.TryGetValue(type, out var cachedPanel))
            {
                if (cachedPanel.gameObject.activeSelf)
                {
                    cachedPanel.transform.SetAsLastSibling();
                    cachedPanel.RefreshData(uiData);
                }
                else
                {
                    cachedPanel.OnOpen(uiData);
                }

                return;
            }

            string path = $"{RELATIVE_PANEL_PREFIX}{type.Name}";
            GameObject prefab = Resources.Load<GameObject>(path);

            if (prefab == null)
            {
                Debug.LogError($"[UIKit] 同步资源加载失败: {path}");
                return;
            }

            GameObject go = Instantiate(prefab);
            UIPanelBase panel = go.GetComponent(type) as UIPanelBase;

            if (panel == null)
            {
                Debug.LogError($"[UIKit] 预制体 {type.Name} 未挂载对应的脚本!");
                Destroy(go);
                return;
            }

            go.name = type.Name;
            Transform parent = _layers.ContainsKey(panel.Layer)
                ? _layers[panel.Layer]
                : _layers[UIPanelBase.PanelLayer.Middle];
            go.transform.SetParent(parent, false);

            RectTransform rt = panel.RectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            go.SetActive(false);
            panel.OnInit();

            _panelCache[type] = panel;
            panel.OnOpen(uiData);
        }

        private void ClosePanelInternal(Type type)
        {
            if (_panelCache.TryGetValue(type, out var panel))
            {
                if (panel.gameObject.activeSelf)
                {
                    panel.OnClose();
                    if (panel.DestroyOnClose)
                    {
                        Destroy(panel.gameObject);
                        _panelCache.Remove(type);
                    }
                }
            }
        }
    }
}