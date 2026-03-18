using Cysharp.Threading.Tasks;
using StellarFramework;
using StellarFramework.UI;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Panel_StellarNetReplay : UIPanelBase
{
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private Slider progressSlider;
    [SerializeField] private Button playPauseBtn;
    [SerializeField] private TMP_Text playPauseBtnText;
    [SerializeField] private Button restartBtn;
    [SerializeField] private Button exitBtn;

    [Header("预设倍速控制")] [SerializeField] private Button speed05Btn;
    [SerializeField] private Button speed10Btn;
    [SerializeField] private Button speed20Btn;
    [SerializeField] private Button speed40Btn;
    [SerializeField] private TMP_Text currentSpeedText;

    [Header("自定义倍速 (开发者/自由输入)")] [SerializeField]
    private TMP_InputField customSpeedIpt;

    [SerializeField] private Button applyCustomSpeedBtn;

    private ClientReplayPlayer _replayPlayer;
    private bool _isDraggingSlider = false;

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

    private void OnDestroy()
    {
        playPauseBtn.onClick.RemoveAllListeners();
        restartBtn.onClick.RemoveAllListeners();
        exitBtn.onClick.RemoveAllListeners();
        speed05Btn.onClick.RemoveAllListeners();
        speed10Btn.onClick.RemoveAllListeners();
        speed20Btn.onClick.RemoveAllListeners();
        speed40Btn.onClick.RemoveAllListeners();
        if (applyCustomSpeedBtn != null) applyCustomSpeedBtn.onClick.RemoveAllListeners();
        progressSlider.onValueChanged.RemoveAllListeners();
    }

    public override async UniTask OnOpen(object uiData = null)
    {
        await base.OnOpen(uiData);

        if (uiData is ReplayFile replayFile)
        {
            _replayPlayer = new ClientReplayPlayer(NetClient.App);
            _replayPlayer.StartReplay(replayFile);

            progressSlider.minValue = 0;
            progressSlider.maxValue = _replayPlayer.GetTotalTicks();
            SetSpeed(1.0f);
            UpdateUIState();
        }
        else
        {
            LogKit.LogError("Panel_StellarNetReplay", "打开回放面板失败：未传入合法的 ReplayFile 数据");
            CloseSelf();
        }
    }

    private void Update()
    {
        if (_replayPlayer == null) return;

        _replayPlayer.Update(Time.deltaTime);

        if (!_isDraggingSlider)
        {
            progressSlider.value = _replayPlayer.CurrentTick;
        }

        progressText.text = $"进度: {_replayPlayer.CurrentTick} / {_replayPlayer.GetTotalTicks()}";

        if (playPauseBtnText.text == "暂停" && _replayPlayer.IsPaused)
        {
            UpdateUIState();
        }
    }

    private void OnPlayPauseBtn()
    {
        if (_replayPlayer == null) return;
        _replayPlayer.IsPaused = !_replayPlayer.IsPaused;
        UpdateUIState();
    }

    private void OnRestartBtn()
    {
        if (_replayPlayer == null) return;
        _replayPlayer.Seek(0);
        _replayPlayer.IsPaused = false;
        UpdateUIState();
    }

    /// <summary>
    /// 核心接口：支持任意浮点数的倍速设置
    /// </summary>
    public void SetSpeed(float speed)
    {
        if (_replayPlayer == null) return;
        _replayPlayer.PlaybackSpeed = speed;
        currentSpeedText.text = $"当前倍速: {speed}x";
    }

    private void OnApplyCustomSpeedBtn()
    {
        if (customSpeedIpt == null) return;

        if (float.TryParse(customSpeedIpt.text, out float speed))
        {
            // 限制一下合理的范围，防止输入负数或过大(如 10000)导致单帧 while 循环卡死主线程
            speed = Mathf.Clamp(speed, 0.1f, 100f);
            SetSpeed(speed);
            customSpeedIpt.text = speed.ToString("F1");
        }
        else
        {
            LogKit.LogError("Panel_StellarNetReplay", "请输入合法的数字倍速");
        }
    }

    private void OnSliderValueChanged(float value)
    {
        if (_replayPlayer == null) return;
        if (Mathf.Abs(value - _replayPlayer.CurrentTick) > 1f)
        {
            _isDraggingSlider = true;
            _replayPlayer.Seek((int)value);
            _isDraggingSlider = false;
        }
    }

    private void OnExitBtn()
    {
        if (_replayPlayer != null)
        {
            _replayPlayer.StopReplay();
            _replayPlayer = null;
        }
        // 移除 CloseSelf()，将 UI 关闭的权力完全交给 ClientUIRouter 统一调度，防止冲突导致空场景
    }

    private void UpdateUIState()
    {
        if (_replayPlayer == null) return;
        playPauseBtnText.text = _replayPlayer.IsPaused ? "播放" : "暂停";
    }
}