using System.Collections.Concurrent;
using System.Diagnostics;

namespace StellarNetLite.LoadTest;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        LoadTestOptions options;
        try
        {
            options = LoadTestOptions.Parse(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"参数错误: {ex.Message}");
            Console.WriteLine($"参数错误: {ex.Message}");
            LoadTestOptions.PrintUsage();
            return 1;
        }

        using CancellationTokenSource cts = options.DurationSeconds > 0
            ? new CancellationTokenSource(TimeSpan.FromSeconds(options.DurationSeconds))
            : new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        var runner = new LoadTestRunner(options);
        Task commandTask = Task.Run(() => ReadCommandsAsync(runner, cts.Token), cts.Token);
        try
        {
            await runner.RunAsync(cts.Token);
        }
        finally
        {
            cts.Cancel();
            try
            {
                await commandTask;
            }
            catch
            {
            }
        }

        return 0;
    }

    private static async Task ReadCommandsAsync(LoadTestRunner runner, CancellationToken token)
    {
        Console.WriteLine("鍛戒护: addroom [鏁伴噺] | removeroom [鏁伴噺] | endroom <鎴块棿搴忓彿> | status | help");
        while (!token.IsCancellationRequested)
        {
            string line;
            try
            {
                line = await Console.In.ReadLineAsync(token) ?? string.Empty;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                runner.EnqueueCommand(line.Trim());
            }
        }
    }
}

internal sealed class LoadTestOptions
{
    public string Transport { get; private set; } = "kcp";
    public string Host { get; private set; } = "127.0.0.1";
    public int Port { get; private set; } = 7777;
    public int RoomCount { get; private set; } = 1;
    public int ClientsPerRoom { get; private set; } = 50;
    public int RedundantClientsPerRoom { get; private set; } = 0;
    public int ConnectRate { get; private set; } = 10;
    public int DurationSeconds { get; private set; } = 0;
    public int MoveRate { get; private set; } = 8;
    public string RoomName { get; private set; } = "LoadTestRoom";
    public string AccountPrefix { get; private set; } = "bot";
    public string ClientVersion { get; private set; } = "0.0.1";
    public int LogIntervalSeconds { get; private set; } = 5;

    public int TotalClients => RoomCount * ClientsPerRoom;

    public static LoadTestOptions Parse(string[] args)
    {
        var options = new LoadTestOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string? value = i + 1 < args.Length ? args[i + 1] : null;
            switch (arg)
            {
                case "--transport":
                    options.Transport = RequireValue(arg, value);
                    i++;
                    break;
                case "--host":
                    options.Host = RequireValue(arg, value);
                    i++;
                    break;
                case "--port":
                    options.Port = int.Parse(RequireValue(arg, value));
                    i++;
                    break;
                case "--rooms":
                    options.RoomCount = int.Parse(RequireValue(arg, value));
                    i++;
                    break;
                case "--clients-per-room":
                    options.ClientsPerRoom = int.Parse(RequireValue(arg, value));
                    i++;
                    break;
                case "--redundant-clients-per-room":
                    options.RedundantClientsPerRoom = int.Parse(RequireValue(arg, value));
                    i++;
                    break;
                case "--clients":
                    // 兼容旧参数：等价于 1 个房间内 N 个客户端。
                    options.RoomCount = 1;
                    options.ClientsPerRoom = int.Parse(RequireValue(arg, value));
                    i++;
                    break;
                case "--connect-rate":
                    options.ConnectRate = int.Parse(RequireValue(arg, value));
                    i++;
                    break;
                case "--duration":
                    options.DurationSeconds = int.Parse(RequireValue(arg, value));
                    i++;
                    break;
                case "--move-rate":
                    options.MoveRate = int.Parse(RequireValue(arg, value));
                    i++;
                    break;
                case "--room-name":
                    options.RoomName = RequireValue(arg, value);
                    i++;
                    break;
                case "--account-prefix":
                    options.AccountPrefix = RequireValue(arg, value);
                    i++;
                    break;
                case "--client-version":
                    options.ClientVersion = RequireValue(arg, value);
                    i++;
                    break;
                case "--log-interval":
                    options.LogIntervalSeconds = int.Parse(RequireValue(arg, value));
                    i++;
                    break;
                case "--help":
                case "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(args), $"未知参数: {arg}");
            }
        }

        if (options.Transport != "kcp" && options.Transport != "tcp")
        {
            throw new InvalidOperationException("--transport 仅支持 kcp 或 tcp");
        }

        if (options.Port <= 0 ||
            options.RoomCount <= 0 ||
            options.ClientsPerRoom <= 0 ||
            options.RedundantClientsPerRoom < 0 ||
            options.ConnectRate <= 0 ||
            options.MoveRate <= 0 ||
            options.LogIntervalSeconds <= 0)
        {
            throw new InvalidOperationException("port / rooms / clients-per-room / connect-rate / move-rate / log-interval 必须都大于 0，冗余客户端数必须大于等于 0");
        }

        if (options.DurationSeconds < 0)
        {
            throw new InvalidOperationException("duration 必须大于等于 0，填 0 表示一直运行直到手动停止。");
        }

        return options;
    }

    public static void PrintUsage()
    {
        Console.WriteLine("用法:");
        Console.WriteLine("  dotnet run -c Release -- --transport kcp --host 127.0.0.1 --port 7777 --rooms 5 --clients-per-room 20 --redundant-clients-per-room 10 --duration 0 --move-rate 30");
    }

    private static string RequireValue(string arg, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException($"{arg} 缺少参数值");
        }

        return value;
    }
}

internal sealed class LoadTestRunner
{
    private const int StateLogSampleBotCount = 5;
    private readonly ConcurrentQueue<string> _pendingCommands = new();
    private readonly ConcurrentQueue<string> _runtimeCommands = new();
    private readonly LoadTestOptions _options;
    private readonly Stopwatch _stopwatch = new();
    private readonly LoadTestStats _stats = new();
    private readonly List<LoadTestBot> _bots = new();
    private string[] _roomIds;
    private bool[] _roomActive;
    private long _nextConnectAtMs;
    private long _lastLogAtMs;

    public LoadTestRunner(LoadTestOptions options)
    {
        _options = options;
        _roomIds = new string[_options.RoomCount];
        _roomActive = new bool[_options.RoomCount];
        for (int i = 0; i < _roomActive.Length; i++)
        {
            _roomActive[i] = true;
        }
    }

    private void AppendRoomBots(int startRoomIndex, int endRoomIndex)
    {
        int globalBotIndex = _bots.Count;
        for (int roomIndex = startRoomIndex; roomIndex < endRoomIndex; roomIndex++)
        {
            int roomSlot = roomIndex;
            for (int seatIndex = 0; seatIndex < _options.ClientsPerRoom; seatIndex++)
            {
                bool isOwner = seatIndex == 0;
                _bots.Add(new LoadTestBot(
                    globalBotIndex,
                    roomIndex,
                    seatIndex,
                    isOwner,
                    _options,
                    _stats,
                    () => _roomIds[roomSlot],
                    roomId => _roomIds[roomSlot] = roomId));
                globalBotIndex++;
            }
        }
    }

    public void EnqueueCommand(string commandLine)
    {
        if (!string.IsNullOrWhiteSpace(commandLine))
        {
            _runtimeCommands.Enqueue(commandLine);
        }
    }

    public async Task RunAsync(CancellationToken token)
    {
        Console.WriteLine(
            $"启动压测: 传输层={_options.Transport}, 地址={_options.Host}:{_options.Port}, 房间数={_options.RoomCount}, 每房机器人={_options.ClientsPerRoom}, 每房冗余={_options.RedundantClientsPerRoom}, 总机器人={_options.TotalClients}, 时长={FormatDuration(_options.DurationSeconds)}");
        _stopwatch.Start();
        AppendRoomBots(0, _options.RoomCount);

        try
        {
            while (!token.IsCancellationRequested)
            {
                long nowMs = _stopwatch.ElapsedMilliseconds;
                ProcessCommands();
                while (_pendingCommands.TryDequeue(out string? command))
                {
                    Console.WriteLine($"[命令] 当前版本暂未接入动态命令处理: {command}");
                }
                StartPendingBots(nowMs);

                for (int i = 0; i < _bots.Count; i++)
                {
                    _bots[i].Tick(nowMs);
                }

                if (nowMs - _lastLogAtMs >= _options.LogIntervalSeconds * 1000L)
                {
                    _lastLogAtMs = nowMs;
                    PrintStats(nowMs);
                }

                await Task.Delay(10, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            for (int i = 0; i < _bots.Count; i++)
            {
                _bots[i].Dispose();
            }

            PrintStats(_stopwatch.ElapsedMilliseconds);
            Console.WriteLine("压测已停止。");
        }
    }

    private void StartPendingBots(long nowMs)
    {
        if (nowMs < _nextConnectAtMs)
        {
            return;
        }

        long intervalMs = Math.Max(1, 1000 / _options.ConnectRate);
        for (int i = 0; i < _bots.Count; i++)
        {
            if (_bots[i].HasStarted || _bots[i].IsStopped || !IsRoomActive(_bots[i].RoomIndex))
            {
                continue;
            }

            _bots[i].Start();
            _nextConnectAtMs = nowMs + intervalMs;
            break;
        }
    }

    private void ProcessCommands()
    {
        while (_runtimeCommands.TryDequeue(out string? commandLine))
        {
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                continue;
            }

            string[] parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            string command = parts[0].ToLowerInvariant();
            switch (command)
            {
                case "addroom":
                    AddRooms(parts.Length > 1 && int.TryParse(parts[1], out int addCount) ? addCount : 1);
                    break;
                case "removeroom":
                    RemoveRooms(parts.Length > 1 && int.TryParse(parts[1], out int removeCount) ? removeCount : 1);
                    break;
                case "endroom":
                    if (parts.Length > 1 && int.TryParse(parts[1], out int roomIndex))
                    {
                        EndRoom(roomIndex);
                    }
                    else
                    {
                        Console.WriteLine("[命令] 用法: endroom <房间序号>");
                    }
                    break;
                case "status":
                    PrintStats(_stopwatch.ElapsedMilliseconds);
                    break;
                case "help":
                    Console.WriteLine("命令: addroom [数量] | removeroom [数量] | endroom <房间序号> | status | help");
                    break;
                default:
                    Console.WriteLine($"[命令] 未知命令: {commandLine}");
                    break;
            }
        }
    }

    private void AddRooms(int count)
    {
        if (count <= 0)
        {
            return;
        }

        int oldRoomCount = _roomIds.Length;
        Array.Resize(ref _roomIds, oldRoomCount + count);
        Array.Resize(ref _roomActive, oldRoomCount + count);
        int globalBotIndex = _bots.Count;

        for (int roomIndex = oldRoomCount; roomIndex < oldRoomCount + count; roomIndex++)
        {
            _roomActive[roomIndex] = true;
            int roomSlot = roomIndex;
            for (int seatIndex = 0; seatIndex < _options.ClientsPerRoom; seatIndex++)
            {
                bool isOwner = seatIndex == 0;
                _bots.Add(new LoadTestBot(
                    globalBotIndex,
                    roomIndex,
                    seatIndex,
                    isOwner,
                    _options,
                    _stats,
                    () => _roomIds[roomSlot],
                    roomId => _roomIds[roomSlot] = roomId));
                globalBotIndex++;
            }

            Console.WriteLine($"[命令] 已新增房间 {roomIndex + 1}，机器人={_options.ClientsPerRoom}，冗余={_options.RedundantClientsPerRoom}");
        }
    }

    private void RemoveRooms(int count)
    {
        if (count <= 0)
        {
            return;
        }

        for (int roomIndex = _roomIds.Length - 1; roomIndex >= 0 && count > 0; roomIndex--)
        {
            if (!IsRoomActive(roomIndex))
            {
                continue;
            }

            bool hasActiveBots = false;
            for (int i = 0; i < _bots.Count; i++)
            {
                if (_bots[i].RoomIndex == roomIndex && !_bots[i].IsStopped)
                {
                    hasActiveBots = true;
                    break;
                }
            }

            if (!hasActiveBots)
            {
                continue;
            }

            for (int i = 0; i < _bots.Count; i++)
            {
                if (_bots[i].RoomIndex == roomIndex && !_bots[i].IsStopped)
                {
                    _bots[i].Shutdown("按命令移除房间");
                }
            }

            _roomActive[roomIndex] = false;
            _roomIds[roomIndex] = string.Empty;
            Console.WriteLine($"[命令] 已移除房间 {roomIndex + 1}");
            count--;
        }
    }

    private void EndRoom(int roomNumber)
    {
        int roomIndex = roomNumber - 1;
        if (roomIndex < 0 || roomIndex >= _roomIds.Length)
        {
            Console.WriteLine($"[命令] 房间序号非法: {roomNumber}");
            return;
        }

        if (!IsRoomActive(roomIndex))
        {
            Console.WriteLine($"[cmd] room {roomNumber} is not active");
            return;
        }

        string roomId = _roomIds[roomIndex];
        _roomActive[roomIndex] = false;
        _roomIds[roomIndex] = string.Empty;
        if (string.IsNullOrEmpty(roomId))
        {
            Console.WriteLine($"[命令] 房间 {roomNumber} 尚未创建成功或已被移除。");
            return;
        }

        for (int i = 0; i < _bots.Count; i++)
        {
            if (_bots[i].RoomIndex == roomIndex && _bots[i].IsOwner && !_bots[i].IsStopped)
            {
                _bots[i].RequestForceEnd();
                Console.WriteLine($"[命令] 已请求房间 {roomNumber} 强制结束当前对局。");
                return;
            }
        }

        Console.WriteLine($"[命令] 房间 {roomNumber} 未找到可用房主机器人。");
    }

    private void PrintStats(long nowMs)
    {
        int createdRooms = 0;
        for (int i = 0; i < _roomIds.Length; i++)
        {
            if (!string.IsNullOrEmpty(_roomIds[i]))
            {
                createdRooms++;
            }
        }

        Dictionary<BotState, int> stateCounts = BuildStateCounts();
        LoadTestStats.Snapshot snapshot = _stats.Capture(nowMs / 1000d);
        Console.WriteLine(
            $"[统计] t={snapshot.ElapsedSeconds:F1}s 已启动={snapshot.StartedClients}/{_options.TotalClients} 已连接={snapshot.ConnectedClients} " +
            $"登录成功={snapshot.LoginSuccess} 进房完成={snapshot.RoomSetupSuccess} 已开局={snapshot.GameStartedClients} 房间={createdRooms}/{_options.RoomCount} " +
            $"发包={snapshot.SentPackets} 收包={snapshot.ReceivedPackets} 发流量KB={snapshot.SentBytes / 1024d:F1} 收流量KB={snapshot.ReceivedBytes / 1024d:F1} 错误={snapshot.Errors}");
        Console.WriteLine(
            $"[状态] 连接中={GetStateCount(stateCounts, BotState.Connecting)} 等登录={GetStateCount(stateCounts, BotState.AwaitLogin)} " +
            $"等建房={GetStateCount(stateCounts, BotState.AwaitCreateRoom)} 等加房={GetStateCount(stateCounts, BotState.AwaitJoinRoom)} " +
            $"等确认={GetStateCount(stateCounts, BotState.AwaitRoomSetup)} 等开局={GetStateCount(stateCounts, BotState.RoomWaiting)} " +
            $"已Ready={GetStateCount(stateCounts, BotState.ReadySent)} 游戏中={GetStateCount(stateCounts, BotState.InGame)} " +
            $"断开={GetStateCount(stateCounts, BotState.Disconnected)} 失败={GetStateCount(stateCounts, BotState.Failed)}");
        PrintSampleBots();
    }

    private static string FormatDuration(int seconds)
    {
        return seconds > 0 ? $"{seconds}s" : "直到手动停止";
    }

    private Dictionary<BotState, int> BuildStateCounts()
    {
        var counts = new Dictionary<BotState, int>();
        for (int i = 0; i < _bots.Count; i++)
        {
            BotState state = _bots[i].CurrentState;
            counts[state] = counts.TryGetValue(state, out int oldValue) ? oldValue + 1 : 1;
        }

        return counts;
    }

    private static int GetStateCount(Dictionary<BotState, int> counts, BotState state)
    {
        return counts.TryGetValue(state, out int value) ? value : 0;
    }

    private void PrintSampleBots()
    {
        int sampleCount = Math.Min(StateLogSampleBotCount, _bots.Count);
        for (int i = 0; i < sampleCount; i++)
        {
            Console.WriteLine($"[样本] {_bots[i].Tag} 当前状态={_bots[i].CurrentStateText}");
        }
    }

    private bool IsRoomActive(int roomIndex)
    {
        return roomIndex >= 0 && roomIndex < _roomActive.Length && _roomActive[roomIndex];
    }
}

internal enum BotState
{
    Idle,
    Connecting,
    AwaitLogin,
    AwaitCreateRoom,
    AwaitJoinRoom,
    AwaitRoomSetup,
    AwaitReady,
    RoomWaiting,
    ReadySent,
    InGame,
    LeavingRoom,
    RoomEnded,
    Disconnected,
    Failed,
    Stopped
}

internal sealed class LoadTestBot : IDisposable
{
    private const long StartGameRetryIntervalMs = 2500;
    private const long ConnectTimeoutMs = 5000;
    private const long LoginTimeoutMs = 5000;
    private const long CreateRoomTimeoutMs = 5000;
    private const long JoinRoomTimeoutMs = 5000;
    private const long RoomSetupTimeoutMs = 5000;
    private const long LeaveRoomTimeoutMs = 4000;

    private readonly int _globalBotIndex;
    private readonly int _roomIndex;
    private readonly int _seatIndex;
    private readonly bool _isOwner;
    private readonly LoadTestOptions _options;
    private readonly LoadTestStats _stats;
    private readonly Func<string> _getAssignedRoomId;
    private readonly Action<string> _setAssignedRoomId;
    private readonly ILoadTestTransport _transport;
    private readonly byte[] _sendBuffer = new byte[131072];
    private readonly Random _random;
    private readonly double _moveIntervalMs;
    private readonly string _tag;
    private uint _seq;
    private BotState _state;
    private long _stateEnterAtMs;
    private string _roomId = string.Empty;
    private bool _createRoomSent;
    private bool _joinRoomSent;
    private bool _roomSetupSent;
    private bool _readySent;
    private bool _leaveRoomSent;
    private long _leaveRoomSentAtMs;
    private bool _shutdownRequested;
    private string _shutdownReason = string.Empty;
    private bool _forceEndRequested;
    private long _nextReadyAtMs;
    private long _lastMoveAtMs;
    private long _lastStartGameAttemptAtMs;
    private bool _isWalking;
    private bool _sentStopPacket;
    private long _nextBehaviorAtMs;
    private long _nextActionAtMs;
    private long _nextBubbleAtMs;
    private float _currentPosX;
    private float _currentPosZ;
    private float _targetPosX;
    private float _targetPosZ;
    private bool _hasLoggedStateTimeout;
    private bool _wasTransportConnected;
    private bool _isStopped;

    public bool HasStarted { get; private set; }
    public bool IsStopped => _isStopped;
    public int RoomIndex => _roomIndex;
    public bool IsOwner => _isOwner;
    public BotState CurrentState => _state;
    public string Tag => _tag;
    public string CurrentStateText => BuildStateText();

    public LoadTestBot(
        int globalBotIndex,
        int roomIndex,
        int seatIndex,
        bool isOwner,
        LoadTestOptions options,
        LoadTestStats stats,
        Func<string> getAssignedRoomId,
        Action<string> setAssignedRoomId)
    {
        _globalBotIndex = globalBotIndex;
        _roomIndex = roomIndex;
        _seatIndex = seatIndex;
        _isOwner = isOwner;
        _options = options;
        _stats = stats;
        _getAssignedRoomId = getAssignedRoomId;
        _setAssignedRoomId = setAssignedRoomId;
        _transport = options.Transport == "kcp" ? new KcpLoadTestTransport() : new TcpLoadTestTransport();
        _random = new Random(globalBotIndex * 7919 + 17);
        _moveIntervalMs = 1000d / options.MoveRate;
        _tag = $"房间{_roomIndex + 1}-席位{_seatIndex + 1}-客户端{_globalBotIndex + 1}";
    }

    public void Start()
    {
        if (HasStarted || _isStopped)
        {
            return;
        }

        HasStarted = true;
        SetState(BotState.Connecting, 0);
        _wasTransportConnected = false;
        try
        {
            _transport.Connect(_options.Host, _options.Port);
            _stats.MarkStarted();
        }
        catch (Exception ex)
        {
            _state = BotState.Failed;
            _stats.MarkError();
            Console.WriteLine($"[错误][{_tag}] 连接服务端失败: {ex.GetType().Name} - {ex.Message}");
        }
    }

    public void Tick(long nowMs)
    {
        if (!HasStarted || _isStopped)
        {
            return;
        }

        _transport.Pump();
        bool isConnectedNow = _transport.IsConnected;

        if (isConnectedNow)
        {
            _wasTransportConnected = true;
        }

        if (isConnectedNow && _state == BotState.Connecting)
        {
            SetState(BotState.AwaitLogin, nowMs);
            _stats.MarkConnected();
            Console.WriteLine($"[信息][{_tag}] 已建立物理连接，开始发送登录请求。");
            SendLogin();
        }

        while (_transport.TryDequeue(out PacketData packet))
        {
            _stats.MarkReceived(packet.PayloadLength);
            HandlePacket(packet);
        }

        if (!_isOwner &&
            !_shutdownRequested &&
            !string.IsNullOrEmpty(_roomId) &&
            string.IsNullOrEmpty(_getAssignedRoomId()))
        {
            Shutdown("room retired");
        }

        if (_shutdownRequested)
        {
            ProcessShutdown(nowMs);
        }

        if (!_shutdownRequested && _state == BotState.AwaitJoinRoom && !_joinRoomSent)
        {
            string assignedRoomId = _getAssignedRoomId();
            if (!string.IsNullOrEmpty(assignedRoomId))
            {
                _roomId = assignedRoomId;
                Console.WriteLine($"[信息][{_tag}] 已拿到目标房间号，开始发送加房请求。RoomId={assignedRoomId}");
                SendJoinRoom(assignedRoomId);
            }
        }

        if (!_shutdownRequested && !_isOwner && _state == BotState.AwaitReady && nowMs >= _nextReadyAtMs)
        {
            SendReady();
            SetState(BotState.ReadySent, nowMs);
        }

        if (!_shutdownRequested && _state == BotState.RoomWaiting && _isOwner && nowMs - _lastStartGameAttemptAtMs >= StartGameRetryIntervalMs)
        {
            _lastStartGameAttemptAtMs = nowMs;
            SendStartGame();
        }

        if (!_shutdownRequested && _state == BotState.InGame)
        {
            UpdateNormalPlayerBehavior(nowMs);
        }

        // 只在“曾经连上过、现在又掉了”的情况下记为断开。
        // 避免 KCP 握手尚未完成时，就被错误地标记成已断开，导致后续永远不再发登录。
        if (_wasTransportConnected &&
            !isConnectedNow &&
            _state != BotState.Idle &&
            _state != BotState.Disconnected &&
            _state != BotState.Failed)
        {
            Console.WriteLine($"[错误][{_tag}] 连接已断开，当前状态={BuildStateText()}");
            if (_shutdownRequested)
            {
                StopImmediately(string.IsNullOrWhiteSpace(_shutdownReason) ? "disconnect during shutdown" : _shutdownReason);
                return;
            }

            SetState(BotState.Disconnected, nowMs);
        }

        CheckStateTimeout(nowMs);
    }

    public void Dispose()
    {
        _transport.Dispose();
    }

    public void CancelBeforeStart(string reason)
    {
        if (HasStarted)
        {
            Shutdown(reason);
            return;
        }

        _isStopped = true;
        SetState(BotState.Stopped, 0);
    }

    public void RequestForceEndAndShutdown(string reason)
    {
        _forceEndRequested = true;
        RequestShutdown(reason);
    }

    public void RequestForceEnd()
    {
        _forceEndRequested = true;
        RequestShutdown("force end room");
    }

    public void RequestShutdown(string reason)
    {
        if (_isStopped)
        {
            return;
        }

        _shutdownRequested = true;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            _shutdownReason = reason;
        }
    }

    public void Shutdown(string reason)
    {
        if (_isStopped)
        {
            return;
        }

        if (HasStarted)
        {
            _shutdownRequested = true;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                _shutdownReason = reason;
            }

            return;
        }

        _isStopped = true;
        SetState(BotState.Stopped, Environment.TickCount64);
        _transport.Dispose();
        Console.WriteLine($"[信息][{_tag}] 已停止: {reason}");
    }

    private void StopImmediately(string reason)
    {
        if (_isStopped)
        {
            return;
        }

        _isStopped = true;
        SetState(BotState.Stopped, Environment.TickCount64);
        _transport.Dispose();
        if (_isOwner && string.Equals(_getAssignedRoomId(), _roomId, StringComparison.Ordinal))
        {
            _setAssignedRoomId(string.Empty);
        }

        _roomId = string.Empty;
        Console.WriteLine($"[info][{_tag}] stopped: {reason}");
    }

    private void ProcessShutdown(long nowMs)
    {
        if (_isStopped)
        {
            return;
        }

        if (!_transport.IsConnected)
        {
            StopImmediately(string.IsNullOrWhiteSpace(_shutdownReason) ? "disconnected" : _shutdownReason);
            return;
        }

        if (_forceEndRequested && _isOwner && _state == BotState.InGame)
        {
            _forceEndRequested = false;
            byte[] endGamePayload = LiteJson.Serialize(new C2S_EndGame());
            SendPacket(MsgIds.C2S_EndGame, NetScope.Room, _roomId, endGamePayload);
        }

        if (!_leaveRoomSent &&
            (_state == BotState.RoomWaiting ||
             _state == BotState.ReadySent ||
             _state == BotState.InGame ||
             _state == BotState.RoomEnded ||
             _state == BotState.AwaitReady))
        {
            _leaveRoomSent = true;
            _leaveRoomSentAtMs = nowMs;
            SetState(BotState.LeavingRoom, nowMs);
            byte[] leavePayload = LiteJson.Serialize(new C2S_LeaveRoom());
            SendPacket(MsgIds.C2S_LeaveRoom, NetScope.Global, string.Empty, leavePayload);
            return;
        }

        if (_leaveRoomSent && nowMs - _leaveRoomSentAtMs >= LeaveRoomTimeoutMs)
        {
            StopImmediately(string.IsNullOrWhiteSpace(_shutdownReason) ? "leave timeout" : _shutdownReason);
            return;
        }

        if (!_leaveRoomSent &&
            (_state == BotState.Connecting ||
             _state == BotState.AwaitLogin ||
             _state == BotState.AwaitCreateRoom ||
             _state == BotState.AwaitJoinRoom ||
             _state == BotState.AwaitRoomSetup))
        {
            StopImmediately(string.IsNullOrWhiteSpace(_shutdownReason) ? "shutdown before room ready" : _shutdownReason);
        }
    }

    private void HandlePacket(in PacketData packet)
    {
        switch (packet.MsgId)
        {
            case MsgIds.S2C_LoginResult:
                HandleLoginResult(packet);
                break;
            case MsgIds.S2C_CreateRoomResult:
                HandleCreateRoomResult(packet);
                break;
            case MsgIds.S2C_JoinRoomResult:
                HandleJoinRoomResult(packet);
                break;
            case MsgIds.S2C_RoomSetupResult:
                HandleRoomSetupResult(packet);
                break;
            case MsgIds.S2C_GameStarted:
                HandleGameStarted(Environment.TickCount64);
                break;
            case MsgIds.S2C_GameEnded:
                SetState(BotState.RoomEnded, Environment.TickCount64);
                break;
            case MsgIds.S2C_LeaveRoomResult:
                StopImmediately(string.IsNullOrWhiteSpace(_shutdownReason) ? "leave room success" : _shutdownReason);
                break;
        }
    }

    private void HandleGameStarted(long nowMs)
    {
        SetState(BotState.InGame, nowMs);
        _stats.MarkGameStarted();
        _currentPosX = (float)(_random.NextDouble() * 1.2 - 0.6);
        _currentPosZ = (float)(_random.NextDouble() * 1.2 - 0.6);
        _targetPosX = _currentPosX;
        _targetPosZ = _currentPosZ;
        _isWalking = false;
        _sentStopPacket = false;
        _nextBehaviorAtMs = nowMs + NextRange(250, 900);
        _nextActionAtMs = nowMs + NextRange(6000, 12000);
        _nextBubbleAtMs = nowMs + NextRange(8000, 16000);
        _lastMoveAtMs = nowMs;
    }

    private void HandleLoginResult(in PacketData packet)
    {
        S2C_LoginResult? msg = LiteJson.Deserialize<S2C_LoginResult>(packet.Payload, packet.PayloadOffset, packet.PayloadLength);
        if (msg == null || !msg.Success)
        {
            SetState(BotState.Failed, Environment.TickCount64);
            _stats.MarkError();
            string reason = msg != null ? msg.Reason : "登录结果反序列化失败";
            Console.WriteLine($"[错误][{_tag}] 登录失败: {reason}");
            return;
        }

        _stats.MarkLoginSuccess();
        if (_isOwner)
        {
            SetState(BotState.AwaitCreateRoom, Environment.TickCount64);
            Console.WriteLine($"[信息][{_tag}] 登录成功，房主准备建房。");
            SendCreateRoom();
        }
        else
        {
            SetState(BotState.AwaitJoinRoom, Environment.TickCount64);
            Console.WriteLine($"[信息][{_tag}] 登录成功，等待目标房间创建完成。");
        }
    }

    private void HandleCreateRoomResult(in PacketData packet)
    {
        S2C_CreateRoomResult? msg = LiteJson.Deserialize<S2C_CreateRoomResult>(packet.Payload, packet.PayloadOffset, packet.PayloadLength);
        if (msg == null || !msg.Success || string.IsNullOrEmpty(msg.RoomId))
        {
            SetState(BotState.Failed, Environment.TickCount64);
            _stats.MarkError();
            string reason = msg != null ? msg.Reason : "建房结果反序列化失败";
            Console.WriteLine($"[错误][{_tag}] 建房失败: {reason}");
            return;
        }

        _roomId = msg.RoomId;
        _setAssignedRoomId(msg.RoomId);
        Console.WriteLine($"[信息][{_tag}] 建房成功: RoomId={msg.RoomId}");
        SendRoomSetupReady();
    }

    private void HandleJoinRoomResult(in PacketData packet)
    {
        S2C_JoinRoomResult? msg = LiteJson.Deserialize<S2C_JoinRoomResult>(packet.Payload, packet.PayloadOffset, packet.PayloadLength);
        if (msg == null || !msg.Success || string.IsNullOrEmpty(msg.RoomId))
        {
            SetState(BotState.Failed, Environment.TickCount64);
            _stats.MarkError();
            string reason = msg != null ? msg.Reason : "加房结果反序列化失败";
            Console.WriteLine($"[错误][{_tag}] 加入房间失败: {reason}");
            return;
        }

        _roomId = msg.RoomId;
        Console.WriteLine($"[信息][{_tag}] 加入房间成功: RoomId={msg.RoomId}");
        SendRoomSetupReady();
    }

    private void HandleRoomSetupResult(in PacketData packet)
    {
        S2C_RoomSetupResult? msg = LiteJson.Deserialize<S2C_RoomSetupResult>(packet.Payload, packet.PayloadOffset, packet.PayloadLength);
        if (msg == null || !msg.Success)
        {
            SetState(BotState.Failed, Environment.TickCount64);
            _stats.MarkError();
            string reason = msg != null ? msg.Reason : "房间确认结果反序列化失败";
            Console.WriteLine($"[错误][{_tag}] 进房确认失败: {reason}");
            return;
        }

        _stats.MarkRoomSetupSuccess();
        Console.WriteLine($"[信息][{_tag}] 进房确认成功: RoomId={msg.RoomId}");
        if (_isOwner)
        {
            SetState(BotState.RoomWaiting, Environment.TickCount64);
            _lastStartGameAttemptAtMs = 0;
        }
        else
        {
            _nextReadyAtMs = Environment.TickCount64 + NextRange(300, 1500);
            SetState(BotState.AwaitReady, Environment.TickCount64);
        }
    }

    private void SendLogin()
    {
        byte[] payload = LiteJson.Serialize(new C2S_Login
        {
            AccountId = $"{_options.AccountPrefix}_{_roomIndex:D3}_{_seatIndex:D3}_{_globalBotIndex:D5}",
            ClientVersion = _options.ClientVersion
        });
        SendPacket(MsgIds.C2S_Login, NetScope.Global, string.Empty, payload);
    }

    private void SendCreateRoom()
    {
        if (_createRoomSent)
        {
            return;
        }

        _createRoomSent = true;
        string roomName = _options.RoomCount > 1 || _roomIndex > 0 ? $"{_options.RoomName}_{_roomIndex + 1:D3}" : _options.RoomName;

        byte[] payload = LiteJson.Serialize(new C2S_CreateRoom
        {
            RoomConfig = new RoomDTO
            {
                RoomName = roomName,
                ComponentIds = new[] { ComponentIds.SocialRoom, ComponentIds.RoomSettings, ComponentIds.ObjectSync },
                MaxMembers = _options.ClientsPerRoom + _options.RedundantClientsPerRoom,
                EnableReplayRecording = false,
                Password = string.Empty,
                CustomProperties = new Dictionary<string, string>()
            }
        });

        SendPacket(MsgIds.C2S_CreateRoom, NetScope.Global, string.Empty, payload);
    }

    private void SendJoinRoom(string roomId)
    {
        if (_joinRoomSent)
        {
            return;
        }

        _joinRoomSent = true;
        byte[] payload = LiteJson.Serialize(new C2S_JoinRoom
        {
            RoomId = roomId,
            Password = string.Empty
        });
        SendPacket(MsgIds.C2S_JoinRoom, NetScope.Global, string.Empty, payload);
    }

    private void SendRoomSetupReady()
    {
        if (_roomSetupSent)
        {
            return;
        }

        _roomSetupSent = true;
        SetState(BotState.AwaitRoomSetup, Environment.TickCount64);
        byte[] payload = LiteJson.Serialize(new C2S_RoomSetupReady
        {
            RoomId = _roomId
        });
        SendPacket(MsgIds.C2S_RoomSetupReady, NetScope.Global, string.Empty, payload);
    }

    private void SendReady()
    {
        if (_readySent)
        {
            return;
        }

        _readySent = true;
        byte[] payload = LiteJson.Serialize(new C2S_SetReady
        {
            IsReady = true
        });
        SendPacket(MsgIds.C2S_SetReady, NetScope.Room, _roomId, payload);
    }

    private void SendStartGame()
    {
        byte[] payload = LiteJson.Serialize(new C2S_StartGame());
        Console.WriteLine($"[信息][{_tag}] 房主发起开局请求。");
        SendPacket(MsgIds.C2S_StartGame, NetScope.Room, _roomId, payload);
    }

    private void UpdateNormalPlayerBehavior(long nowMs)
    {
        if (nowMs >= _nextBehaviorAtMs)
        {
            if (_isWalking)
            {
                _isWalking = false;
                _sentStopPacket = false;
                _nextBehaviorAtMs = nowMs + NextRange(1500, 4200);
            }
            else
            {
                _isWalking = true;
                _sentStopPacket = false;
                _targetPosX = Math.Clamp(_currentPosX + (float)(_random.NextDouble() * 2.4 - 1.2), -2.5f, 2.5f);
                _targetPosZ = Math.Clamp(_currentPosZ + (float)(_random.NextDouble() * 2.4 - 1.2), -2.5f, 2.5f);
                _nextBehaviorAtMs = nowMs + NextRange(1800, 4200);
            }
        }

        if (nowMs >= _nextActionAtMs)
        {
            if (_isWalking)
            {
                _nextActionAtMs = nowMs + NextRange(1000, 2500);
            }
            else
            {
                _nextActionAtMs = nowMs + NextRange(15000, 30000);
                SendAction(_random.NextDouble() < 0.5 ? 1 : 2);
            }
        }

        if (nowMs >= _nextBubbleAtMs)
        {
            if (_isWalking)
            {
                _nextBubbleAtMs = nowMs + NextRange(1500, 3500);
            }
            else
            {
                _nextBubbleAtMs = nowMs + NextRange(25000, 45000);
                SendBubble(RandomAsciiBubbleText());
            }
        }

        if (nowMs - _lastMoveAtMs < _moveIntervalMs)
        {
            return;
        }

        double deltaTime = Math.Max(0.001d, (nowMs - _lastMoveAtMs) / 1000d);
        _lastMoveAtMs = nowMs;

        if (_isWalking)
        {
            float dx = _targetPosX - _currentPosX;
            float dz = _targetPosZ - _currentPosZ;
            float distance = MathF.Sqrt(dx * dx + dz * dz);
            if (distance < 0.08f)
            {
                _isWalking = false;
                _sentStopPacket = false;
                _nextBehaviorAtMs = nowMs + NextRange(1500, 3600);
            }
            else
            {
                const float moveSpeed = 2.2f;
                float velX = dx / distance * moveSpeed;
                float velZ = dz / distance * moveSpeed;
                _currentPosX += (float)(velX * deltaTime);
                _currentPosZ += (float)(velZ * deltaTime);
                SendMove(_currentPosX, _currentPosZ, velX, velZ);
                return;
            }
        }

        if (!_sentStopPacket)
        {
            _sentStopPacket = true;
            SendMove(_currentPosX, _currentPosZ, 0f, 0f);
        }
    }

    private void SendMove(float posX, float posZ, float velX, float velZ)
    {
        float rotY = velX == 0f && velZ == 0f ? 0f : (MathF.Atan2(velX, velZ) * 180f / MathF.PI + 360f) % 360f;

        byte[] payload = SocialMoveSerializer.Serialize(new SocialMovePayload
        {
            PosX = posX,
            PosY = 0f,
            PosZ = posZ,
            VelX = velX,
            VelY = 0f,
            VelZ = velZ,
            RotY = rotY
        });

        SendPacket(MsgIds.C2S_SocialMoveReq, NetScope.Room, _roomId, payload);
    }

    private void SendAction(int actionId)
    {
        byte[] payload = SocialActionSerializer.Serialize(actionId);
        SendPacket(MsgIds.C2S_SocialActionReq, NetScope.Room, _roomId, payload);
    }

    private void SendBubble(string content)
    {
        byte[] payload = SocialBubbleSerializer.Serialize(content);
        SendPacket(MsgIds.C2S_SocialBubbleReq, NetScope.Room, _roomId, payload);
    }

    private void SendPacket(int msgId, NetScope scope, string roomId, byte[] payload)
    {
        PacketData packet = new(++_seq, msgId, scope, roomId, payload, payload.Length);
        int length = LitePacketCodec.Serialize(packet, _sendBuffer, 0);
        _transport.Send(_sendBuffer, length);
        _stats.MarkSent(length);
    }

    private void SetState(BotState newState, long nowMs)
    {
        _state = newState;
        _stateEnterAtMs = nowMs;
        _hasLoggedStateTimeout = false;
    }

    private void CheckStateTimeout(long nowMs)
    {
        if (_hasLoggedStateTimeout)
        {
            return;
        }

        long threshold = _state switch
        {
            BotState.Connecting => ConnectTimeoutMs,
            BotState.AwaitLogin => LoginTimeoutMs,
            BotState.AwaitCreateRoom => CreateRoomTimeoutMs,
            BotState.AwaitJoinRoom => JoinRoomTimeoutMs,
            BotState.AwaitRoomSetup => RoomSetupTimeoutMs,
            BotState.LeavingRoom => LeaveRoomTimeoutMs,
            _ => -1
        };

        if (threshold <= 0 || nowMs - _stateEnterAtMs < threshold)
        {
            return;
        }

        _hasLoggedStateTimeout = true;
        Console.WriteLine($"[超时][{_tag}] 卡在状态 {BuildStateText()} 超过 {threshold} ms");
    }

    private int NextRange(int min, int max)
    {
        return _random.Next(min, max + 1);
    }

    private string RandomAsciiBubbleText()
    {
        string[] texts = { "hello", "ok", "moving", "wait here", "someone here", "ready", "nice" };
        return texts[_random.Next(0, texts.Length)];
    }

    private string RandomBubbleText()
    {
        string[] texts = { "你好", "收到", "测试", "走起", "这里有人", "ok", "准备好了" };
        return texts[_random.Next(0, texts.Length)];
    }

    private string BuildStateText()
    {
        return _state switch
        {
            BotState.Idle => "空闲",
            BotState.Connecting => "连接中",
            BotState.AwaitLogin => "等待登录结果",
            BotState.AwaitCreateRoom => "等待建房结果",
            BotState.AwaitJoinRoom => "等待加房结果",
            BotState.AwaitRoomSetup => "等待进房确认",
            BotState.RoomWaiting => "房内等待开局",
            BotState.ReadySent => "已发送Ready",
            BotState.InGame => "游戏中",
            BotState.Disconnected => "已断开",
            BotState.Failed => "失败",
            _ => _state.ToString()
        };
    }
}

internal sealed class LoadTestStats
{
    private long _startedClients;
    private long _connectedClients;
    private long _loginSuccess;
    private long _roomSetupSuccess;
    private long _gameStartedClients;
    private long _sentPackets;
    private long _receivedPackets;
    private long _sentBytes;
    private long _receivedBytes;
    private long _errors;

    public void MarkStarted() => Interlocked.Increment(ref _startedClients);
    public void MarkConnected() => Interlocked.Increment(ref _connectedClients);
    public void MarkLoginSuccess() => Interlocked.Increment(ref _loginSuccess);
    public void MarkRoomSetupSuccess() => Interlocked.Increment(ref _roomSetupSuccess);
    public void MarkGameStarted() => Interlocked.Increment(ref _gameStartedClients);

    public void MarkSent(int bytes)
    {
        Interlocked.Increment(ref _sentPackets);
        Interlocked.Add(ref _sentBytes, bytes);
    }

    public void MarkReceived(int bytes)
    {
        Interlocked.Increment(ref _receivedPackets);
        Interlocked.Add(ref _receivedBytes, bytes);
    }

    public void MarkError() => Interlocked.Increment(ref _errors);

    public Snapshot Capture(double elapsedSeconds)
    {
        return new Snapshot
        {
            ElapsedSeconds = elapsedSeconds,
            StartedClients = Interlocked.Read(ref _startedClients),
            ConnectedClients = Interlocked.Read(ref _connectedClients),
            LoginSuccess = Interlocked.Read(ref _loginSuccess),
            RoomSetupSuccess = Interlocked.Read(ref _roomSetupSuccess),
            GameStartedClients = Interlocked.Read(ref _gameStartedClients),
            SentPackets = Interlocked.Read(ref _sentPackets),
            ReceivedPackets = Interlocked.Read(ref _receivedPackets),
            SentBytes = Interlocked.Read(ref _sentBytes),
            ReceivedBytes = Interlocked.Read(ref _receivedBytes),
            Errors = Interlocked.Read(ref _errors)
        };
    }

    internal sealed class Snapshot
    {
        public double ElapsedSeconds;
        public long StartedClients;
        public long ConnectedClients;
        public long LoginSuccess;
        public long RoomSetupSuccess;
        public long GameStartedClients;
        public long SentPackets;
        public long ReceivedPackets;
        public long SentBytes;
        public long ReceivedBytes;
        public long Errors;
    }
}
