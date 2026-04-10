using TMPro;
using UnityEngine;

/// <summary>
/// 聊天气泡项。
/// </summary>
public class SocialRoomBubbleItem : MonoBehaviour
{
    /// <summary>
    /// 用于显示聊天内容的文本组件。
    /// </summary>
    [Header("UI 引用")]
    [Tooltip("用于显示聊天内容的文本组件")]
    public TextMeshProUGUI contentText;

    // 剩余显示时间。
    private float _remainTime;
    private bool _isActive;

    /// <summary>
    /// 初始化气泡内容和持续时间。
    /// </summary>
    public void Init(string content, float duration)
    {
        // 先确保文本组件可用，再复用更新逻辑。
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

    /// <summary>
    /// 更新气泡内容和持续时间。
    /// </summary>
    public void UpdateContent(string content, float duration)
    {
        // 刷新文本并重置寿命。
        if (contentText != null)
        {
            contentText.text = content;
        }

        _remainTime = duration;
        _isActive = true;
    }

    /// <summary>
    /// 按帧刷新气泡生命周期。
    /// </summary>
    private void Update()
    {
        if (!_isActive) return;

        _remainTime -= Time.deltaTime;

        // 气泡到时自动销毁。
        if (_remainTime <= 0f)
        {
            _isActive = false;
            Destroy(gameObject);
        }
    }
}
