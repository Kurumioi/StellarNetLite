using System;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 录像列表项组件。
/// </summary>
public class Panel_StellarNetLobby_ReplayItem : MonoBehaviour
{
    /// <summary>
    /// 录像信息文本。
    /// </summary>
    [SerializeField] private TMP_Text infoText;

    /// <summary>
    /// 下载或播放按钮。
    /// </summary>
    [SerializeField] private Button actionBtn;

    /// <summary>
    /// 按钮文案文本。
    /// </summary>
    [SerializeField] private TMP_Text btnText;

    /// <summary>
    /// 下载进度填充图。
    /// </summary>
    [SerializeField] private Image progressSlider;

    /// <summary>
    /// 当前条目对应的录像 Id。
    /// </summary>
    private string _replayId;

    /// <summary>
    /// 当前录像显示名。
    /// </summary>
    private string _displayName;

    /// <summary>
    /// 是否正在下载当前录像。
    /// </summary>
    private bool _isDownloading;

    /// <summary>
    /// 初始化录像列表项。
    /// </summary>
    public void Init(ReplayBriefInfo info, bool isLocalCached)
    {
        if (info == null) return;

        _replayId = info.ReplayId;
        _displayName = info.DisplayName;
        _isDownloading = false;

        DateTime dt = DateTimeOffset.FromUnixTimeSeconds(info.Timestamp).LocalDateTime;
        if (infoText != null)
        {
            infoText.text = $"[{dt:MM-dd HH:mm}] {_displayName}";
        }

        if (progressSlider != null)
        {
            progressSlider.gameObject.SetActive(false);
            progressSlider.fillAmount = 0f;
        }

        if (btnText != null)
        {
            btnText.text = isLocalCached ? "播放" : "下载";
        }

        if (actionBtn != null)
        {
            actionBtn.onClick.RemoveAllListeners();
            actionBtn.onClick.AddListener(OnActionBtnClick);
        }

        // 列表项只关心自己的下载进度和结果。
        GlobalTypeNetEvent.Register<Local_ReplayDownloadProgress>(OnDownloadProgress)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
        GlobalTypeNetEvent.Register<S2C_DownloadReplayResult>(OnDownloadResult)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
    }

    /// <summary>
    /// 处理按钮点击。
    /// </summary>
    private void OnActionBtnClick()
    {
        if (_isDownloading) return;

        if (btnText != null && btnText.text == "播放")
        {
            // 统一复用下载入口；命中本地缓存时会立即广播成功并跳转回放。
            StellarNet.Lite.Client.Modules.ClientReplayModule.RequestDownload(NetClient.App, _replayId);
            return;
        }

        _isDownloading = true;
        if (btnText != null) btnText.text = "下载中...";
        if (progressSlider != null)
        {
            progressSlider.gameObject.SetActive(true);
            progressSlider.fillAmount = 0f;
        }

        StellarNet.Lite.Client.Modules.ClientReplayModule.RequestDownload(NetClient.App, _replayId);
    }

    /// <summary>
    /// 处理下载进度事件。
    /// </summary>
    private void OnDownloadProgress(Local_ReplayDownloadProgress evt)
    {
        if (evt.ReplayId != _replayId) return;

        if (progressSlider != null)
        {
            if (!progressSlider.gameObject.activeSelf) progressSlider.gameObject.SetActive(true);
            progressSlider.fillAmount = evt.TotalBytes > 0 ? (float)evt.DownloadedBytes / evt.TotalBytes : 0f;
        }

        if (btnText != null)
        {
            // 按百分比更新按钮文案。
            float percent = evt.TotalBytes > 0 ? ((float)evt.DownloadedBytes / evt.TotalBytes) * 100f : 0f;
            btnText.text = $"{percent:F1}%";
        }
    }

    /// <summary>
    /// 处理下载结果事件。
    /// </summary>
    private void OnDownloadResult(S2C_DownloadReplayResult msg)
    {
        if (msg.ReplayId != _replayId) return;

        _isDownloading = false;
        if (progressSlider != null) progressSlider.gameObject.SetActive(false);

        if (msg.Success)
        {
            // 下载完成后切为播放态。
            if (btnText != null) btnText.text = "播放";
        }
        else
        {
            // 失败后保留可见提示，等待用户重新触发。
            if (btnText != null) btnText.text = "下载失败";
        }
    }
}
