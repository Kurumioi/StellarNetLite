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

    [Header("倍速控制")] [SerializeField] private Button speed05Btn;
    [SerializeField] private Button speed10Btn;
    [SerializeField] private Button speed20Btn;
    [SerializeField] private Button speed40Btn;
    [SerializeField] private TMP_Text currentSpeedText;

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

        // 监听进度条拖拽，实现 Seek 功能
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
        progressSlider.onValueChanged.RemoveAllListeners();
    }

    public override async UniTask OnOpen(object uiData = null)
    {
        await base.OnOpen(uiData);

        if (uiData is ReplayFile replayFile)
        {
            // 核心修复：使用 NetClient.App 替代长链式调用
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

        // 驱动沙盒时间轴
        _replayPlayer.Update(Time.deltaTime);

        // 更新进度条表现 (如果玩家正在拖拽，则不覆盖进度条的值)
        if (!_isDraggingSlider)
        {
            progressSlider.value = _replayPlayer.CurrentTick;
        }

        progressText.text = $"进度: {_replayPlayer.CurrentTick} / {_replayPlayer.GetTotalTicks()}";

        // 如果播放完毕自动暂停，刷新按钮文本
        if (playPauseBtnText.text == "暂停" && _replayPlayer.IsPaused)
        {
            UpdateUIState();
        }
    }

    #region UI 交互逻辑

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

    private void SetSpeed(float speed)
    {
        if (_replayPlayer == null) return;
        _replayPlayer.PlaybackSpeed = speed;
        currentSpeedText.text = $"当前倍速: {speed}x";
    }

    private void OnSliderValueChanged(float value)
    {
        if (_replayPlayer == null) return;

        // 判断是否是玩家主动产生的较大跨度拖拽
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

        // 退出回放，回归大厅闭环
        UIKit.OpenPanel<Panel_StellarNetLobby>();
        CloseSelf();
    }

    private void UpdateUIState()
    {
        if (_replayPlayer == null) return;
        playPauseBtnText.text = _replayPlayer.IsPaused ? "播放" : "暂停";
    }

    #endregion
}