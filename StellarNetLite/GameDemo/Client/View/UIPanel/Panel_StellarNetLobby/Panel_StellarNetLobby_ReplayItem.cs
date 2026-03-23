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
    [SerializeField] private TMP_Text infoText;
    [SerializeField] private Button actionBtn;
    [SerializeField] private TMP_Text btnText;
    [SerializeField] private Image progressSlider;

    private string _replayId;
    private string _displayName;
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
            if (btnText != null) btnText.text = "播放";
        }
        else
        {
            if (btnText != null) btnText.text = "下载失败";
        }
    }
}