using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RCi.Toolbox.Boxes;

namespace RCi.Toolbox
{
    /// <summary>
    /// Job execution result.
    /// </summary>
    /// <param name="Cancelled">Whether job queue was cancelled after job was already enqueued.</param>
    /// <param name="Exception">Whether exception was thrown during job execution.</param>
    /// <param name="Result">Job result.</param>
    public readonly record struct JobResult<T>(bool Cancelled, Exception? Exception, T? Result);

    /// <inheritdoc cref="JobResult{T}"/>
    public readonly record struct JobResult(bool Cancelled, Exception? Exception);

    /// <summary>
    /// Job queue for asynchronous job execution.
    /// Job queue can have many workers for job execution.
    /// Each worker executes enqueued jobs in parallel.
    /// </summary>
    public interface IJobQueue
    {
        /// <summary>
        /// Dedicated worker count. If there's only one worker, it will execute enqueued jobs in sequence,
        /// otherwise multiple workers will dequeue and execute jobs in parallel.
        /// </summary>
        int WorkerCount { get; }

        /// <summary>
        /// Active worker count. 0 - job queue is idle, otherwise it is busy (actively executing at least one job).
        /// </summary>
        int ActiveWorkerCount { get; }

        /// <summary>
        /// Whether job queue is currently idle (all workers are idle).
        /// </summary>
        bool IsIdle { get; }

        /// <summary>
        /// Whether job queue is currently working (at least one worker is active).
        /// </summary>
        bool IsBusy { get; }

        /// <summary>
        /// Whether job queue is cancelled.
        /// </summary>
        bool IsCancelled { get; }

        /// <summary>
        /// Cancel job queue. Will not accept new jobs. Already enqueued jobs
        /// can check for cancellation via passed <see cref="CancellationToken"/>.
        /// </summary>
        /// <returns>
        ///     <see langword="true"/> - if cancelled successfully,
        ///     <see langword="false"/> - if it was already cancelled.
        /// </returns>
        bool Cancel();

        /// <summary>
        /// Enqueues the job. Workers will dequeue and execute them.
        /// </summary>
        /// <param name="job">Job to execute (with <see cref="CancellationToken"/> to check whether job queue was cancelled).</param>
        /// <param name="onComplete">Delegate to pass job results once execution finishes.</param>
        /// <returns>
        ///     <see langword="true"/> - if job was enqueued successfully,
        ///     <see langword="false"/> - job queue is cancelled and job was rejected.
        /// </returns>
        bool Post(Action<CancellationToken> job, Action<JobResult> onComplete);

        /// <inheritdoc cref="Post(Action{CancellationToken},Action{JobResult})"/>
        bool Post(Action<CancellationToken> job);

        /// <summary>
        /// Fired synchronously while holding the internal queue lock. This guarantees strict chronological ordering.
        ///
        /// WARNING: Because the global queue lock is held during invocation, any long-running operations or I/O
        /// will globally freeze the queue, blocking all other threads from posting or dequeuing jobs.
        /// Acquiring external locks inside this handler also introduces a high risk of cross-lock deadlocks.
        ///
        /// While re-entrant calls to the queue (e.g., calling <see cref="Post(Action{CancellationToken})"/>)
        /// are technically thread-safe due to lock re-entrancy, using <see cref="ActiveWorkerCountChangedDeferred"/>
        /// is highly recommended for most use cases.
        /// </summary>
        public event EventHandler<int>? ActiveWorkerCountChanged;

        /// <summary>
        /// Fired asynchronously via a background channel pump outside the internal queue lock.
        ///
        /// This is the recommended event for general use. It is completely safe for heavy lifting,
        /// UI thread marshaling, acquiring external locks, and making re-entrant calls back into the <see cref="JobQueue"/>.
        ///
        /// Note: Because delivery is deferred, the reported worker count represents a chronological history
        /// and might slightly lag behind the absolute real-time state under heavy concurrent load.
        /// </summary>
        public event EventHandler<int>? ActiveWorkerCountChangedDeferred;

        /// <summary>
        /// Job queue was cancelled (job queue might be idle or working).
        /// </summary>
        public event EventHandler? Cancelled;

        /// <summary>
        /// Job queue began disposing.
        /// </summary>
        public event EventHandler? Disposing;

        /// <summary>
        /// Job queue disposed.
        /// </summary>
        public event EventHandler? Disposed;

        /// <summary>
        /// Blocks thread and waits (with timeout) for job queue to become idle,
        /// returns immediately if job queue is already idle.
        /// All negative timeouts are treated as <see cref="Timeout.InfiniteTimeSpan"/>.
        /// </summary>
        /// <returns>
        ///     <see langword="true"/> - waited successfully, or job queue was already idle,
        ///     <see langword="false"/> - timeout or cancelled.
        /// </returns>
        Task<bool> WaitForIdleAsync(
            TimeSpan timeout,
            TimeProvider timeProvider,
            CancellationToken ct
        );

        /// <inheritdoc cref="WaitForIdleAsync" />
        bool WaitForIdle(TimeSpan timeout, TimeProvider timeProvider, CancellationToken ct);

        /// <summary>
        /// Blocks thread and waits (with timeout) for job queue to become busy,
        /// returns immediately if job queue is already busy.
        /// All negative timeouts are treated as <see cref="Timeout.InfiniteTimeSpan"/>.
        /// </summary>
        /// <returns>
        ///     <see langword="true"/> - waited successfully, or job queue was already busy,
        ///     <see langword="false"/> - timeout or cancelled.
        /// </returns>
        Task<bool> WaitForBusyAsync(
            TimeSpan timeout,
            TimeProvider timeProvider,
            CancellationToken ct
        );

        /// <inheritdoc cref="WaitForBusyAsync" />
        bool WaitForBusy(TimeSpan timeout, TimeProvider timeProvider, CancellationToken ct);
    }

    /// <inheritdoc cref="IJobQueue"/>
    public interface IJobQueueDisposable : IJobQueue, IDisposable;

    /// <summary>
    /// Defines <see cref="IJobQueue"/> parameters.
    /// </summary>
    public sealed record JobQueueParameters
    {
        public static readonly JobQueueParameters Default = new();

        /// <summary>
        /// How many dedicated workers (threads) to use for parallel job execution?
        /// If the value is less than 1 it will be clamped to 1. Although there's no
        /// upper limit, be aware to respect <see cref="Environment.ProcessorCount"/>,
        /// otherwise thread context switching will create unnecessary overhead.
        /// </summary>
        public int WorkerCount { get; init; } = 1;

        public bool UseBackgroundThreads { get; init; } = true;

        public ThreadPriority ThreadPriority { get; init; } = ThreadPriority.BelowNormal;

        public string? Name { get; init; }
    }

    /// <summary>
    /// <see cref="IJobQueue"/> implementation with dedicated threads as workers.
    /// </summary>
    public sealed class JobQueue : IJobQueueDisposable
    {
        private readonly record struct JobQueueItem(
            Action<CancellationToken> Job,
            Action<JobResult>? OnComplete
        );

        public JobQueueParameters Parameters { get; }
        private readonly CancellationTokenSource _cts;
        private readonly CancellationToken _ct;
        private readonly Lock _lock = new();
        private bool? _disposed;
        private readonly Queue<JobQueueItem> _jobQueue;
        private readonly Queue<int> _idleWorkerIds;
        private readonly AutoResetEvent[] _synchronizers;
        private readonly Thread[] _workers;
        private readonly SyncBox<int> _activeWorkerCountBox; // can be used for internal state tracking (fully synchronized)
        private readonly SyncBoxDeferred<int> _activeWorkerCountBoxDeferred; // only for outside observers, do not use for internal state tracking

        private int _isCancelled; // maintained via interlocked (outside locked context)

        /// <inheritdoc />
        public int WorkerCount => Parameters.WorkerCount;

        /// <inheritdoc />
        public int ActiveWorkerCount => _activeWorkerCountBox.Value;

        /// <inheritdoc />
        public bool IsIdle => ActiveWorkerCount == 0;

        /// <inheritdoc />
        public bool IsBusy => ActiveWorkerCount > 0;

        /// <inheritdoc />
        public bool IsCancelled => _ct.IsCancellationRequested;

        /// <inheritdoc />
        public event EventHandler<int>? ActiveWorkerCountChanged
        {
            add => _activeWorkerCountBox.ValueChanged += value;
            remove => _activeWorkerCountBox.ValueChanged -= value;
        }

        /// <inheritdoc />
        public event EventHandler<int>? ActiveWorkerCountChangedDeferred
        {
            add => _activeWorkerCountBoxDeferred.ValueChanged += value;
            remove => _activeWorkerCountBoxDeferred.ValueChanged -= value;
        }

        /// <inheritdoc />
        public event EventHandler? Cancelled;

        /// <inheritdoc />
        public event EventHandler? Disposing;

        /// <inheritdoc />
        public event EventHandler? Disposed;

        public JobQueue(JobQueueParameters parameters)
        {
            if (parameters.WorkerCount < 1)
            {
                parameters = parameters with { WorkerCount = 1 };
            }

            _cts = new CancellationTokenSource();
            _ct = _cts.Token;
            Parameters = parameters;

            lock (_lock)
            {
                _jobQueue = new Queue<JobQueueItem>();
                _idleWorkerIds = new Queue<int>(parameters.WorkerCount);
                _synchronizers = new AutoResetEvent[parameters.WorkerCount];
                _workers = new Thread[parameters.WorkerCount];

                // we always start with active workers because they need to park themselves to the idle queue
                _activeWorkerCountBox = new SyncBox<int>(parameters.WorkerCount);
                _activeWorkerCountBoxDeferred = new SyncBoxDeferred<int>(parameters.WorkerCount);

                for (var i = 0; i < parameters.WorkerCount; i++)
                {
                    // create thread synchronizer
                    _synchronizers[i] = new AutoResetEvent(false);

                    // create worker thread
                    var workerId = i;
                    var thread = new Thread(() => WorkerThread(workerId))
                    {
                        IsBackground = parameters.UseBackgroundThreads,
                        Name = $"{parameters.Name ?? nameof(JobQueue)}.{nameof(_workers)}[{i}]",
                        Priority = parameters.ThreadPriority,
                    };
                    _workers[i] = thread;
                    thread.Start();
                }
            }

            // wait for workers to initialize and enqueue as idle
            // this should always finish almost instantly
            // because we're in the constructor and nobody had a chance to enqueue jobs
            // thus workers would park to idle queue immediately
            WaitForIdle(Timeout.InfiniteTimeSpan, TimeProvider.System, CancellationToken.None);
        }

        public JobQueue()
            : this(JobQueueParameters.Default) { }

        public void Dispose()
        {
            // ensure cancelled, thus we won't enqueue new jobs
            Cancel();

            lock (_lock)
            {
                if (_disposed is not null)
                {
                    // already disposing (or disposed)
                    return;
                }

                // mark as disposing (initiated but not finished)
                _disposed = false;

                // notify all waiting workers, they should not re-add themselves
                // back to the idle list because we're already disposing
                while (_idleWorkerIds.TryDequeue(out var idleWorkerId))
                {
                    _synchronizers[idleWorkerId].Set();
                }
            }

            Disposing?.Invoke(this, EventArgs.Empty);

            // block and wait for all threads to terminate
            foreach (var thread in _workers)
            {
                thread.Join(); // no timeout, we want all jobs to complete
            }

            // shut down the background channel pump
            _activeWorkerCountBoxDeferred.Dispose();

            lock (_lock)
            {
                // do sanity checks (should never throw)
                if (_workers.Any(t => t.IsAlive))
                {
                    throw new InvalidOperationException("at least one worker thread is alive");
                }
                if (_jobQueue.Count != 0)
                {
                    throw new InvalidOperationException("job queue is not empty");
                }
                if (_idleWorkerIds.Count != 0)
                {
                    // workers are expected to never add themselves back to the idle queue
                    // once global job queue is drained and dispose is triggered
                    throw new InvalidOperationException("not all workers are idle");
                }

                // free memory
                foreach (var synchronizer in _synchronizers)
                {
                    synchronizer.Dispose();
                }
                _cts.Dispose();

                // mark disposing as done
                _disposed = true;
            }

            Disposed?.Invoke(this, EventArgs.Empty);
        }

        private void WorkerThread(int workerId)
        {
            // get synchronizer for this worker
            var synchronizer = _synchronizers[workerId];

            while (true)
            {
                bool dequeued;
                JobQueueItem item;

                lock (_lock)
                {
                    // try to dequeue scheduled jobs
                    dequeued = _jobQueue.TryDequeue(out item);

                    // if no job is found, immediately mark as idle while still holding the lock
                    if (!dequeued)
                    {
                        // if disposing (or disposed) => stop worker thread
                        // (skips re-adding worker to idle worker queue)
                        if (_disposed is not null)
                        {
                            break;
                        }

                        // enqueue this worker back to idle queue
                        _idleWorkerIds.Enqueue(workerId);

                        // notify observers via sync-boxes (we just parked an idle worker)
                        // the synchronous one fires its event immediately inline
                        // the deferred one simply pushes the value to its channel and returns instantly
                        var activeWorkerCount = WorkerCount - _idleWorkerIds.Count;
                        _activeWorkerCountBox.Value = activeWorkerCount;
                        _activeWorkerCountBoxDeferred.Value = activeWorkerCount;
                    }
                }

                if (dequeued)
                {
                    // execute the job (safely outside the lock)
                    Exception? exception = null;
                    try
                    {
                        item.Job.Invoke(_ct);
                    }
                    catch (Exception e)
                    {
                        // catch all exceptions from the job itself
                        exception = e;
                    }

                    var onCompleteCallback = item.OnComplete;
                    if (onCompleteCallback is not null)
                    {
                        // execute completion callback (safely caught so it doesn't kill the worker)
                        try
                        {
                            // note: evaluating cancellation right here is more accurate than doing it
                            // at the start of the job, as the user might have cancelled while the job was running
                            var wasCancelled = _ct.IsCancellationRequested;
                            var jobResult = new JobResult(wasCancelled, exception);
                            onCompleteCallback(jobResult);
                        }
                        catch
                        {
                            // hooks should not throw, we might want to log this,
                            // for now, we swallow it to protect the dedicated thread's lifespan
                        }
                    }
                }
                else
                {
                    // stop and wait for notification from Post() or Dispose()
                    synchronizer.WaitOne();
                }
            }
        }

        /// <inheritdoc />
        public bool Cancel()
        {
            // ensure cancellation only happens once
            if (Interlocked.CompareExchange(ref _isCancelled, 1, 0) != 0)
            {
                return false;
            }

            lock (_lock)
            {
                // synchronize the actual cancellation with Post() and Dispose()
                _cts.Cancel();
            }

            Cancelled?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private bool Post(JobQueueItem item)
        {
            lock (_lock)
            {
                // make sure we can still accept new jobs inside the lock,
                // this guarantees Dispose() and Cancel() cannot happen concurrently with this check
                if (_ct.IsCancellationRequested || _disposed is not null)
                {
                    return false;
                }

                // add job to the queue
                _jobQueue.Enqueue(item);

                // check if there are idle workers
                if (_idleWorkerIds.TryDequeue(out var idleWorkerId))
                {
                    // notify observers via sync-boxes (we just took one idle worker from the queue)
                    // the synchronous one fires its event immediately inline
                    // the deferred one simply pushes the value to its channel and returns instantly
                    var activeWorkerCount = WorkerCount - _idleWorkerIds.Count;
                    _activeWorkerCountBox.Value = activeWorkerCount;
                    _activeWorkerCountBoxDeferred.Value = activeWorkerCount;

                    // notify worker (will start executing enqueued jobs)
                    _synchronizers[idleWorkerId].Set();
                }
            }

            return true;
        }

        /// <inheritdoc />
        public bool Post(Action<CancellationToken> job, Action<JobResult> onComplete) =>
            Post(new JobQueueItem(job, onComplete));

        /// <inheritdoc />
        public bool Post(Action<CancellationToken> job) => Post(new JobQueueItem(job, null));

        /// <inheritdoc />
        public Task<bool> WaitForIdleAsync(
            TimeSpan timeout,
            TimeProvider timeProvider,
            CancellationToken ct
        ) => _activeWorkerCountBox.WaitForAsync(x => x == 0, timeout, timeProvider, ct);

        /// <inheritdoc />
        public bool WaitForIdle(
            TimeSpan timeout,
            TimeProvider timeProvider,
            CancellationToken ct
        ) => _activeWorkerCountBox.WaitFor(x => x == 0, timeout, timeProvider, ct);

        /// <inheritdoc />
        public Task<bool> WaitForBusyAsync(
            TimeSpan timeout,
            TimeProvider timeProvider,
            CancellationToken ct
        ) => _activeWorkerCountBox.WaitForAsync(x => x != 0, timeout, timeProvider, ct);

        /// <inheritdoc />
        public bool WaitForBusy(
            TimeSpan timeout,
            TimeProvider timeProvider,
            CancellationToken ct
        ) => _activeWorkerCountBox.WaitFor(x => x != 0, timeout, timeProvider, ct);
    }

    public static class JobQueueExtensions
    {
        extension(IJobQueue jobQueue)
        {
            /// <inheritdoc cref="IJobQueue.Post(Action{CancellationToken},Action{JobResult})" />
            public bool Post(Action job, Action<JobResult> onComplete) =>
                jobQueue.Post(_ => job(), onComplete);

            /// <inheritdoc cref="IJobQueue.Post(Action{CancellationToken},Action{JobResult})" />
            public bool Post(Action job) => jobQueue.Post(_ => job());

            /// <inheritdoc cref="IJobQueue.Post(Action{CancellationToken},Action{JobResult})" />
            public bool Post<T>(Func<CancellationToken, T> job, Action<JobResult<T>> onComplete)
            {
                var result = default(T);
                return jobQueue.Post(
                    ct => result = job(ct),
                    jobResult =>
                        onComplete(
                            new JobResult<T>(jobResult.Cancelled, jobResult.Exception, result)
                        )
                );
            }

            /// <inheritdoc cref="IJobQueue.Post(Action{CancellationToken},Action{JobResult})" />
            public bool Post<T>(Func<T> job, Action<JobResult<T>> onComplete) =>
                jobQueue.Post(_ => job(), onComplete);

            //

            /// <summary>
            /// Enqueues the job. Workers will dequeue and execute them.
            /// This blocks current thread and waits for job to be executed by a worker
            /// (meaning all previously enqueued jobs will be picked by workers first).
            /// </summary>
            /// <param name="job">Job to execute (with <see cref="CancellationToken"/> to check whether job queue was cancelled).</param>
            /// <param name="result">Job result.</param>
            /// <returns>
            ///     <see langword="true"/> - if job was enqueued and executed successfully,
            ///     <see langword="false"/> - job queue is cancelled and job was rejected.
            /// </returns>
            public bool Send(Action<CancellationToken> job, out JobResult result)
            {
                using var waiter = new AutoResetEvent(false);
                var resultOut = default(JobResult);
                var enqueued = jobQueue.Post(
                    job,
                    r =>
                    {
                        resultOut = r;
                        waiter.Set();
                    }
                );
                if (enqueued)
                {
                    waiter.WaitOne();
                }
                result = resultOut;
                return enqueued;
            }

            /// <inheritdoc cref="Send(IJobQueue,Action{CancellationToken},out JobResult)" />
            public bool Send(Action job, out JobResult result) =>
                jobQueue.Send(_ => job(), out result);

            /// <inheritdoc cref="Send(IJobQueue,Action{CancellationToken},out JobResult)" />
            public bool Send(Action<CancellationToken> job) => jobQueue.Send(job, out _);

            /// <inheritdoc cref="Send(IJobQueue,Action{CancellationToken},out JobResult)" />
            public bool Send(Action job) => jobQueue.Send(job, out _);

            /// <inheritdoc cref="Send(IJobQueue,Action{CancellationToken},out JobResult)" />
            public bool Send<T>(Func<CancellationToken, T> job, out JobResult<T> result)
            {
                var value = default(T);
                var enqueued = jobQueue.Send(
                    ct =>
                    {
                        value = job(ct);
                    },
                    out var resultVanilla
                );
                result = new JobResult<T>(resultVanilla.Cancelled, resultVanilla.Exception, value);
                return enqueued;
            }

            /// <inheritdoc cref="Send(IJobQueue,Action{CancellationToken},out JobResult)" />
            public bool Send<T>(Func<T> job, out JobResult<T> result) =>
                jobQueue.Send(_ => job(), out result);

            //

            /// <inheritdoc cref="JobQueue.WaitForIdleAsync" />
            public Task<bool> WaitForIdleAsync(TimeSpan timeout, TimeProvider timeProvider) =>
                jobQueue.WaitForIdleAsync(timeout, timeProvider, CancellationToken.None);

            /// <inheritdoc cref="JobQueue.WaitForIdleAsync" />
            public Task<bool> WaitForIdleAsync(TimeSpan timeout, CancellationToken ct) =>
                jobQueue.WaitForIdleAsync(timeout, TimeProvider.System, ct);

            /// <inheritdoc cref="JobQueue.WaitForIdleAsync" />
            public Task<bool> WaitForIdleAsync(TimeSpan timeout) =>
                jobQueue.WaitForIdleAsync(timeout, TimeProvider.System, CancellationToken.None);

            /// <inheritdoc cref="JobQueue.WaitForIdleAsync" />
            public Task<bool> WaitForIdleAsync(CancellationToken ct) =>
                jobQueue.WaitForIdleAsync(Timeout.InfiniteTimeSpan, TimeProvider.System, ct);

            /// <summary>
            /// Blocks thread and waits for job queue to become idle.
            /// </summary>
            public Task WaitForIdleAsync() =>
                jobQueue.WaitForIdleAsync(
                    Timeout.InfiniteTimeSpan,
                    TimeProvider.System,
                    CancellationToken.None
                );

            //

            /// <inheritdoc cref="JobQueue.WaitForIdle" />
            public bool WaitForIdle(TimeSpan timeout, TimeProvider timeProvider) =>
                jobQueue.WaitForIdle(timeout, timeProvider, CancellationToken.None);

            /// <inheritdoc cref="JobQueue.WaitForIdle" />
            public bool WaitForIdle(TimeSpan timeout, CancellationToken ct) =>
                jobQueue.WaitForIdle(timeout, TimeProvider.System, ct);

            /// <inheritdoc cref="JobQueue.WaitForIdle" />
            public bool WaitForIdle(TimeSpan timeout) =>
                jobQueue.WaitForIdle(timeout, TimeProvider.System, CancellationToken.None);

            /// <inheritdoc cref="JobQueue.WaitForIdle" />
            public bool WaitForIdle(CancellationToken ct) =>
                jobQueue.WaitForIdle(Timeout.InfiniteTimeSpan, TimeProvider.System, ct);

            /// <summary>
            /// Blocks thread and waits for job queue to become idle.
            /// </summary>
            public void WaitForIdle() =>
                jobQueue.WaitForIdle(
                    Timeout.InfiniteTimeSpan,
                    TimeProvider.System,
                    CancellationToken.None
                );

            //
            /// <inheritdoc cref="JobQueue.WaitForBusyAsync" />
            public Task<bool> WaitForBusyAsync(TimeSpan timeout, TimeProvider timeProvider) =>
                jobQueue.WaitForBusyAsync(timeout, timeProvider, CancellationToken.None);

            /// <inheritdoc cref="JobQueue.WaitForBusyAsync" />
            public Task<bool> WaitForBusyAsync(TimeSpan timeout, CancellationToken ct) =>
                jobQueue.WaitForBusyAsync(timeout, TimeProvider.System, ct);

            /// <inheritdoc cref="JobQueue.WaitForBusyAsync" />
            public Task<bool> WaitForBusyAsync(TimeSpan timeout) =>
                jobQueue.WaitForBusyAsync(timeout, TimeProvider.System, CancellationToken.None);

            /// <inheritdoc cref="JobQueue.WaitForBusyAsync" />
            public Task<bool> WaitForBusyAsync(CancellationToken ct) =>
                jobQueue.WaitForBusyAsync(Timeout.InfiniteTimeSpan, TimeProvider.System, ct);

            /// <summary>
            /// Blocks thread and waits for job queue to become busy.
            /// </summary>
            public Task WaitForBusyAsync() =>
                jobQueue.WaitForBusyAsync(
                    Timeout.InfiniteTimeSpan,
                    TimeProvider.System,
                    CancellationToken.None
                );

            //

            /// <inheritdoc cref="JobQueue.WaitForBusy" />
            public bool WaitForBusy(TimeSpan timeout, TimeProvider timeProvider) =>
                jobQueue.WaitForBusy(timeout, timeProvider, CancellationToken.None);

            /// <inheritdoc cref="JobQueue.WaitForBusy" />
            public bool WaitForBusy(TimeSpan timeout, CancellationToken ct) =>
                jobQueue.WaitForBusy(timeout, TimeProvider.System, ct);

            /// <inheritdoc cref="JobQueue.WaitForBusy" />
            public bool WaitForBusy(TimeSpan timeout) =>
                jobQueue.WaitForBusy(timeout, TimeProvider.System, CancellationToken.None);

            /// <inheritdoc cref="JobQueue.WaitForBusy" />
            public bool WaitForBusy(CancellationToken ct) =>
                jobQueue.WaitForBusy(Timeout.InfiniteTimeSpan, TimeProvider.System, ct);

            /// <summary>
            /// Blocks thread and waits for job queue to become busy.
            /// </summary>
            public void WaitForBusy() =>
                jobQueue.WaitForBusy(
                    Timeout.InfiniteTimeSpan,
                    TimeProvider.System,
                    CancellationToken.None
                );
        }
    }
}
