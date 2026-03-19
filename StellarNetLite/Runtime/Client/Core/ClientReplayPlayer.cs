using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Client.Core.Events;
using UnityEngine;

namespace StellarNet.Lite.Client.Core
{
    public sealed class ClientReplayPlayer : IDisposable
    {
        private readonly ClientApp _app;

        public int CurrentTick { get; private set; }
        public float PlaybackSpeed { get; set; } = 1f;
        public bool IsPaused { get; set; } = false;

        private bool _isPlaying;
        private float _tickAccumulator;
        private const float TickInterval = 1f / 60f;
        private float _lastReportedTimeScale = -1f;

        private string _replayFilePath;
        private string _rawFilePath;
        private FileStream _rawFs;
        private BinaryReader _rawReader;

        private string _roomId;
        private int[] _componentIds;
        private int _realTotalTicks = 108000;

        private readonly Dictionary<int, long> _frameIndex = new Dictionary<int, long>();

        private struct ReplayFrameData
        {
            public int Tick;
            public int MsgId;
            public byte[] Payload;
            public int PayloadLength;
        }

        private ReplayFrameData? _nextFrame = null;

        public ClientReplayPlayer(ClientApp app)
        {
            _app = app;
        }

        public void StartReplay(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
            if (_app.State != ClientAppState.InLobby) return;

            _replayFilePath = filePath;
            _isPlaying = true;
            IsPaused = false;
            PlaybackSpeed = 1f;
            _tickAccumulator = 0f;
            _lastReportedTimeScale = -1f;

            if (!InitStream())
            {
                StopReplay();
                return;
            }

            RestartSandbox();
        }

        private bool InitStream()
        {
            try
            {
                CleanupStream();

                using (var fs = new FileStream(_replayFilePath, FileMode.Open, FileAccess.Read))
                {
                    byte[] lengthBytes = new byte[4];
                    int readCount = fs.Read(lengthBytes, 0, 4);
                    if (readCount < 4) return false;

                    int headerLength = BitConverter.ToInt32(lengthBytes, 0);
                    byte[] headerBytes = new byte[headerLength];
                    fs.Read(headerBytes, 0, headerLength);

                    using (var ms = new MemoryStream(headerBytes))
                    using (var headerReader = new BinaryReader(ms))
                    {
                        uint magic = headerReader.ReadUInt32();
                        if (magic != 0x50455253) // "SREP"
                        {
                            NetLogger.LogError("ClientReplayPlayer", "录像文件格式或魔数错误");
                            return false;
                        }

                        byte version = headerReader.ReadByte();
                        string displayName = headerReader.ReadString();
                        _roomId = headerReader.ReadString();

                        int compCount = headerReader.ReadInt32();
                        _componentIds = new int[compCount];
                        for (int i = 0; i < compCount; i++)
                        {
                            _componentIds[i] = headerReader.ReadInt32();
                        }

                        if (version >= 2)
                        {
                            _realTotalTicks = headerReader.ReadInt32();
                        }
                        else
                        {
                            _realTotalTicks = 108000;
                        }
                    }

                    _rawFilePath = _replayFilePath + ".raw";
                    if (!File.Exists(_rawFilePath))
                    {
                        NetLogger.LogInfo("ClientReplayPlayer", "首次播放，正在解压为 Raw 缓存文件以支持极速拖拽...");
                        using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                        using (var rawFs = new FileStream(_rawFilePath, FileMode.Create, FileAccess.Write))
                        {
                            gz.CopyTo(rawFs);
                        }
                    }
                }

                _rawFs = new FileStream(_rawFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                _rawReader = new BinaryReader(_rawFs);

                BuildSparseIndex();

                return true;
            }
            catch (Exception e)
            {
                NetLogger.LogError("ClientReplayPlayer", $"读取录像流失败: {e.Message}");
                return false;
            }
        }

        private void BuildSparseIndex()
        {
            if (_rawFs == null || _rawReader == null) return;

            _frameIndex.Clear();
            _rawFs.Position = 0;

            try
            {
                while (_rawFs.Position < _rawFs.Length)
                {
                    long offset = _rawFs.Position;
                    int tick = _rawReader.ReadInt32();
                    int msgId = _rawReader.ReadInt32();
                    int len = _rawReader.ReadInt32();

                    if (tick % 300 == 0 && !_frameIndex.ContainsKey(tick))
                    {
                        _frameIndex[tick] = offset;
                    }

                    _rawFs.Position += len;
                }
            }
            catch (EndOfStreamException)
            {
            }

            _rawFs.Position = 0;
        }

        private void CleanupStream()
        {
            _rawReader?.Dispose();
            _rawFs?.Dispose();
            _rawReader = null;
            _rawFs = null;

            if (_nextFrame.HasValue && _nextFrame.Value.Payload != null)
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(_nextFrame.Value.Payload);
                _nextFrame = null;
            }
        }

        public void StopReplay()
        {
            if (!_isPlaying) return;
            _isPlaying = false;
            CleanupStream();
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

            if (!_isPlaying || IsPaused) return;

            // 核心修复：到达录像末尾时自动暂停，防止 CurrentTick 无限增加
            if (CurrentTick >= _realTotalTicks)
            {
                IsPaused = true;
                CurrentTick = _realTotalTicks;
                return;
            }

            _tickAccumulator += deltaTime * PlaybackSpeed;
            while (_tickAccumulator >= TickInterval)
            {
                _tickAccumulator -= TickInterval;
                ProcessNextTick();

                // 核心修复：单帧内快进时也要做越界拦截
                if (CurrentTick >= _realTotalTicks)
                {
                    IsPaused = true;
                    CurrentTick = _realTotalTicks;
                    break;
                }
            }
        }

        public void Seek(int targetTick)
        {
            if (!_isPlaying) return;

            targetTick = Mathf.Clamp(targetTick, 0, _realTotalTicks);

            if (targetTick < CurrentTick)
            {
                RestartSandbox();
            }

            while (CurrentTick < targetTick && _rawFs.Position < _rawFs.Length)
            {
                ProcessNextTick();
            }
        }

        public int GetTotalTicks()
        {
            return _realTotalTicks;
        }

        private void RestartSandbox()
        {
            if (_app.State == ClientAppState.ReplayRoom)
            {
                _app.LeaveRoom(true);
            }

            _app.EnterReplayRoom(_roomId);
            bool buildSuccess = ClientRoomFactory.BuildComponents(_app.CurrentRoom, _componentIds);
            if (!buildSuccess)
            {
                NetLogger.LogError("ClientReplayPlayer", $"回放房间 {_roomId} 本地装配失败，强制终止回放");
                StopReplay();
                return;
            }

            GlobalTypeNetEvent.Broadcast(new Local_RoomEntered { Room = _app.CurrentRoom });

            CurrentTick = 0;
            _tickAccumulator = 0f;
            _rawFs.Position = 0;
            if (_nextFrame.HasValue && _nextFrame.Value.Payload != null)
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(_nextFrame.Value.Payload);
                _nextFrame = null;
            }
        }

        private void ReadNextFrameFromFile()
        {
            if (_rawFs.Position >= _rawFs.Length)
            {
                _nextFrame = null;
                return;
            }

            int tick = _rawReader.ReadInt32();
            int msgId = _rawReader.ReadInt32();
            int len = _rawReader.ReadInt32();

            byte[] payload = null;
            if (len > 0)
            {
                payload = System.Buffers.ArrayPool<byte>.Shared.Rent(len);
                int bytesRead = 0;
                while (bytesRead < len)
                {
                    int r = _rawReader.Read(payload, bytesRead, len - bytesRead);
                    if (r == 0) break;
                    bytesRead += r;
                }
            }

            _nextFrame = new ReplayFrameData
            {
                Tick = tick,
                MsgId = msgId,
                Payload = payload,
                PayloadLength = len
            };
        }

        private void ProcessNextTick()
        {
            if (_app.CurrentRoom == null) return;

            while (true)
            {
                if (!_nextFrame.HasValue && _rawFs.Position < _rawFs.Length)
                {
                    ReadNextFrameFromFile();
                }

                if (!_nextFrame.HasValue) break;

                if (_nextFrame.Value.Tick > CurrentTick)
                {
                    break;
                }

                var frame = _nextFrame.Value;
                var packet = new Packet(0, frame.MsgId, NetScope.Room, _roomId, frame.Payload, frame.PayloadLength);
                _app.CurrentRoom.Dispatcher.Dispatch(packet);

                if (frame.Payload != null)
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(frame.Payload);
                }

                _nextFrame = null;
            }

            CurrentTick++;
        }

        public void Dispose()
        {
            StopReplay();
        }
    }
}