using TMPro;
using UnityEngine;

/// <summary>
/// 纯粹的业务表现层脚本：仅负责气泡文本的渲染与生命周期倒计时销毁。
/// 坐标跟随职责已完全剥离至 UGUIFollowTarget。
/// </summary>
public class SocialRoomBubbleItem : MonoBehaviour
{
    [Header("UI 引用")] [Tooltip("用于显示聊天内容的文本组件")]
    public TextMeshProUGUI contentText;

    private float _remainTime;
    private bool _isActive;

    public void Init(string content, float duration)
    {
        if (contentText == null)
        {
            contentText = GetComponentInChildren<TextMeshProUGUI>();
            if (contentText == null)
            {
                Debug.LogError($"[SocialRoomBubbleItem] 初始化失败: 预制体 {gameObject.name} 及其子节点缺失 TextMeshProUGUI 组件。");
                return;
            }
        }

        UpdateContent(content, duration);
    }

    public void UpdateContent(string content, float duration)
    {
        if (contentText != null)
        {
            contentText.text = content;
        }

        _remainTime = duration;
        _isActive = true;
    }

    private void Update()
    {
        if (!_isActive) return;

        _remainTime -= Time.deltaTime;

        if (_remainTime <= 0f)
        {
            _isActive = false;
            Destroy(gameObject);
        }
    }
}