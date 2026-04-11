using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StellarNet.Lite.Server.Infrastructure
{
    /// <summary>
    /// 异步持久化任务快照，用于观察当前仍在执行的任务。
    /// </summary>
    public sealed class PersistencePendingInfo
    {
        public Guid Id { get; }
        public string Owner { get; }
        public string Operation { get; }
        public DateTime StartUtc { get; }
        public bool JoinOnShutdown { get; }
        public TimeSpan Elapsed => DateTime.UtcNow - StartUtc;

        internal PersistencePendingInfo(Guid id, string owner, string operation, DateTime startUtc, bool joinOnShutdown)
        {
            Id = id;
            Owner = owner;
            Operation = operation;
            StartUtc = startUtc;
            JoinOnShutdown = joinOnShutdown;
        }
    }

    /// <summary>
    /// 停机排空结果。
    /// </summary>
    public sealed class PersistenceDrainResult
    {
        public bool Completed { get; }
        public PersistencePendingInfo[] Pending { get; }
        public int PendingCount => Pending == null ? 0 : Pending.Length;

        public PersistenceDrainResult(bool completed, PersistencePendingInfo[] pending)
        {
            Completed = completed;
            Pending = pending ?? Array.Empty<PersistencePendingInfo>();
        }
    }

    /// <summary>
    /// 仅负责托管异步持久化任务，不关心业务内容和具体存储策略。
    /// </summary>
    public sealed class ServerPersistenceRuntime
    {
        private readonly object _gate = new object();
        private readonly Dictionary<Guid, PersistencePendingInfo> _pending = new Dictionary<Guid, PersistencePendingInfo>();
        private TaskCompletionSource<bool> _joinOnShutdownIdleSource = CreateCompletedIdleSource();
        private int _joinOnShutdownPendingCount;
        private bool _isShuttingDown;

        public bool IsShuttingDown
        {
            get
            {
                lock (_gate)
                {
                    return _isShuttingDown;
                }
            }
        }

        public int PendingCount
        {
            get
            {
                lock (_gate)
                {
                    return _pending.Count;
                }
            }
        }

        public int JoinOnShutdownPendingCount
        {
            get
            {
                lock (_gate)
                {
                    return _joinOnShutdownPendingCount;
                }
            }
        }

        public Task TrackAsync(
            string owner,
            string operation,
            Func<Task> work,
            bool joinOnShutdown = true,
            bool allowStartDuringShutdown = false)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));

            return TrackAsync(
                owner,
                operation,
                _ => work(),
                joinOnShutdown,
                allowStartDuringShutdown,
                default(CancellationToken));
        }

        public Task<T> TrackAsync<T>(
            string owner,
            string operation,
            Func<Task<T>> work,
            bool joinOnShutdown = true,
            bool allowStartDuringShutdown = false)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));

            return TrackAsync(
                owner,
                operation,
                _ => work(),
                joinOnShutdown,
                allowStartDuringShutdown,
                default(CancellationToken));
        }

        public Task TrackAsync(
            string owner,
            string operation,
            Func<CancellationToken, Task> work,
            bool joinOnShutdown = true,
            bool allowStartDuringShutdown = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (work == null) throw new ArgumentNullException(nameof(work));

            PersistencePendingInfo info = Register(owner, operation, joinOnShutdown, allowStartDuringShutdown);
            return RunTrackedAsync(info, work, cancellationToken);
        }

        public Task<T> TrackAsync<T>(
            string owner,
            string operation,
            Func<CancellationToken, Task<T>> work,
            bool joinOnShutdown = true,
            bool allowStartDuringShutdown = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (work == null) throw new ArgumentNullException(nameof(work));

            PersistencePendingInfo info = Register(owner, operation, joinOnShutdown, allowStartDuringShutdown);
            return RunTrackedAsync(info, work, cancellationToken);
        }

        public void BeginShutdown()
        {
            lock (_gate)
            {
                _isShuttingDown = true;
            }
        }

        public IReadOnlyList<PersistencePendingInfo> SnapshotPending()
        {
            lock (_gate)
            {
                return _pending.Values
                    .OrderBy(info => info.StartUtc)
                    .ToArray();
            }
        }

        public async Task<PersistenceDrainResult> WaitForIdleAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be non-negative or Timeout.InfiniteTimeSpan.");
            }

            DateTime? deadlineUtc = timeout == Timeout.InfiniteTimeSpan ? (DateTime?)null : DateTime.UtcNow + timeout;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Task idleTask;
                lock (_gate)
                {
                    if (_joinOnShutdownPendingCount == 0)
                    {
                        return new PersistenceDrainResult(true, Array.Empty<PersistencePendingInfo>());
                    }

                    idleTask = _joinOnShutdownIdleSource.Task;
                }

                if (!deadlineUtc.HasValue)
                {
                    await idleTask.ConfigureAwait(false);
                    continue;
                }

                TimeSpan remaining = deadlineUtc.Value - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    PersistencePendingInfo[] timeoutPending = SnapshotJoinOnShutdownPending();
                    return new PersistenceDrainResult(timeoutPending.Length == 0, timeoutPending);
                }

                Task delayTask = Task.Delay(remaining, cancellationToken);
                Task completedTask = await Task.WhenAny(idleTask, delayTask).ConfigureAwait(false);
                if (completedTask == idleTask)
                {
                    await idleTask.ConfigureAwait(false);
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                PersistencePendingInfo[] pending = SnapshotJoinOnShutdownPending();
                return new PersistenceDrainResult(pending.Length == 0, pending);
            }
        }

        private PersistencePendingInfo Register(string owner, string operation, bool joinOnShutdown, bool allowStartDuringShutdown)
        {
            lock (_gate)
            {
                if (_isShuttingDown && !allowStartDuringShutdown)
                {
                    throw new InvalidOperationException(
                        $"Persistence runtime is shutting down. Task '{Normalize(operation, "unnamed")}' was blocked.");
                }

                if (joinOnShutdown && (_joinOnShutdownPendingCount == 0 || _joinOnShutdownIdleSource.Task.IsCompleted))
                {
                    _joinOnShutdownIdleSource = new TaskCompletionSource<bool>();
                }

                var info = new PersistencePendingInfo(
                    Guid.NewGuid(),
                    Normalize(owner, "unknown"),
                    Normalize(operation, "unnamed"),
                    DateTime.UtcNow,
                    joinOnShutdown);

                _pending.Add(info.Id, info);
                if (joinOnShutdown)
                {
                    _joinOnShutdownPendingCount++;
                }

                return info;
            }
        }

        private async Task RunTrackedAsync(
            PersistencePendingInfo info,
            Func<CancellationToken, Task> work,
            CancellationToken cancellationToken)
        {
            try
            {
                await work(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Complete(info);
            }
        }

        private async Task<T> RunTrackedAsync<T>(
            PersistencePendingInfo info,
            Func<CancellationToken, Task<T>> work,
            CancellationToken cancellationToken)
        {
            try
            {
                return await work(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Complete(info);
            }
        }

        private void Complete(PersistencePendingInfo info)
        {
            if (info == null)
            {
                return;
            }

            lock (_gate)
            {
                if (!_pending.Remove(info.Id))
                {
                    return;
                }

                if (!info.JoinOnShutdown)
                {
                    return;
                }

                _joinOnShutdownPendingCount = Math.Max(0, _joinOnShutdownPendingCount - 1);
                if (_joinOnShutdownPendingCount == 0)
                {
                    _joinOnShutdownIdleSource.TrySetResult(true);
                }
            }
        }

        private PersistencePendingInfo[] SnapshotJoinOnShutdownPending()
        {
            lock (_gate)
            {
                return _pending.Values
                    .Where(info => info.JoinOnShutdown)
                    .OrderBy(info => info.StartUtc)
                    .ToArray();
            }
        }

        private static TaskCompletionSource<bool> CreateCompletedIdleSource()
        {
            var source = new TaskCompletionSource<bool>();
            source.SetResult(true);
            return source;
        }

        private static string Normalize(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
