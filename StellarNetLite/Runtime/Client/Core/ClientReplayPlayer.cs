using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

namespace StellarNet.Lite.Client.Core
{
    public sealed class ClientReplayPlayer : IDisposable
    {
        private readonly ClientApp _app;

        public int CurrentTick { get; private set; }
        public float PlaybackSpeed { get; set; } = 1f;
        public bool IsPaused { get; set; }

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
        private const int SparseIndexIntervalTicks = 300;

        private struct ReplayFrameData
        {
            public int Tick;
            public int MsgId;
            public byte[] Payload;
            public int PayloadLength;
        }

        private ReplayFrameData? _nextFrame;

        public ClientReplayPlayer(ClientApp app)
        {
            _app = app;
        }

        public void StartReplay(string filePath)
        {
            if (_app == null)
            {
                NetLogger.LogError("ClientReplayPlayer", $"启动回放失败: _app 为空, FilePath:{filePath}");
                return;
            }

            if (string.IsNullOrEmpty(filePath))
            {
                NetLogger.LogError("ClientReplayPlayer", "启动回放失败: filePath 为空");
                return;
            }

            if (!File.Exists(filePath))
            {
                NetLogger.LogError("ClientReplayPlayer", $"启动回放失败: 文件不存在, FilePath:{filePath}");
                return;
            }

            if (_app.State != ClientAppState.InLobby)
            {
                NetLogger.LogError("ClientReplayPlayer", $"启动回放失败: 当前状态非法, State:{_app.State}, FilePath:{filePath}");
                return;
            }

            _replayFilePath = filePath;
            _isPlaying = true;
            IsPaused = false;
            PlaybackSpeed = 1f;
            _tickAccumulator = 0f;
            _lastReportedTimeScale = -1f;

            bool initSuccess = InitStream();
            if (!initSuccess)
            {
                StopReplay();
                return;
            }

            RestartSandbox();
        }

        private bool InitStream()
        {
            CleanupStream();

            if (string.IsNullOrEmpty(_replayFilePath))
            {
                NetLogger.LogError("ClientReplayPlayer", "初始化录像流失败: _replayFilePath 为空");
                return false;
            }

            if (!File.Exists(_replayFilePath))
            {
                NetLogger.LogError("ClientReplayPlayer", $"初始化录像流失败: 文件不存在, FilePath:{_replayFilePath}");
                return false;
            }

            try
            {
                using (var fs = new FileStream(_replayFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] lengthBytes = new byte[4];
                    int headerLengthBytesRead = fs.Read(lengthBytes, 0, 4);
                    if (headerLengthBytesRead < 4)
                    {
                        NetLogger.LogError("ClientReplayPlayer", $"初始化录像流失败: Header 长度字段读取不足, FilePath:{_replayFilePath}, Read:{headerLengthBytesRead}");
                        return false;
                    }

                    int headerLength = BitConverter.ToInt32(lengthBytes, 0);
                    if (headerLength <= 0)
                    {
                        NetLogger.LogError("ClientReplayPlayer", $"初始化录像流失败: HeaderLength 非法, FilePath:{_replayFilePath}, HeaderLength:{headerLength}");
                        return false;
                    }

                    byte[] headerBytes = new byte[headerLength];
                    int actualHeaderRead = ReadFull(fs, headerBytes, 0, headerLength);
                    if (actualHeaderRead != headerLength)
                    {
                        NetLogger.LogError(
                            "ClientReplayPlayer",
                            $"初始化录像流失败: Header 读取不足, FilePath:{_replayFilePath}, Expected:{headerLength}, Actual:{actualHeaderRead}");
                        return false;
                    }

                    using (var ms = new MemoryStream(headerBytes))
                    using (var headerReader = new BinaryReader(ms))
                    {
                        uint magic = headerReader.ReadUInt32();
                        if (magic != 0x50455253)
                        {
                            NetLogger.LogError("ClientReplayPlayer", $"初始化录像流失败: 魔数错误, FilePath:{_replayFilePath}, Magic:{magic}");
                            return false;
                        }

                        byte version = headerReader.ReadByte();
                        string displayName = headerReader.ReadString();
                        _roomId = headerReader.ReadString();
                        int compCount = headerReader.ReadInt32();

                        if (compCount < 0)
                        {
                            NetLogger.LogError("ClientReplayPlayer", $"初始化录像流失败: compCount 非法, FilePath:{_replayFilePath}, CompCount:{compCount}");
                            return false;
                        }

                        _componentIds = new int[compCount];
                        for (int i = 0; i < compCount; i++)
                        {
                            _componentIds[i] = headerReader.ReadInt32();
                        }

                        _realTotalTicks = version >= 2 ? headerReader.ReadInt32() : 108000;
                        if (_realTotalTicks < 0)
                        {
                            NetLogger.LogWarning("ClientReplayPlayer", $"录像总 Tick 非法，已回退为 0, FilePath:{_replayFilePath}, TotalTicks:{_realTotalTicks}");
                            _realTotalTicks = 0;
                        }

                        if (string.IsNullOrEmpty(_roomId))
                        {
                            NetLogger.LogError(
                                "ClientReplayPlayer",
                                $"初始化录像流失败: RoomId 为空, FilePath:{_replayFilePath}, DisplayName:{displayName}, Version:{version}");
                            return false;
                        }
                    }

                    _rawFilePath = _replayFilePath + ".raw";
                    if (!File.Exists(_rawFilePath))
                    {
                        NetLogger.LogInfo("ClientReplayPlayer", $"首次播放录像，开始解压 Raw 缓存文件。Replay:{_replayFilePath}");

                        using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                        using (var rawFs = new FileStream(_rawFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            gz.CopyTo(rawFs);
                        }
                    }
                }

                if (!File.Exists(_rawFilePath))
                {
                    NetLogger.LogError("ClientReplayPlayer", $"初始化录像流失败: Raw 缓存文件不存在, RawPath:{_rawFilePath}");
                    return false;
                }

                _rawFs = new FileStream(_rawFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                _rawReader = new BinaryReader(_rawFs);
                BuildSparseIndex();
                ResetStreamToStart();
                return true;
            }
            catch (Exception ex)
            {
                NetLogger.LogError(
                    "ClientReplayPlayer",
                    $"初始化录像流失败: FilePath:{_replayFilePath}, Exception:{ex.GetType().Name}, Message:{ex.Message}");
                CleanupStream();
                return false;
            }
        }

        private void BuildSparseIndex()
        {
            if (_rawFs == null || _rawReader == null)
            {
                NetLogger.LogError("ClientReplayPlayer", "构建稀疏索引失败: 录像流为空");
                return;
            }

            _frameIndex.Clear();
            _rawFs.Position = 0;

            while (_rawFs.Position < _rawFs.Length)
            {
                long offset = _rawFs.Position;

                if (!TryReadFrameHeader(_rawReader, out int tick, out int msgId, out int len))
                {
                    NetLogger.LogWarning("ClientReplayPlayer", $"构建稀疏索引提前结束: 帧头读取失败, RawPath:{_rawFilePath}, Offset:{offset}");
                    break;
                }

                if (len < 0)
                {
                    NetLogger.LogError("ClientReplayPlayer", $"构建稀疏索引失败: PayloadLength 非法, Tick:{tick}, MsgId:{msgId}, Len:{len}, Offset:{offset}");
                    break;
                }

                if (tick % SparseIndexIntervalTicks == 0 && !_frameIndex.ContainsKey(tick))
                {
                    _frameIndex.Add(tick, offset);
                }

                long nextPosition = _rawFs.Position + len;
                if (nextPosition > _rawFs.Length)
                {
                    NetLogger.LogWarning(
                        "ClientReplayPlayer",
                        $"构建稀疏索引提前结束: 帧越界, Tick:{tick}, MsgId:{msgId}, Len:{len}, Position:{_rawFs.Position}, Length:{_rawFs.Length}");
                    break;
                }

                _rawFs.Position = nextPosition;
            }
        }

        private void CleanupStream()
        {
            _rawReader?.Dispose();
            _rawFs?.Dispose();
            _rawReader = null;
            _rawFs = null;

            if (_nextFrame.HasValue && _nextFrame.Value.Payload != null)
            {
                ArrayPool<byte>.Shared.Return(_nextFrame.Value.Payload);
            }

            _nextFrame = null;
        }

        public void StopReplay()
        {
            if (!_isPlaying)
            {
                return;
            }

            _isPlaying = false;
            CleanupStream();

            if (_app != null)
            {
                _app.LeaveRoom(false);
            }

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

            if (!_isPlaying || IsPaused)
            {
                return;
            }

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
            if (!_isPlaying)
            {
                NetLogger.LogWarning("ClientReplayPlayer", $"Seek 跳过: 当前未播放, TargetTick:{targetTick}");
                return;
            }

            if (_rawFs == null || _rawReader == null)
            {
                NetLogger.LogError("ClientReplayPlayer", $"Seek 失败: 录像流为空, TargetTick:{targetTick}, Replay:{_replayFilePath}");
                return;
            }

            targetTick = Mathf.Clamp(targetTick, 0, _realTotalTicks);
            if (targetTick == CurrentTick)
            {
                return;
            }

            if (targetTick < CurrentTick)
            {
                RestartSandbox();
            }

            int anchorTick = (targetTick / SparseIndexIntervalTicks) * SparseIndexIntervalTicks;
            if (anchorTick > CurrentTick && _frameIndex.TryGetValue(anchorTick, out long anchorOffset))
            {
                ResetRoomToIndexedPosition(anchorTick, anchorOffset);
            }

            int safeGuard = 0;
            while (CurrentTick < targetTick && _rawFs.Position < _rawFs.Length)
            {
                ProcessNextTick();
                safeGuard++;

                if (safeGuard > 10000000)
                {
                    NetLogger.LogError("ClientReplayPlayer", $"Seek 失败: 安全保护触发, TargetTick:{targetTick}, CurrentTick:{CurrentTick}");
                    break;
                }
            }
        }

        public int GetTotalTicks()
        {
            return _realTotalTicks;
        }

        private void RestartSandbox()
        {
            if (_app == null)
            {
                NetLogger.LogError("ClientReplayPlayer", "重启回放沙盒失败: _app 为空");
                return;
            }

            if (_rawFs == null || _rawReader == null)
            {
                NetLogger.LogError("ClientReplayPlayer", "重启回放沙盒失败: 录像流为空");
                return;
            }

            if (_app.State == ClientAppState.ReplayRoom)
            {
                _app.LeaveRoom(true);
            }

            _app.EnterReplayRoom(_roomId);
            if (_app.CurrentRoom == null)
            {
                NetLogger.LogError("ClientReplayPlayer", $"重启回放沙盒失败: CurrentRoom 创建失败, RoomId:{_roomId}");
                StopReplay();
                return;
            }

            bool buildSuccess = ClientRoomFactory.BuildComponents(_app.CurrentRoom, _componentIds);
            if (!buildSuccess)
            {
                NetLogger.LogError("ClientReplayPlayer", $"回放房间装配失败，强制终止。RoomId:{_roomId}");
                StopReplay();
                return;
            }

            GlobalTypeNetEvent.Broadcast(new Local_RoomEntered { Room = _app.CurrentRoom });
            CurrentTick = 0;
            _tickAccumulator = 0f;
            ResetStreamToStart();
        }

        private void ResetRoomToIndexedPosition(int tick, long offset)
        {
            RestartSandbox();

            if (_rawFs == null || _rawReader == null)
            {
                NetLogger.LogError("ClientReplayPlayer", $"索引跳转失败: 录像流为空, Tick:{tick}, Offset:{offset}");
                return;
            }

            if (offset < 0 || offset > _rawFs.Length)
            {
                NetLogger.LogError("ClientReplayPlayer", $"索引跳转失败: Offset 非法, Tick:{tick}, Offset:{offset}, Length:{_rawFs.Length}");
                return;
            }

            _rawFs.Position = offset;
            CurrentTick = tick;
            _tickAccumulator = 0f;

            if (_nextFrame.HasValue && _nextFrame.Value.Payload != null)
            {
                ArrayPool<byte>.Shared.Return(_nextFrame.Value.Payload);
            }

            _nextFrame = null;
        }

        private void ResetStreamToStart()
        {
            if (_rawFs == null)
            {
                NetLogger.LogError("ClientReplayPlayer", "重置录像流失败: _rawFs 为空");
                return;
            }

            _rawFs.Position = 0;

            if (_nextFrame.HasValue && _nextFrame.Value.Payload != null)
            {
                ArrayPool<byte>.Shared.Return(_nextFrame.Value.Payload);
            }

            _nextFrame = null;
        }

        private void ReadNextFrameFromFile()
        {
            if (_rawFs == null || _rawReader == null)
            {
                NetLogger.LogError("ClientReplayPlayer", "读取下一帧失败: 录像流为空");
                _nextFrame = null;
                return;
            }

            if (_rawFs.Position >= _rawFs.Length)
            {
                _nextFrame = null;
                return;
            }

            long offset = _rawFs.Position;

            if (!TryReadFrameHeader(_rawReader, out int tick, out int msgId, out int len))
            {
                NetLogger.LogWarning("ClientReplayPlayer", $"读取下一帧失败: 帧头读取失败, Offset:{offset}, RawPath:{_rawFilePath}");
                _nextFrame = null;
                return;
            }

            if (len < 0)
            {
                NetLogger.LogError("ClientReplayPlayer", $"读取下一帧失败: PayloadLength 非法, Tick:{tick}, MsgId:{msgId}, Len:{len}");
                _nextFrame = null;
                return;
            }

            byte[] payload = null;
            if (len > 0)
            {
                payload = ArrayPool<byte>.Shared.Rent(len);
                int actualBytesRead = ReadFull(_rawFs, payload, 0, len);
                if (actualBytesRead != len)
                {
                    NetLogger.LogWarning(
                        "ClientReplayPlayer",
                        $"读取下一帧失败: Payload 读取不足, Tick:{tick}, MsgId:{msgId}, Expected:{len}, Actual:{actualBytesRead}");

                    ArrayPool<byte>.Shared.Return(payload);
                    _nextFrame = null;
                    return;
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
            if (_app == null || _app.CurrentRoom == null)
            {
                NetLogger.LogWarning("ClientReplayPlayer", $"处理下一 Tick 跳过: CurrentRoom 为空, CurrentTick:{CurrentTick}");
                return;
            }

            while (true)
            {
                if (!_nextFrame.HasValue && _rawFs != null && _rawFs.Position < _rawFs.Length)
                {
                    ReadNextFrameFromFile();
                }

                if (!_nextFrame.HasValue)
                {
                    break;
                }

                if (_nextFrame.Value.Tick > CurrentTick)
                {
                    break;
                }

                ReplayFrameData frame = _nextFrame.Value;
                var packet = new Packet(0, frame.MsgId, NetScope.Room, _roomId, frame.Payload ?? Array.Empty<byte>(), 0, frame.PayloadLength);
                _app.CurrentRoom.Dispatcher.Dispatch(packet);

                if (frame.Payload != null)
                {
                    ArrayPool<byte>.Shared.Return(frame.Payload);
                }

                _nextFrame = null;
            }

            CurrentTick++;
        }

        private static bool TryReadFrameHeader(BinaryReader reader, out int tick, out int msgId, out int len)
        {
            tick = 0;
            msgId = 0;
            len = 0;

            if (reader == null)
            {
                return false;
            }

            Stream baseStream = reader.BaseStream;
            if (baseStream == null)
            {
                return false;
            }

            if (baseStream.Position + 12 > baseStream.Length)
            {
                return false;
            }

            tick = reader.ReadInt32();
            msgId = reader.ReadInt32();
            len = reader.ReadInt32();
            return true;
        }

        private static int ReadFull(Stream stream, byte[] buffer, int offset, int length)
        {
            if (stream == null || buffer == null || offset < 0 || length < 0 || offset + length > buffer.Length)
            {
                return 0;
            }

            int totalRead = 0;
            while (totalRead < length)
            {
                int read = stream.Read(buffer, offset + totalRead, length - totalRead);
                if (read <= 0)
                {
                    break;
                }

                totalRead += read;
            }

            return totalRead;
        }

        public void Dispose()
        {
            StopReplay();
        }
    }
}