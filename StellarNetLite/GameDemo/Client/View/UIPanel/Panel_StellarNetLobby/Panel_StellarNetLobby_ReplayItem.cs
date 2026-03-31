using System;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 录像列表项组件。
/// 职责：独立管理单个录像的展示、下载进度监听与状态刷新，实现表现层高内聚。
/// </summary>
public class Panel_StellarNetLobby_ReplayItem : MonoBehaviour
{
    // 录像基础展示。
    [SerializeField] private TMP_Text infoText;
    // 下载/播放入口按钮。
    [SerializeField] private Button actionBtn;
    [SerializeField] private TMP_Text btnText;
    // 下载进度条。
    [SerializeField] private Image progressSlider;

    // 当前列表项绑定的录像 Id。
    private string _replayId;
    // 当前录像展示名。
    private string _displayName;
    // 是否正处于下载中。
    private bool _isDownloading;

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
