using System.Collections.Generic;
using UnityEngine;

namespace UGUIFollow
{
    /// <summary>
    /// 全局 UGUI 跟随管理器。
    /// 集中驱动所有跟随组件，避免每个对象各自 Update。
    /// </summary>
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

        // 预分配初始容量，降低早期扩容开销。
        private readonly List<UGUIFollowTarget> _targets = new List<UGUIFollowTarget>(128);

        public void Register(UGUIFollowTarget target)
        {
            // 前置拦截非法参数，防止脏数据注入管理队列。
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

        // 在 LateUpdate 中执行，确保相机和动画已经更新完毕。
        private void LateUpdate()
        {
            float dt = Time.deltaTime;

            // 倒序遍历，允许安全移除失效引用。
            for (int i = _targets.Count - 1; i >= 0; i--)
            {
                var target = _targets[i];

                // 目标被销毁时，顺手清理无效引用。
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
