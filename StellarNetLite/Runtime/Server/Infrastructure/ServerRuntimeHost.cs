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

        private readonly ServerApp _serverApp;
        private readonly IServerTransportPump _serverTransportPump;
        private readonly ConcurrentQueue<Action> _pendingActions = new ConcurrentQueue<Action>();
        private readonly AutoResetEvent _workSignal = new AutoResetEvent(false);
        private readonly Stopwatch _stopwatch = new Stopwatch();

        private CancellationTokenSource _cts;
        private Thread _thread;
        private volatile int _serverThreadId = -1;
        private bool _isRunning;

        public ServerRuntimeHost(ServerApp serverApp, INetworkTransport transport)
        {
            _serverApp = serverApp;
            _serverTransportPump = transport as IServerTransportPump;
        }

        public float RealtimeSinceStartup => (float)_stopwatch.Elapsed.TotalSeconds;

        public bool IsCurrentThread => Thread.CurrentThread.ManagedThreadId == _serverThreadId;

        public void Start()
        {
            if (_isRunning)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _thread = new Thread(RunLoop)
            {
                IsBackground = true,
                Name = "StellarNet.ServerRuntime"
            };
            _isRunning = true;
            _thread.Start();
        }

        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
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

        public void Dispose()
        {
            Stop();
            _workSignal.Dispose();
        }

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
                }

                _stopwatch.Stop();
                _serverThreadId = -1;
            }
        }

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
