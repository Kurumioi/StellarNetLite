using System.Collections.Generic;
using UnityEngine;

namespace UGUIFollow
{
    // 采用单例模式的全局管理器，负责统一调度所有 UGUI 跟随组件的更新逻辑。
    // 将分散的 Update 集中处理，提升 CPU 缓存命中率并降低引擎回调开销。
    public class UGUIFollowManager : MonoBehaviour
    {
        private static UGUIFollowManager _instance;

        public static UGUIFollowManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("[UGUIFollowManager]");
                    _instance = go.AddComponent<UGUIFollowManager>();
                    DontDestroyOnLoad(go);
                }

                return _instance;
            }
        }

        public static bool HasInstance => _instance != null;

        // 预分配初始容量，防止在游戏初期频繁触发扩容导致的 GC 分配
        private readonly List<UGUIFollowTarget> _targets = new List<UGUIFollowTarget>(128);

        public void Register(UGUIFollowTarget target)
        {
            // 前置拦截非法参数，防止脏数据注入管理队列
            if (target == null)
            {
                Debug.LogError("[UGUIFollowManager] 注册失败: 传入的 target 为空。");
                return;
            }

            if (!_targets.Contains(target))
            {
                _targets.Add(target);
            }
        }

        public void Unregister(UGUIFollowTarget target)
        {
            if (target == null)
            {
                return;
            }

            _targets.Remove(target);
        }

        // 必须在 LateUpdate 中执行，确保所有的相机移动和动画更新已经完成，避免 UI 发生滞后或抖动
        private void LateUpdate()
        {
            float dt = Time.deltaTime;

            // 采用倒序遍历，允许在遍历过程中安全地移除失效的引用
            for (int i = _targets.Count - 1; i >= 0; i--)
            {
                var target = _targets[i];

                // 容错处理：若目标对象已被意外销毁，则清理无效引用并跳过
                if (target == null)
                {
                    _targets.RemoveAt(i);
                    continue;
                }

                target.OnUpdatePosition(dt);
            }
        }
    }
}