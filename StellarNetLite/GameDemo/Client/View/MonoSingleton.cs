using UnityEngine;

namespace StellarNet.View
{
    /// <summary>
    /// 泛型 Mono 单例基类
    /// 确保全局唯一性，支持自动创建或手动挂载
    /// </summary>
    public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
    {
        // 当前单例实例。
        private static T _instance;
        // 惰性初始化锁。
        private static readonly object _lock = new object();
        // 程序退出时禁止再次创建单例。
        private static bool _applicationIsQuitting = false;

        public static T Instance
        {
            get
            {
                if (_applicationIsQuitting)
                {
                    Debug.LogWarning($"[MonoSingleton] {typeof(T)} 实例已在程序退出时销毁，不再返回。");
                    return null;
                }

                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = (T)FindObjectOfType(typeof(T));

                        // 场景里不存在时自动创建一个常驻节点。
                        if (_instance == null)
                        {
                            GameObject singleton = new GameObject();
                            _instance = singleton.AddComponent<T>();
                            singleton.name = $"(Singleton) {typeof(T)}";
                            DontDestroyOnLoad(singleton);
                        }
                    }

                    return _instance;
                }
            }
        }

        protected virtual void Awake()
        {
            // 第一次挂载的实例成为正式单例。
            if (_instance == null)
            {
                _instance = this as T;
                DontDestroyOnLoad(gameObject);
            }
            // 防止场景里出现重复副本。
            else if (_instance != this)
            {
                Debug.LogWarning($"[MonoSingleton] 场景中存在重复单例: {typeof(T)}，已销毁副本。");
                Destroy(gameObject);
            }
        }

        protected virtual void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        protected virtual void OnApplicationQuit()
        {
            _applicationIsQuitting = true;
        }
    }
}
