using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Infrastructure
{
    /// <summary>
    /// 房间分片调度器。
    /// 负责把房间分配到负载较低的工作线程，并把房间域消息投递到对应线程执行。
    /// </summary>
    public sealed class ServerRoomScheduler : IDisposable
    {
        private sealed class RoomWorker : IDisposable
        {
            private readonly int _workerId;
            private readonly float _tickIntervalSeconds;
            private readonly Func<float> _realtimeProvider;
            private readonly ConcurrentQueue<Action> _pendingActions = new ConcurrentQueue<Action>();
            private readonly AutoResetEvent _signal = new AutoResetEvent(false);
            private readonly Dictionary<string, Room> _rooms = new Dictionary<string, Room>();
            private readonly Stopwatch _stopwatch = new Stopwatch();

            private CancellationTokenSource _cts;
            private Thread _thread;
            private volatile bool _isRunning;
            private volatile int _pendingActionCount;
            private volatile int _ownedRoomCount;
            private volatile float _averageTickMs;

            public RoomWorker(int workerId, int tickRate, Func<float> realtimeProvider)
            {
                _workerId = workerId;
                _tickIntervalSeconds = tickRate > 0 ? 1f / tickRate : 1f / 60f;
                _realtimeProvider = realtimeProvider ?? (() => 0f);
            }

            public int WorkerId => _workerId;
            public int OwnedRoomCount => _ownedRoomCount;
            public int PendingActionCount => _pendingActionCount;
            public float AverageTickMs => _averageTickMs;
            public float LoadScore => (_averageTickMs * 1000f) + (_ownedRoomCount * 100f) + (_pendingActionCount * 10f);

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
                    Name = $"StellarNet.RoomWorker.{_workerId}"
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
                _signal.Set();
                if (_thread != null && _thread.IsAlive)
                {
                    _thread.Join(2000);
                }

                _thread = null;
                _cts?.Dispose();
                _cts = null;
                _stopwatch.Reset();
            }

            public void RegisterRoom(Room room)
            {
                if (room == null)
                {
                    return;
                }

                ExecuteSync(() =>
                {
                    _rooms[room.RoomId] = room;
                    _ownedRoomCount = _rooms.Count;
                    room.SetAssignedWorker(_workerId);
                    room.UpdateWorkerMetrics(_workerId, _averageTickMs);
                });
            }

            public void UnregisterRoom(string roomId)
            {
                if (string.IsNullOrEmpty(roomId))
                {
                    return;
                }

                ExecuteSync(() =>
                {
                    _rooms.Remove(roomId);
                    _ownedRoomCount = _rooms.Count;
                });
            }

            public void Enqueue(Action action)
            {
                if (action == null)
                {
                    return;
                }

                _pendingActions.Enqueue(action);
                Interlocked.Increment(ref _pendingActionCount);
                _signal.Set();
            }

            public void Dispose()
            {
                Stop();
                _signal.Dispose();
            }

            private void ExecuteSync(Action action)
            {
                if (action == null)
                {
                    return;
                }

                using (var done = new ManualResetEventSlim(false))
                {
                    Exception error = null;
                    Enqueue(() =>
                    {
                        try
                        {
                            action();
                        }
                        catch (Exception ex)
                        {
                            error = ex;
                        }
                        finally
                        {
                            done.Set();
                        }
                    });

                    if (!done.Wait(TimeSpan.FromSeconds(5)))
                    {
                        throw new TimeoutException($"RoomWorker[{_workerId}] synchronous action timed out.");
                    }

                    if (error != null)
                    {
                        throw error;
                    }
                }
            }

            private void RunLoop()
            {
                _stopwatch.Start();
                double nextTickAt = 0d;

                try
                {
                    while ((_cts != null && !_cts.IsCancellationRequested) || !_pendingActions.IsEmpty)
                    {
                        DrainPendingActions();

                        double nowSeconds = _stopwatch.Elapsed.TotalSeconds;
                        while (nowSeconds >= nextTickAt && (_cts == null || !_cts.IsCancellationRequested))
                        {
                            TickOwnedRooms();
                            nextTickAt += _tickIntervalSeconds;
                            nowSeconds = _stopwatch.Elapsed.TotalSeconds;
                        }

                        if (_pendingActions.IsEmpty)
                        {
                            double waitSeconds = nextTickAt - nowSeconds;
                            int waitMilliseconds = waitSeconds > 0d ? Math.Max(1, (int)Math.Floor(waitSeconds * 1000d)) : 1;
                            _signal.WaitOne(waitMilliseconds);
                        }
                        else
                        {
                            Thread.Yield();
                        }
                    }
                }
                finally
                {
                    DrainPendingActions();
                    _stopwatch.Stop();
                }
            }

            private void DrainPendingActions()
            {
                while (_pendingActions.TryDequeue(out Action action))
                {
                    Interlocked.Decrement(ref _pendingActionCount);
                    try
                    {
                        action?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        NetLogger.LogError("ServerRoomScheduler", $"房间工作线程动作异常: Worker:{_workerId}, {ex.GetType().Name}, {ex.Message}");
                    }
                }
            }

            private void TickOwnedRooms()
            {
                if (_rooms.Count == 0)
                {
                    _averageTickMs = 0f;
                    return;
                }

                Room[] rooms = _rooms.Values.ToArray();
                float realtime = _realtimeProvider();
                DateTime utcNow = DateTime.UtcNow;
                long tickStart = Stopwatch.GetTimestamp();

                for (int i = 0; i < rooms.Length; i++)
                {
                    Room room = rooms[i];
                    if (room == null)
                    {
                        continue;
                    }

                    room.UpdateRuntimeContext(realtime, utcNow);
                    room.Tick();
                }

                double elapsedMs = (Stopwatch.GetTimestamp() - tickStart) * 1000d / Stopwatch.Frequency;
                _averageTickMs = _averageTickMs <= 0f
                    ? (float)elapsedMs
                    : (_averageTickMs * 0.85f) + ((float)elapsedMs * 0.15f);

                for (int i = 0; i < rooms.Length; i++)
                {
                    Room room = rooms[i];
                    if (room != null)
                    {
                        room.UpdateWorkerMetrics(_workerId, _averageTickMs);
                    }
                }
            }
        }

        private readonly RoomWorker[] _workers;
        private readonly Dictionary<string, int> _roomToWorker = new Dictionary<string, int>();

        public ServerRoomScheduler(NetConfig config, Func<float> realtimeProvider)
        {
            int tickRate = config != null && config.TickRate > 0 ? config.TickRate : 60;
            int workerCount = ResolveWorkerCount(config);
            _workers = new RoomWorker[workerCount];

            for (int i = 0; i < workerCount; i++)
            {
                _workers[i] = new RoomWorker(i, tickRate, realtimeProvider);
            }
        }

        public int WorkerCount => _workers.Length;

        public void Start()
        {
            for (int i = 0; i < _workers.Length; i++)
            {
                _workers[i].Start();
            }
        }

        public void Stop()
        {
            for (int i = 0; i < _workers.Length; i++)
            {
                _workers[i].Stop();
            }
        }

        public int RegisterRoom(Room room)
        {
            if (room == null)
            {
                return -1;
            }

            int workerIndex = SelectWorkerIndex();
            lock (_roomToWorker)
            {
                _roomToWorker[room.RoomId] = workerIndex;
            }

            _workers[workerIndex].RegisterRoom(room);
            return workerIndex;
        }

        public void UnregisterRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                return;
            }

            int workerIndex;
            lock (_roomToWorker)
            {
                if (!_roomToWorker.TryGetValue(roomId, out workerIndex))
                {
                    return;
                }

                _roomToWorker.Remove(roomId);
            }

            _workers[workerIndex].UnregisterRoom(roomId);
        }

        public void DispatchPacketToRoom(Room room, Session session, Packet packet)
        {
            if (room == null || session == null)
            {
                return;
            }

            int workerIndex;
            lock (_roomToWorker)
            {
                if (!_roomToWorker.TryGetValue(room.RoomId, out workerIndex))
                {
                    return;
                }
            }

            byte[] payloadCopy = null;
            if (packet.PayloadLength > 0)
            {
                payloadCopy = ArrayPool<byte>.Shared.Rent(packet.PayloadLength);
                Buffer.BlockCopy(packet.Payload, packet.PayloadOffset, payloadCopy, 0, packet.PayloadLength);
            }

            Packet safePacket = new Packet(packet.Seq, packet.MsgId, packet.Scope, packet.RoomId, payloadCopy ?? Array.Empty<byte>(), 0, packet.PayloadLength);
            _workers[workerIndex].Enqueue(() =>
            {
                try
                {
                    if (session.CurrentRoomId != room.RoomId)
                    {
                        return;
                    }

                    if (room.GetMember(session.SessionId) == null)
                    {
                        return;
                    }

                    room.DispatchPacket(session, safePacket);
                }
                finally
                {
                    if (payloadCopy != null)
                    {
                        ArrayPool<byte>.Shared.Return(payloadCopy);
                    }
                }
            });
        }

        public int GetAssignedWorkerId(string roomId)
        {
            lock (_roomToWorker)
            {
                return _roomToWorker.TryGetValue(roomId, out int workerIndex) ? workerIndex : -1;
            }
        }

        public (int WorkerId, int Rooms, int Pending, float AvgTickMs, float LoadScore)[] CaptureWorkerStats()
        {
            var result = new (int WorkerId, int Rooms, int Pending, float AvgTickMs, float LoadScore)[_workers.Length];
            for (int i = 0; i < _workers.Length; i++)
            {
                RoomWorker worker = _workers[i];
                result[i] = (worker.WorkerId, worker.OwnedRoomCount, worker.PendingActionCount, worker.AverageTickMs, worker.LoadScore);
            }

            return result;
        }

        public void Dispose()
        {
            Stop();
            for (int i = 0; i < _workers.Length; i++)
            {
                _workers[i].Dispose();
            }
        }

        private int SelectWorkerIndex()
        {
            int bestIndex = 0;
            float bestScore = float.MaxValue;
            for (int i = 0; i < _workers.Length; i++)
            {
                RoomWorker worker = _workers[i];
                float score = worker.LoadScore;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                    continue;
                }

                if (Math.Abs(score - bestScore) < 0.001f && worker.OwnedRoomCount < _workers[bestIndex].OwnedRoomCount)
                {
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static int ResolveWorkerCount(NetConfig config)
        {
            if (config != null && config.RoomWorkerCount > 0)
            {
                return Math.Max(1, config.RoomWorkerCount);
            }

            int reserveCpu = config != null ? Math.Max(0, config.RoomWorkerReserveCpuCount) : 1;
            int available = Math.Max(1, Environment.ProcessorCount - reserveCpu);
            return available;
        }
    }
}
