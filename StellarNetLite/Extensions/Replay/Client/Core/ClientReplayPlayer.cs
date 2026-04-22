using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.ObjectSync;
using StellarNet.Lite.Shared.Replay;
using UnityEngine;

namespace StellarNet.Lite.Client.Core
{
    /// <summary>
    /// 客户端录像播放器。
    /// 负责加载录像、驱动回放 Tick、跳转和快照恢复。
    /// </summary>
    public sealed class ClientReplayPlayer : IDisposable
    {
        private readonly ClientApp _app;

        public int CurrentTick { get; private set; }
        public float PlaybackSpeed { get; set; } = 1f;
        public bool IsPaused { get; set; }

        private bool _isPlaying;
        private float _tickAccumulator;
        private float _tickInterval = 1f / ReplayFormatDefines.DefaultTickRateFallback;
        private float _lastReportedTimeScale = -1f;

        private string _replayFilePath;
        private string _rawFilePath;
        private FileStream _rawFs;
        private BinaryReader _rawReader;

        private string _roomId;
        private int[] _componentIds;
        private int _realTotalTicks = ReplayFormatDefines.DefaultTotalTicksFallback;
        private int _recordedTickRate = ReplayFormatDefines.DefaultTickRateFallback;
        private byte _replayVersion = ReplayFormatDefines.VersionLegacy;

        private readonly Dictionary<int, long> _messageFrameIndex = new Dictionary<int, long>();
        private readonly SortedDictionary<int, long> _snapshotFrameIndex = new SortedDictionary<int, long>();
        private const int SparseIndexIntervalTicks = 300;

        private struct ReplayFrameData
        {
            public ReplayFrameKind FrameKind;
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

        public bool StartReplay(string filePath)
        {
            if (_app == null)
            {
                NetLogger.LogError("ClientReplayPlayer", $"启动回放失败: _app 为空, FilePath:{filePath}");
                return false;
            }

            if (string.IsNullOrEmpty(filePath))
            {
                NetLogger.LogError("ClientReplayPlayer", "启动回放失败: filePath 为空");
                return false;
            }

            if (!File.Exists(filePath))
            {
                NetLogger.LogError("ClientReplayPlayer", $"启动回放失败: 文件不存在, FilePath:{filePath}");
                return false;
            }

            if (_app.State != ClientAppState.InLobby)
            {
                NetLogger.LogError("ClientReplayPlayer", $"启动回放失败: 当前状态非法, State:{_app.State}, FilePath:{filePath}");
                return false;
            }

            _replayFilePath = filePath;
            _isPlaying = true;
            IsPaused = false;
            PlaybackSpeed = 1f;
            _tickAccumulator = 0f;
            _lastReportedTimeScale = -1f;
            _tickInterval = 1f / ReplayFormatDefines.DefaultTickRateFallback;
            bool initSuccess = InitStream();
            if (!initSuccess)
            {
                StopReplay();
                try
                {
                    if (File.Exists(_replayFilePath))
                    {
                        File.Delete(_replayFilePath);
                    }

                    string rawPath = _replayFilePath + ".raw";
                    if (File.Exists(rawPath))
                    {
                        File.Delete(rawPath);
                    }
                }
                catch (Exception ex)
                {
                    NetLogger.LogError("ClientReplayPlayer", $"清理损坏录像文件失败: {ex.Message}");
                }

                return false;
            }

            RestartSandbox();
            return true;
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

            using (FileStream fs = new FileStream(_replayFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] lengthBytes = new byte[4];
                int headerLengthBytesRead = fs.Read(lengthBytes, 0, 4);
                if (headerLengthBytesRead < 4)
                {
                    NetLogger.LogError("ClientReplayPlayer",
                        $"初始化录像流失败: Header 长度字段读取不足, FilePath:{_replayFilePath}, Read:{headerLengthBytesRead}");
                    return false;
                }

                int headerLength = BitConverter.ToInt32(lengthBytes, 0);
                if (headerLength <= 0)
                {
                    NetLogger.LogError("ClientReplayPlayer",
                        $"初始化录像流失败: HeaderLength 非法, FilePath:{_replayFilePath}, HeaderLength:{headerLength}");
                    return false;
                }

                byte[] headerBytes = new byte[headerLength];
                int actualHeaderRead = ReadFull(fs, headerBytes, 0, headerLength);
                if (actualHeaderRead != headerLength)
                {
                    NetLogger.LogError("ClientReplayPlayer",
                        $"初始化录像流失败: Header 读取不足, FilePath:{_replayFilePath}, Expected:{headerLength}, Actual:{actualHeaderRead}");
                    return false;
                }

                using (MemoryStream ms = new MemoryStream(headerBytes))
                using (BinaryReader headerReader = new BinaryReader(ms))
                {
                    uint magic = headerReader.ReadUInt32();
                    if (magic != ReplayFormatDefines.MagicBytes)
                    {
                        NetLogger.LogError("ClientReplayPlayer",
                            $"初始化录像流失败: 魔数错误, FilePath:{_replayFilePath}, Magic:{magic}");
                        return false;
                    }

                    _replayVersion = headerReader.ReadByte();
                    string displayName = headerReader.ReadString();
                    _roomId = headerReader.ReadString();
                    int compCount = headerReader.ReadInt32();
                    if (compCount < 0)
                    {
                        NetLogger.LogError("ClientReplayPlayer",
                            $"初始化录像流失败: compCount 非法, FilePath:{_replayFilePath}, CompCount:{compCount}");
                        return false;
                    }

                    _componentIds = new int[compCount];
                    for (int i = 0; i < compCount; i++)
                    {
                        _componentIds[i] = headerReader.ReadInt32();
                    }

                    _realTotalTicks = _replayVersion >= ReplayFormatDefines.VersionLegacy
                        ? headerReader.ReadInt32()
                        : ReplayFormatDefines.DefaultTotalTicksFallback;
                    if (_realTotalTicks < 0)
                    {
                        NetLogger.LogWarning("ClientReplayPlayer",
                            $"录像总 Tick 非法，已回退为 0, FilePath:{_replayFilePath}, TotalTicks:{_realTotalTicks}");
                        _realTotalTicks = 0;
                    }

                    _recordedTickRate = _replayVersion >= ReplayFormatDefines.VersionWithTickRate
                        ? headerReader.ReadInt32()
                        : ReplayFormatDefines.DefaultTickRateFallback;
                    if (_recordedTickRate <= 0)
                    {
                        NetLogger.LogWarning("ClientReplayPlayer",
                            $"录像 TickRate 非法，已回退为 {ReplayFormatDefines.DefaultTickRateFallback}, FilePath:{_replayFilePath}, TickRate:{_recordedTickRate}");
                        _recordedTickRate = ReplayFormatDefines.DefaultTickRateFallback;
                    }

                    _tickInterval = 1f / _recordedTickRate;

                    if (string.IsNullOrEmpty(_roomId))
                    {
                        NetLogger.LogError("ClientReplayPlayer",
                            $"初始化录像流失败: RoomId 为空, FilePath:{_replayFilePath}, DisplayName:{displayName}, Version:{_replayVersion}");
                        return false;
                    }
                }

                _rawFilePath = _replayFilePath + ".raw";
                if (!File.Exists(_rawFilePath))
                {
                    NetLogger.LogInfo("ClientReplayPlayer", $"首次播放录像，开始解压 Raw 缓存文件。Replay:{_replayFilePath}");
                    using (GZipStream gz = new GZipStream(fs, CompressionMode.Decompress))
                    using (FileStream rawFs =
                           new FileStream(_rawFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
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
            BuildIndices();
            ResetStreamToStart();
            return true;
        }

        private void BuildIndices()
        {
            if (_rawFs == null || _rawReader == null)
            {
                NetLogger.LogError("ClientReplayPlayer", "构建索引失败: 录像流为空");
                return;
            }

            _messageFrameIndex.Clear();
            _snapshotFrameIndex.Clear();
            _rawFs.Position = 0;
            while (_rawFs.Position < _rawFs.Length)
            {
                long offset = _rawFs.Position;
                if (!TryReadFrameHeader(_rawReader, _replayVersion, out ReplayFrameKind frameKind, out int tick,
                        out int msgId, out int len))
                {
                    NetLogger.LogWarning("ClientReplayPlayer",
                        $"构建索引提前结束: 帧头读取失败, RawPath:{_rawFilePath}, Offset:{offset}");
                    break;
                }

                if (len < 0)
                {
                    NetLogger.LogError("ClientReplayPlayer",
                        $"构建索引失败: PayloadLength 非法, Tick:{tick}, MsgId:{msgId}, Len:{len}, Offset:{offset}, FrameKind:{frameKind}");
                    break;
                }

                if (frameKind == ReplayFrameKind.ObjectSnapshot)
                {
                    if (!_snapshotFrameIndex.ContainsKey(tick))
                    {
                        _snapshotFrameIndex.Add(tick, offset);
                    }
                }
                else if (frameKind == ReplayFrameKind.Message)
                {
                    if (tick % SparseIndexIntervalTicks == 0 && !_messageFrameIndex.ContainsKey(tick))
                    {
                        _messageFrameIndex.Add(tick, offset);
                    }
                }

                long nextPosition = _rawFs.Position + len;
                if (nextPosition > _rawFs.Length)
                {
                    NetLogger.LogWarning("ClientReplayPlayer",
                        $"构建索引提前结束: 帧越界, Tick:{tick}, MsgId:{msgId}, Len:{len}, Position:{_rawFs.Position}, Length:{_rawFs.Length}");
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
            while (_tickAccumulator >= _tickInterval)
            {
                _tickAccumulator -= _tickInterval;
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
                NetLogger.LogError("ClientReplayPlayer",
                    $"Seek 失败: 录像流为空, TargetTick:{targetTick}, Replay:{_replayFilePath}");
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

            int snapshotTick = FindNearestSnapshotTick(targetTick);
            if (snapshotTick >= 0 && _snapshotFrameIndex.TryGetValue(snapshotTick, out long snapshotOffset))
            {
                ResetRoomToSnapshotPosition(snapshotTick, snapshotOffset);
            }
            else
            {
                int anchorTick = (targetTick / SparseIndexIntervalTicks) * SparseIndexIntervalTicks;
                if (anchorTick > CurrentTick && _messageFrameIndex.TryGetValue(anchorTick, out long anchorOffset))
                {
                    ResetRoomToIndexedPosition(anchorTick, anchorOffset);
                }
            }

            int safeGuard = 0;
            while (CurrentTick < targetTick && _rawFs.Position < _rawFs.Length)
            {
                ProcessNextTick();
                safeGuard++;
                if (safeGuard > 10000000)
                {
                    NetLogger.LogError("ClientReplayPlayer",
                        $"Seek 失败: 安全保护触发, TargetTick:{targetTick}, CurrentTick:{CurrentTick}");
                    break;
                }
            }
        }

        public int GetTotalTicks()
        {
            return _realTotalTicks;
        }

        public int GetRecordedTickRate()
        {
            return _recordedTickRate > 0 ? _recordedTickRate : ReplayFormatDefines.DefaultTickRateFallback;
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

            if (_app.State == ClientAppState.SandboxRoom)
            {
                _app.LeaveRoom(true);
            }

            _app.EnterSandboxRoom(_roomId);
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
                NetLogger.LogError("ClientReplayPlayer",
                    $"索引跳转失败: Offset 非法, Tick:{tick}, Offset:{offset}, Length:{_rawFs.Length}");
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

        private void ResetRoomToSnapshotPosition(int tick, long offset)
        {
            RestartSandbox();
            if (_rawFs == null || _rawReader == null)
            {
                NetLogger.LogError("ClientReplayPlayer", $"关键帧跳转失败: 录像流为空, Tick:{tick}, Offset:{offset}");
                return;
            }

            if (_app == null || _app.CurrentRoom == null)
            {
                NetLogger.LogError("ClientReplayPlayer", $"关键帧跳转失败: CurrentRoom 为空, Tick:{tick}, Offset:{offset}");
                return;
            }

            if (offset < 0 || offset > _rawFs.Length)
            {
                NetLogger.LogError("ClientReplayPlayer",
                    $"关键帧跳转失败: Offset 非法, Tick:{tick}, Offset:{offset}, Length:{_rawFs.Length}");
                return;
            }

            _rawFs.Position = offset;
            if (!TryReadFrameHeader(_rawReader, _replayVersion, out ReplayFrameKind frameKind, out int frameTick,
                    out int msgId, out int payloadLength))
            {
                NetLogger.LogError("ClientReplayPlayer", $"关键帧跳转失败: 帧头读取失败, Tick:{tick}, Offset:{offset}");
                return;
            }

            if (frameKind != ReplayFrameKind.ObjectSnapshot)
            {
                NetLogger.LogError("ClientReplayPlayer",
                    $"关键帧跳转失败: 帧类型错误, Expected:{ReplayFrameKind.ObjectSnapshot}, Actual:{frameKind}, Tick:{frameTick}, Offset:{offset}");
                return;
            }

            if (payloadLength < 0)
            {
                NetLogger.LogError("ClientReplayPlayer",
                    $"关键帧跳转失败: payloadLength 非法, Tick:{frameTick}, PayloadLength:{payloadLength}");
                return;
            }

            byte[] payload = payloadLength > 0 ? ArrayPool<byte>.Shared.Rent(payloadLength) : Array.Empty<byte>();
            try
            {
                if (payloadLength > 0)
                {
                    int actualRead = ReadFull(_rawFs, payload, 0, payloadLength);
                    if (actualRead != payloadLength)
                    {
                        NetLogger.LogError("ClientReplayPlayer",
                            $"关键帧跳转失败: Payload 读取不足, Tick:{frameTick}, Expected:{payloadLength}, Actual:{actualRead}");
                        return;
                    }
                }

                ReplaySnapshotFrame snapshotFrame = DecodeSnapshotFrame(payload, payloadLength);
                if (snapshotFrame != null)
                {
                    ApplySnapshotToRoom(snapshotFrame);
                }

                CurrentTick = frameTick;
                _tickAccumulator = 0f;
                if (_nextFrame.HasValue && _nextFrame.Value.Payload != null)
                {
                    ArrayPool<byte>.Shared.Return(_nextFrame.Value.Payload);
                }

                _nextFrame = null;
            }
            finally
            {
                if (payloadLength > 0 && payload != null && payload.Length > 0)
                {
                    ArrayPool<byte>.Shared.Return(payload);
                }
            }
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
            if (!TryReadFrameHeader(_rawReader, _replayVersion, out ReplayFrameKind frameKind, out int tick,
                    out int msgId, out int len))
            {
                NetLogger.LogWarning("ClientReplayPlayer", $"读取下一帧失败: 帧头读取失败, Offset:{offset}, RawPath:{_rawFilePath}");
                _nextFrame = null;
                return;
            }

            if (len < 0)
            {
                NetLogger.LogError("ClientReplayPlayer",
                    $"读取下一帧失败: PayloadLength 非法, Tick:{tick}, MsgId:{msgId}, Len:{len}, FrameKind:{frameKind}");
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
                    NetLogger.LogWarning("ClientReplayPlayer",
                        $"读取下一帧失败: Payload 读取不足, Tick:{tick}, MsgId:{msgId}, Expected:{len}, Actual:{actualBytesRead}");
                    ArrayPool<byte>.Shared.Return(payload);
                    _nextFrame = null;
                    return;
                }
            }

            _nextFrame = new ReplayFrameData
            {
                FrameKind = frameKind,
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
                if (frame.FrameKind == ReplayFrameKind.Message)
                {
                    Packet packet = new Packet(0, frame.MsgId, NetScope.Room, _roomId,
                        frame.Payload ?? Array.Empty<byte>(), 0, frame.PayloadLength);
                    _app.CurrentRoom.Dispatcher.Dispatch(packet);
                }
                else if (frame.FrameKind == ReplayFrameKind.ObjectSnapshot)
                {
                    ReplaySnapshotFrame snapshotFrame = DecodeSnapshotFrame(frame.Payload, frame.PayloadLength);
                    if (snapshotFrame != null)
                    {
                        ApplySnapshotToRoom(snapshotFrame);
                    }
                }
                else
                {
                    NetLogger.LogWarning("ClientReplayPlayer",
                        $"未知录像帧类型，已忽略。FrameKind:{frame.FrameKind}, Tick:{frame.Tick}, MsgId:{frame.MsgId}");
                }

                if (frame.Payload != null)
                {
                    ArrayPool<byte>.Shared.Return(frame.Payload);
                }

                _nextFrame = null;
            }

            CurrentTick++;
        }

        // 核心解耦：将快照数据分发给实现了 IReplaySnapshotConsumer 的对应组件
        private void ApplySnapshotToRoom(ReplaySnapshotFrame snapshotFrame)
        {
            if (snapshotFrame == null || snapshotFrame.ComponentSnapshots == null || _app.CurrentRoom == null) return;

            var components = _app.CurrentRoom.Components;
            for (int i = 0; i < snapshotFrame.ComponentSnapshots.Length; i++)
            {
                var compData = snapshotFrame.ComponentSnapshots[i];
                IReplaySnapshotConsumer targetConsumer = null;

                for (int j = 0; j < components.Count; j++)
                {
                    if (components[j] is IReplaySnapshotConsumer consumer && consumer.SnapshotComponentId == compData.ComponentId)
                    {
                        targetConsumer = consumer;
                        break;
                    }
                }

                if (targetConsumer != null)
                {
                    targetConsumer.ApplySnapshot(compData.Payload);
                }
                else
                {
                    NetLogger.LogWarning("ClientReplayPlayer", $"快照应用跳过: 找不到对应的消费者, ComponentId:{compData.ComponentId}");
                }
            }
        }

        private static bool TryReadFrameHeader(BinaryReader reader, byte replayVersion, out ReplayFrameKind frameKind,
            out int tick, out int msgId, out int len)
        {
            frameKind = ReplayFrameKind.None;
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

            if (replayVersion >= ReplayFormatDefines.VersionWithObjectSnapshot)
            {
                if (baseStream.Position + 13 > baseStream.Length)
                {
                    return false;
                }

                frameKind = (ReplayFrameKind)reader.ReadByte();
                tick = reader.ReadInt32();
                msgId = reader.ReadInt32();
                len = reader.ReadInt32();
                return true;
            }

            if (baseStream.Position + 12 > baseStream.Length)
            {
                return false;
            }

            frameKind = ReplayFrameKind.Message;
            tick = reader.ReadInt32();
            msgId = reader.ReadInt32();
            len = reader.ReadInt32();
            return true;
        }

        private static ReplaySnapshotFrame DecodeSnapshotFrame(byte[] payload, int payloadLength)
        {
            if (payloadLength < 0)
            {
                NetLogger.LogError("ClientReplayPlayer", $"解码快照关键帧失败: payloadLength 非法, PayloadLength:{payloadLength}");
                return null;
            }

            if (payloadLength == 0)
            {
                return new ReplaySnapshotFrame
                {
                    Tick = 0,
                    ComponentSnapshots = Array.Empty<ComponentSnapshotData>()
                };
            }

            if (payload == null)
            {
                NetLogger.LogError("ClientReplayPlayer", $"解码快照关键帧失败: payload 为空, PayloadLength:{payloadLength}");
                return null;
            }

            using (MemoryStream ms = new MemoryStream(payload, 0, payloadLength, false))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                ReplaySnapshotFrame frame = new ReplaySnapshotFrame();
                frame.Deserialize(reader);
                return frame;
            }
        }

        private int FindNearestSnapshotTick(int targetTick)
        {
            int nearest = -1;
            foreach (KeyValuePair<int, long> kvp in _snapshotFrameIndex)
            {
                if (kvp.Key > targetTick)
                {
                    break;
                }

                nearest = kvp.Key;
            }

            return nearest;
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
