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

        if (uiData is string filePath)
        {
            _replayPlayer = new ClientReplayPlayer(NetClient.App);
            _replayPlayer.StartReplay(filePath);

            progressSlider.minValue = 0;
            progressSlider.maxValue = _replayPlayer.GetTotalTicks();

            SetSpeed(1.0f);
            UpdateUIState();
        }
        else
        {
            LogKit.LogError("Panel_StellarNetReplay", "打开回放面板失败：未传入合法的录像文件路径");
            CloseSelf();
        }
    }

    private string FormatTime(int ticks)
    {
        int totalSeconds = ticks / 60;
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        return $"{minutes:D2}:{seconds:D2}";
    }

    private void Update()
    {
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

        // 修复：必须主动关闭自身面板，防止底层网络房间已销毁但 UI 残留拦截射线
        CloseSelf();
    }

    private void UpdateUIState()
    {
        if (_replayPlayer == null) return;
        playPauseBtnText.text = _replayPlayer.IsPaused ? "播放" : "暂停";
    }
}