using StellarNet.UI;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 回放控制面板。
/// </summary>
public class Panel_StellarNetReplay : UIPanelBase
{
    /// <summary>
    /// 回放进度文本。
    /// </summary>
    [SerializeField] private TMP_Text progressText;

    /// <summary>
    /// 回放进度滑条。
    /// </summary>
    [SerializeField] private Slider progressSlider;

    /// <summary>
    /// 播放或暂停按钮。
    /// </summary>
    [SerializeField] private Button playPauseBtn;

    /// <summary>
    /// 播放或暂停按钮文案。
    /// </summary>
    [SerializeField] private TMP_Text playPauseBtnText;

    /// <summary>
    /// 重播按钮。
    /// </summary>
    [SerializeField] private Button restartBtn;

    /// <summary>
    /// 退出回放按钮。
    /// </summary>
    [SerializeField] private Button exitBtn;

    /// <summary>
    /// 0.5 倍速按钮。
    /// </summary>
    [Header("预设倍速控制")] [SerializeField] private Button speed05Btn;

    /// <summary>
    /// 1 倍速按钮。
    /// </summary>
    [SerializeField] private Button speed10Btn;

    /// <summary>
    /// 2 倍速按钮。
    /// </summary>
    [SerializeField] private Button speed20Btn;

    /// <summary>
    /// 4 倍速按钮。
    /// </summary>
    [SerializeField] private Button speed40Btn;

    /// <summary>
    /// 当前倍速显示文本。
    /// </summary>
    [SerializeField] private TMP_Text currentSpeedText;

    /// <summary>
    /// 自定义倍速输入框。
    /// </summary>
    [Header("自定义倍速 (开发者/自由输入)")] [SerializeField]
    private TMP_InputField customSpeedIpt;

    /// <summary>
    /// 应用自定义倍速按钮。
    /// </summary>
    [SerializeField] private Button applyCustomSpeedBtn;

    /// <summary>
    /// 当前回放播放器实例。
    /// </summary>
    private ClientReplayPlayer _replayPlayer;

    /// <summary>
    /// 拖动滑条时的保护标记。
    /// 避免 Seek 与自动刷新互相覆盖。
    /// </summary>
    private bool _isDraggingSlider = false;

    /// <summary>
    /// 初始化回放控制按钮和滑条事件。
    /// </summary>
    public override void OnInit()
    {
        base.OnInit();
        playPauseBtn.onClick.AddListener(OnPlayPauseBtn);
        restartBtn.onClick.AddListener(OnRestartBtn);
        exitBtn.onClick.AddListener(OnExitBtn);

        speed05Btn.onClick.AddListener(() => SetSpeed(0.5f));
        speed10Btn.onClick.AddListener(() => SetSpeed(1.0f));
        speed20Btn.onClick.AddListener(() => SetSpeed(2.0f));
        speed40Btn.onClick.AddListener(() => SetSpeed(4.0f));

        if (applyCustomSpeedBtn != null && customSpeedIpt != null)
        {
            applyCustomSpeedBtn.onClick.AddListener(OnApplyCustomSpeedBtn);
        }

        progressSlider.onValueChanged.AddListener(OnSliderValueChanged);
    }

    /// <summary>
    /// 销毁时移除全部 UI 事件。
    /// </summary>
    private void OnDestroy()
    {
        playPauseBtn?.onClick.RemoveAllListeners();
        restartBtn?.onClick.RemoveAllListeners();
        exitBtn?.onClick.RemoveAllListeners();
        speed05Btn?.onClick.RemoveAllListeners();
        speed10Btn?.onClick.RemoveAllListeners();
        speed20Btn?.onClick.RemoveAllListeners();
        speed40Btn?.onClick.RemoveAllListeners();
        applyCustomSpeedBtn?.onClick.RemoveAllListeners();
        progressSlider?.onValueChanged.RemoveAllListeners();
    }

    /// <summary>
    /// 打开回放面板并尝试加载指定回放文件。
    /// </summary>
    public override void OnOpen(object uiData = null)
    {
        base.OnOpen(uiData);
        // 回放面板内部独立管理一个本地播放器。
        if (uiData is string filePath)
        {
            _replayPlayer = new ClientReplayPlayer(NetClient.App);

            // 初始化失败时直接回退大厅，避免停在空回放页。
            bool success = _replayPlayer.StartReplay(filePath);
            if (!success)
            {
                _replayPlayer = null;
                NetLogger.LogError("Panel_StellarNetReplay", "录像初始化失败，自动退出回放并清理损坏文件");
                GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = "录像文件已损坏，已自动清理，请重新下载" });

                return;
            }

            progressSlider.minValue = 0;
            progressSlider.maxValue = _replayPlayer.GetTotalTicks();
            SetSpeed(1.0f);
            UpdateUIState();
        }
        else
        {
            NetLogger.LogError("Panel_StellarNetReplay", "打开回放面板失败：未传入合法的录像文件路径");
            NetClient.App?.LeaveRoom();
        }
    }

    /// <summary>
    /// 将回放 tick 转成 mm:ss 文本。
    /// </summary>
    private string FormatTime(int ticks)
    {
        int tickRate = _replayPlayer != null ? _replayPlayer.GetRecordedTickRate() : 60;
        int totalSeconds = tickRate > 0 ? Mathf.FloorToInt(ticks / (float)tickRate) : ticks / 60;
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        return $"{minutes:D2}:{seconds:D2}";
    }

    /// <summary>
    /// 每帧推进回放播放器并刷新进度显示。
    /// </summary>
    private void Update()
    {
        // 面板每帧驱动播放器并刷新进度文案。
        if (_replayPlayer == null) return;

        _replayPlayer.Update(Time.deltaTime);

        if (!_isDraggingSlider)
        {
            progressSlider.value = _replayPlayer.CurrentTick;
        }

        int displayTick = Mathf.Min(_replayPlayer.CurrentTick, _replayPlayer.GetTotalTicks());
        progressText.text = $"进度: {FormatTime(displayTick)} / {FormatTime(_replayPlayer.GetTotalTicks())}";

        if (playPauseBtnText.text == "暂停" && _replayPlayer.IsPaused)
        {
            UpdateUIState();
        }
    }

    /// <summary>
    /// 切换播放和暂停状态。
    /// </summary>
    private void OnPlayPauseBtn()
    {
        if (_replayPlayer == null) return;
        _replayPlayer.IsPaused = !_replayPlayer.IsPaused;
        UpdateUIState();
    }

    /// <summary>
    /// 从头重新播放回放。
    /// </summary>
    private void OnRestartBtn()
    {
        if (_replayPlayer == null) return;
        _replayPlayer.Seek(0);
        _replayPlayer.IsPaused = false;
        UpdateUIState();
    }

    /// <summary>
    /// 设置回放播放倍速。
    /// </summary>
    public void SetSpeed(float speed)
    {
        if (_replayPlayer == null) return;
        // 倍速变化直接同步到播放器。
        _replayPlayer.PlaybackSpeed = speed;
        currentSpeedText.text = $"当前倍速: {speed}x";
    }

    /// <summary>
    /// 解析并应用自定义倍速输入。
    /// </summary>
    private void OnApplyCustomSpeedBtn()
    {
        if (customSpeedIpt == null) return;
        if (float.TryParse(customSpeedIpt.text, out float speed))
        {
            speed = Mathf.Clamp(speed, 0.1f, 100f);
            SetSpeed(speed);
            customSpeedIpt.text = speed.ToString("F1");
        }
        else
        {
            NetLogger.LogError("Panel_StellarNetReplay", "请输入合法的数字倍速");
        }
    }

    /// <summary>
    /// 拖动进度条时让播放器 Seek 到目标 tick。
    /// </summary>
    private void OnSliderValueChanged(float value)
    {
        if (_replayPlayer == null) return;
        // 用户拖动时间轴时执行 Seek。
        if (Mathf.Abs(value - _replayPlayer.CurrentTick) > 1f)
        {
            _isDraggingSlider = true;
            _replayPlayer.Seek((int)value);
            _isDraggingSlider = false;
        }
    }

    /// <summary>
    /// 退出当前回放房间。
    /// </summary>
    private void OnExitBtn()
    {
        // 退出回放时销毁本地回放房间。
        if (_replayPlayer != null)
        {
            _replayPlayer.StopReplay();
            _replayPlayer = null;
        }
        else
        {
            NetClient.App?.LeaveRoom();
        }
    }

    /// <summary>
    /// 刷新播放/暂停按钮文案。
    /// </summary>
    private void UpdateUIState()
    {
        if (_replayPlayer == null) return;
        playPauseBtnText.text = _replayPlayer.IsPaused ? "播放" : "暂停";
    }
}
