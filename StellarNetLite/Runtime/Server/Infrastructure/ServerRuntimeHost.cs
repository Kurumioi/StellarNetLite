using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Infrastructure
{
    /// <summary>
    /// 服务端独立运行宿主。
    /// 负责在非 Unity 生命周期中驱动传输层泵与固定 Tick。
    /// </summary>
    public sealed class ServerRuntimeHost : IDisposable
    {
        // KCP 默认 interval 是 10ms。
        // 这里把宿主线程的最大阻塞等待提升到 10ms，避免在 Linux 无头模式下以 2ms 频率高频空转。
        private const int MaxWaitMilliseconds = 10;
        private const int NearDeadlineWaitMilliseconds = 1;

        /// <summary>
        /// 当前服务端主状态机。
        /// </summary>
        private readonly ServerApp _serverApp;

        /// <summary>
        /// 可主动驱动的服务端传输层网络泵。
        /// </summary>
        private readonly IServerTransportPump _serverTransportPump;

        /// <summary>
        /// 房间分片调度器。
        /// </summary>
        private readonly ServerRoomScheduler _roomScheduler;

        /// <summary>
        /// 等待在服务端线程执行的动作队列。
        /// </summary>
        private readonly ConcurrentQueue<Action> _pendingActions = new ConcurrentQueue<Action>();

        /// <summary>
        /// 用于唤醒宿主线程的事件。
        /// </summary>
        private readonly AutoResetEvent _workSignal = new AutoResetEvent(false);

        /// <summary>
        /// 宿主线程内部计时器。
        /// </summary>
        private readonly Stopwatch _stopwatch = new Stopwatch();

        /// <summary>
        /// 宿主线程取消源。
        /// </summary>
        private CancellationTokenSource _cts;

        /// <summary>
        /// 当前宿主线程。
        /// </summary>
        private Thread _thread;

        /// <summary>
        /// 当前宿主线程 Id。
        /// </summary>
        private volatile int _serverThreadId = -1;

        /// <summary>
        /// 当前宿主是否正在运行。
        /// </summary>
        private bool _isRunning;

        /// <summary>
        /// 创建服务端独立运行宿主。
        /// </summary>
        public ServerRuntimeHost(ServerApp serverApp, INetworkTransport transport)
        {
            _serverApp = serverApp;
            _serverTransportPump = transport as IServerTransportPump;
            _roomScheduler = new ServerRoomScheduler(serverApp.Config, () => RealtimeSinceStartup);
            _serverApp.AttachRoomScheduler(_roomScheduler);
        }

        /// <summary>
        /// 当前宿主累计运行秒数。
        /// </summary>
        public float RealtimeSinceStartup => (float)_stopwatch.Elapsed.TotalSeconds;

        /// <summary>
        /// 当前线程是否为服务端宿主线程。
        /// </summary>
        public bool IsCurrentThread => Thread.CurrentThread.ManagedThreadId == _serverThreadId;

        /// <summary>
        /// 启动服务端宿主线程和房间调度器。
        /// </summary>
        public void Start()
        {
            if (_isRunning)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _roomScheduler.Start();
            _thread = new Thread(RunLoop)
            {
                IsBackground = true,
                Name = "StellarNet.ServerRuntime"
            };
            _isRunning = true;
            _thread.Start();
        }

        /// <summary>
        /// 停止服务端宿主线程。
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
            _roomScheduler.Stop();
            _cts?.Cancel();
            _workSignal.Set();

            if (_thread != null && _thread.IsAlive && !IsCurrentThread)
            {
                _thread.Join(2000);
            }

            _serverThreadId = -1;
            _thread = null;
            _cts?.Dispose();
            _cts = null;
            _stopwatch.Reset();
        }

        /// <summary>
        /// 当前线程直接执行，其它线程排队到服务端线程执行。
        /// </summary>
        public void ExecuteOrEnqueue(Action action)
        {
            if (action == null)
            {
                return;
            }

            if (IsCurrentThread)
            {
                lock (_serverApp.SyncRoot)
                {
                    action();
                }
                return;
            }

            _pendingActions.Enqueue(action);
            _workSignal.Set();
        }

        /// <summary>
        /// 释放宿主和调度器资源。
        /// </summary>
        public void Dispose()
        {
            Stop();
            _roomScheduler.Dispose();
            _workSignal.Dispose();
        }

        /// <summary>
        /// 服务端宿主主循环。
        /// </summary>
        private void RunLoop()
        {
            _serverThreadId = Thread.CurrentThread.ManagedThreadId;
            _stopwatch.Start();

            int tickRate = _serverApp.Config != null && _serverApp.Config.TickRate > 0
                ? _serverApp.Config.TickRate
                : 60;
            double tickIntervalSeconds = 1.0d / tickRate;
            double nextTickAt = 0d;

            try
            {
                while (_cts != null && !_cts.IsCancellationRequested)
                {
                    DateTime nowUtc = DateTime.UtcNow;
                    double nowSeconds = _stopwatch.Elapsed.TotalSeconds;

                    lock (_serverApp.SyncRoot)
                    {
                        _serverApp.UpdateRuntimeContext((float)nowSeconds, nowUtc);
                        try
                        {
                            _serverTransportPump?.PumpServer();
                        }
                        catch (Exception ex)
                        {
                            NetLogger.LogError("ServerRuntimeHost", $"服务端网络泵异常: {ex.GetType().Name}, {ex.Message}");
                        }

                        DrainPendingActions();
                        _serverApp.FlushOutboundPackets();
                    }

                    nowSeconds = _stopwatch.Elapsed.TotalSeconds;
                    while (nowSeconds >= nextTickAt && _cts != null && !_cts.IsCancellationRequested)
                    {
                        lock (_serverApp.SyncRoot)
                        {
                            _serverApp.UpdateRuntimeContext((float)nextTickAt, DateTime.UtcNow);
                            try
                            {
                                _serverApp.Tick();
                                _serverApp.FlushOutboundPackets();
                            }
                            catch (Exception ex)
                            {
                                NetLogger.LogError("ServerRuntimeHost", $"服务端 Tick 异常: {ex.GetType().Name}, {ex.Message}");
                            }
                        }

                        nextTickAt += tickIntervalSeconds;
                        nowSeconds = _stopwatch.Elapsed.TotalSeconds;
                    }

                    int waitMilliseconds = CalculateWaitMilliseconds(nextTickAt, nowSeconds);
                    if (waitMilliseconds > 0 && _pendingActions.IsEmpty)
                    {
                        _workSignal.WaitOne(waitMilliseconds);
                    }
                    else if (_pendingActions.IsEmpty)
                    {
                        // 临近 Tick 截止时间时做极短等待，避免在 Linux 上退化成高频 Yield 空转。
                        _workSignal.WaitOne(NearDeadlineWaitMilliseconds);
                    }
                    else
                    {
                        Thread.Yield();
                    }
                }
            }
            finally
            {
                lock (_serverApp.SyncRoot)
                {
                    DateTime nowUtc = DateTime.UtcNow;
                    double nowSeconds = _stopwatch.Elapsed.TotalSeconds;
                    _serverApp.UpdateRuntimeContext((float)nowSeconds, nowUtc);
                    DrainPendingActions();
                    _serverApp.FlushOutboundPackets();
                }

                _stopwatch.Stop();
                _serverThreadId = -1;
            }
        }

        /// <summary>
        /// 执行当前所有等待中的服务端线程动作。
        /// </summary>
        private void DrainPendingActions()
        {
            while (_pendingActions.TryDequeue(out Action action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    NetLogger.LogError("ServerRuntimeHost", $"服务端队列动作异常: {ex.GetType().Name}, {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 计算距下一个 Tick 的等待毫秒数。
        /// </summary>
        private static int CalculateWaitMilliseconds(double nextTickAt, double nowSeconds)
        {
            double secondsUntilNextTick = nextTickAt - nowSeconds;
            if (secondsUntilNextTick <= 0d)
            {
                return 0;
            }

            int waitMilliseconds = (int)Math.Floor(secondsUntilNextTick * 1000d);
            if (waitMilliseconds <= 0)
            {
                return 0;
            }

            return waitMilliseconds > MaxWaitMilliseconds ? MaxWaitMilliseconds : waitMilliseconds;
        }
    }
}
