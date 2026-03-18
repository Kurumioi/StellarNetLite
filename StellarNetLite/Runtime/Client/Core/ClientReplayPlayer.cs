using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Client.Core.Events;
using UnityEngine;

namespace StellarNet.Lite.Client.Core
{
    public sealed class ClientReplayPlayer
    {
        private readonly ClientApp _app;
        private ReplayFile _currentFile;

        public int CurrentTick { get; private set; }
        public float PlaybackSpeed { get; set; } = 1f;
        public bool IsPaused { get; set; } = false;

        private int _frameIndex;
        private bool _isPlaying;
        private float _tickAccumulator;
        private const float TickInterval = 1f / 60f;

        private float _lastReportedTimeScale = -1f;

        public ClientReplayPlayer(ClientApp app)
        {
            _app = app;
        }

        public void StartReplay(ReplayFile file)
        {
            if (file == null || file.Frames == null) return;
            if (_app.State != ClientAppState.InLobby) return;

            _currentFile = file;
            _isPlaying = true;
            IsPaused = false;
            PlaybackSpeed = 1f;
            _tickAccumulator = 0f;
            _lastReportedTimeScale = -1f;

            RestartSandbox();
        }

        public void StopReplay()
        {
            if (!_isPlaying) return;
            _isPlaying = false;
            _currentFile = null;
            // 正常退出回放，非静默，触发 Router 路由回大厅
            _app.LeaveRoom(false);
            GlobalTypeNetEvent.Broadcast(new Local_ReplayTimeScaleChanged { TimeScale = 1f });
        }

        public void Update(float deltaTime)
        {
            float currentTimeScale = IsPaused ? 0f : PlaybackSpeed;
            if (Mathf.Abs(_lastReportedTimeScale - currentTimeScale) > 0.001f)
            {
                _lastReportedTimeScale = currentTimeScale;
                GlobalTypeNetEvent.Broadcast(new Local_ReplayTimeScaleChanged { TimeScale = currentTimeScale });
            }

            if (!_isPlaying || IsPaused || _currentFile == null) return;

            _tickAccumulator += deltaTime * PlaybackSpeed;

            while (_tickAccumulator >= TickInterval)
            {
                _tickAccumulator -= TickInterval;
                ProcessNextTick();

                if (CurrentTick > GetTotalTicks())
                {
                    IsPaused = true;
                    break;
                }
            }
        }

        public void Seek(int targetTick)
        {
            if (!_isPlaying || _currentFile == null) return;

            targetTick = Mathf.Clamp(targetTick, 0, GetTotalTicks());

            if (targetTick < CurrentTick)
            {
                RestartSandbox();
            }

            while (CurrentTick < targetTick)
            {
                ProcessNextTick();
            }
        }

        public int GetTotalTicks()
        {
            if (_currentFile == null || _currentFile.Frames == null || _currentFile.Frames.Count == 0) return 0;
            return _currentFile.Frames[_currentFile.Frames.Count - 1].Tick;
        }

        private void RestartSandbox()
        {
            if (_app.State == ClientAppState.ReplayRoom)
            {
                // 核心修复：重播时销毁旧房间必须是静默的，绝不能触发 Router 切回大厅
                _app.LeaveRoom(true);
            }

            _app.EnterReplayRoom(_currentFile.RoomId);

            bool buildSuccess = ClientRoomFactory.BuildComponents(_app.CurrentRoom, _currentFile.ComponentIds);
            if (!buildSuccess)
            {
                NetLogger.LogError($"[ClientReplayPlayer]", $"回放房间 {_currentFile.RoomId} 本地装配失败，强制终止回放");
                StopReplay();
                return;
            }

            GlobalTypeNetEvent.Broadcast(new Local_RoomEntered { Room = _app.CurrentRoom });

            CurrentTick = 0;
            _frameIndex = 0;
            _tickAccumulator = 0f;
        }

        private void ProcessNextTick()
        {
            if (_currentFile == null || _app.CurrentRoom == null) return;

            while (_frameIndex < _currentFile.Frames.Count)
            {
                var frame = _currentFile.Frames[_frameIndex];
                if (frame.Tick > CurrentTick)
                {
                    break;
                }

                var packet = new Packet(0, frame.MsgId, NetScope.Room, frame.RoomId, frame.Payload);
                _app.CurrentRoom.Dispatcher.Dispatch(packet);
                _frameIndex++;
            }

            CurrentTick++;
        }
    }
}