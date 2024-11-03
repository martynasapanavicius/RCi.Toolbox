using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RCi.Toolbox
{
    /// <summary>
    /// Job execution result.
    /// </summary>
    /// <param name="Cancelled">Whether job queue was cancelled after job was already enqueued.</param>
    /// <param name="Exception">Whether exception was thrown during job execution</param>
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
        /// Active worker count. 0 - job queue is idle, otherwise it is actively executing at least one job.
        /// </summary>
        int ActiveWorkerCount { get; }

        /// <summary>
        /// Whether job queue is currently idle (all workers are idle).
        /// </summary>
        bool IsIdle { get; }

        /// <summary>
        /// Whether job queue is currently working (at least one worker is active).
        /// </summary>
        bool IsWorking { get; }

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
        /// Blocks thread and waits (with timeout) for job queue to become idle with
        /// (returns immediately if job queue is already idle).
        /// </summary>
        /// <returns>
        ///     <see langword="true"/> - waited successfully, or job queue was already idle,
        ///     <see langword="false"/> - timeout.
        /// </returns>
        bool WaitForIdle(TimeSpan timeout);

        /// <summary>
        /// Blocks thread and waits (indefinitely) for job queue to become idle
        /// (returns immediately if job queue is already idle).
        /// </summary>
        void WaitForIdle();

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
        /// Active worker count changed.
        /// </summary>
        public event EventHandler<int>? ActiveWorkerCountChanged;

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
    }

    /// <inheritdoc cref="IJobQueue"/>
    public interface IJobQueueDisposable :
        IJobQueue,
        IDisposable;

    /// <summary>
    /// Defines <see cref="IJobQueue"/> parameters.
    /// </summary>
    public sealed record JobQueueParameters
    {
        public static readonly JobQueueParameters Default = new();

        public int WorkerCount { get; init; } = 1;

        public bool UseBackgroundThreads { get; init; } = true;

        public ThreadPriority ThreadPriority { get; init; } = ThreadPriority.BelowNormal;

        public string? Name { get; init; }
    }

    /// <summary>
    /// <see cref="IJobQueue"/> implementation with dedicated threads as workers.
    /// </summary>
    public sealed class JobQueue :
        IJobQueueDisposable
    {
        private readonly record struct JobQueueItem(Action<CancellationToken> Job, Action<JobResult>? OnComplete);

        private readonly CancellationTokenSource _cts;
        private readonly CancellationToken _ct;
        private readonly object _lock = new();
        private bool? _disposed;
        private readonly Queue<JobQueueItem> _jobQueue;
        private readonly Queue<int> _idleWorkerIds;
        private readonly AutoResetEvent[] _synchronizers;
        private readonly Thread[] _workers;
        private int _idleWorkerCount;

        public event EventHandler<int>? ActiveWorkerCountChanged;
        public event EventHandler? Cancelled;
        public event EventHandler? Disposing;
        public event EventHandler? Disposed;

        public JobQueueParameters Parameters { get; }

        public int WorkerCount => Parameters.WorkerCount;

        public int ActiveWorkerCount
        {
            get
            {
                lock (_lock)
                {
                    return WorkerCount - _idleWorkerCount;
                }
            }
        }

        public bool IsIdle => ActiveWorkerCount == 0;

        public bool IsWorking => ActiveWorkerCount > 0;

        public bool IsCancelled => _ct.IsCancellationRequested;

        public JobQueue(JobQueueParameters parameters)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(parameters.WorkerCount, 1, nameof(parameters.WorkerCount));

            _cts = new CancellationTokenSource();
            _ct = _cts.Token;
            Parameters = parameters;

            lock (_lock)
            {
                _jobQueue = new Queue<JobQueueItem>();
                _idleWorkerIds = new Queue<int>(parameters.WorkerCount);
                _synchronizers = new AutoResetEvent[parameters.WorkerCount];
                _workers = new Thread[parameters.WorkerCount];
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

            // wait for workers to warm up and enqueue as idle
            WaitForIdle();
        }

        public JobQueue() :
            this(JobQueueParameters.Default)
        {
        }

        ~JobQueue() => Dispose(false);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool _)
        {
            // ensure cancelled, thus we won't enqueue new jobs
            Cancel();

            lock (_lock)
            {
                if (_disposed.HasValue)
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
                thread.Join();
            }

            lock (_lock)
            {
                // do sanity checks (should never throw)
                if (_workers.Any(t => t.IsAlive))
                {
                    throw new InvalidOperationException("at least one worker thread is alive");
                }
                if (_jobQueue.Any())
                {
                    throw new InvalidOperationException("job queue is not empty");
                }
                if (_idleWorkerIds.Count != 0)
                {
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
                // check if there are available jobs
                bool dequeued;
                JobQueueItem item;
                lock (_lock)
                {
                    // try to dequeue scheduled jobs
                    dequeued = _jobQueue.TryDequeue(out item);
                }

                if (dequeued)
                {
                    // execute the job
                    var exception = default(Exception);
                    try
                    {
                        item.Job.Invoke(_ct);
                    }
                    catch (Exception e)
                    {
                        // catch all exceptions and store them as part of result
                        exception = e;
                    }

                    // notify if needed
                    item.OnComplete?.Invoke(new JobResult(_ct.IsCancellationRequested, exception));
                }
                else
                {
                    int idleWorkerCountBefore, idleWorkerCountAfter;
                    lock (_lock)
                    {
                        // if no job + disposing (or disposed) => stop worker thread
                        // (skips re-adding worker to idle worker queue)
                        if (_disposed.HasValue)
                        {
                            break;
                        }

                        // enqueue this worker back to idle queue
                        idleWorkerCountBefore = _idleWorkerIds.Count;
                        _idleWorkerIds.Enqueue(workerId);
                        _idleWorkerCount = idleWorkerCountAfter = _idleWorkerIds.Count;
                    }

                    // notify observers
                    if (idleWorkerCountBefore != idleWorkerCountAfter)
                    {
                        var activeWorkerCount = WorkerCount - idleWorkerCountAfter;
                        ActiveWorkerCountChanged?.Invoke(this, activeWorkerCount);
                    }

                    // stop and wait for notification
                    synchronizer.WaitOne();
                }
            }
        }

        public bool Cancel()
        {
            if (_ct.IsCancellationRequested)
            {
                return false;
            }
            _cts.Cancel();
            Cancelled?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void WaitForIdle() => WaitForIdle(TimeSpan.Zero);

        public bool WaitForIdle(TimeSpan timeout)
        {
            AutoResetEvent waiter;
            bool subscribed;
            lock (_lock)
            {
                if (IsIdle)
                {
                    // already idle
                    return true;
                }

                waiter = new AutoResetEvent(false);
                subscribed = true;
                ActiveWorkerCountChanged += OnActiveWorkerCountChanged;
            }

            try
            {
                // block thread and wait for signal
                return timeout > TimeSpan.Zero
                    ? waiter.WaitOne(timeout)   // with timeout
                    : waiter.WaitOne();         // indefinitely
            }
            finally
            {
                // unsubscribe no matter we waited successfully or failed due timeout
                lock (_lock)
                {
                    if (subscribed)
                    {
                        ActiveWorkerCountChanged -= OnActiveWorkerCountChanged;
                        subscribed = false;
                    }
                }
                waiter.Dispose();
            }

            void OnActiveWorkerCountChanged(object? sender, int activeWorkerCount)
            {
                if (activeWorkerCount != 0)
                {
                    // not idle yet
                    return;
                }

                // only allow idle event trigger once
                lock (_lock)
                {
                    if (!subscribed)
                    {
                        // was not subscribed
                        // exit without unsubscribing and pulsing waiter
                        return;
                    }
                    subscribed = false;
                    ActiveWorkerCountChanged -= OnActiveWorkerCountChanged;
                }

                // send signal to unblock waiter
                waiter.Set();
            }
        }

        private bool Post(JobQueueItem item)
        {
            // make sure we can still accept new jobs
            if (_ct.IsCancellationRequested)
            {
                return false;
            }

            var idleWorkerCount = default(int?);
            lock (_lock)
            {
                // add to the queue
                _jobQueue.Enqueue(item);

                // check if there are idle workers
                if (_idleWorkerIds.TryDequeue(out var idleWorkerId))
                {
                    // update idle worker count (we just took one from the queue)
                    idleWorkerCount = _idleWorkerCount = _idleWorkerIds.Count;

                    // notify worker (will start executing enqueued jobs)
                    _synchronizers[idleWorkerId].Set();
                }
            }

            // notify observers
            if (idleWorkerCount.HasValue)
            {
                var activeWorkerCount = WorkerCount - idleWorkerCount.Value;
                ActiveWorkerCountChanged?.Invoke(this, activeWorkerCount);
            }

            return true;
        }

        public bool Post(Action<CancellationToken> job, Action<JobResult> onComplete) =>
            Post(new JobQueueItem(job, onComplete));

        public bool Post(Action<CancellationToken> job) =>
            Post(new JobQueueItem(job, default));
    }

    public static class JobQueueExtensions
    {
        public static bool Post(this IJobQueue jobQueue, Action job, Action<JobResult> onComplete) =>
            jobQueue.Post(_ => job(), onComplete);

        public static bool Post(this IJobQueue jobQueue, Action job) =>
            jobQueue.Post(_ => job());

        public static bool Post<T>(this IJobQueue jobQueue, Func<CancellationToken, T> job, Action<JobResult<T>> onComplete)
        {
            var result = default(T);
            return jobQueue.Post
            (
                ct => result = job(ct),
                jobResult => onComplete.Invoke(new JobResult<T>(jobResult.Cancelled, jobResult.Exception, result))
            );
        }

        public static bool Post<T>(this IJobQueue jobQueue, Func<T> job, Action<JobResult<T>> onComplete) =>
            jobQueue.Post(_ => job(), onComplete);

        public static bool Send(this IJobQueue jobQueue, Action<CancellationToken> job, out JobResult result)
        {
            using var waiter = new AutoResetEvent(false);
            var resultOut = default(JobResult);
            var enqueued = jobQueue.Post(job, r =>
            {
                resultOut = r;
                waiter.Set();
            });
            if (enqueued)
            {
                waiter.WaitOne();
            }
            result = resultOut;
            return enqueued;
        }

        public static bool Send(this IJobQueue jobQueue, Action job, out JobResult result) =>
            jobQueue.Send(_ => job(), out result);

        public static bool Send(this IJobQueue jobQueue, Action<CancellationToken> job) =>
            jobQueue.Send(job, out _);

        public static bool Send(this IJobQueue jobQueue, Action job) =>
            jobQueue.Send(job, out _);

        public static bool Send<T>(this IJobQueue jobQueue, Func<CancellationToken, T> job, out JobResult<T> result)
        {
            var value = default(T);
            var enqueued = jobQueue.Send(ct =>
            {
                value = job(ct);
            }, out var resultVanilla);
            result = new JobResult<T>(resultVanilla.Cancelled, resultVanilla.Exception, value);
            return enqueued;
        }

        public static bool Send<T>(this IJobQueue jobQueue, Func<T> job, out JobResult<T> result) =>
            jobQueue.Send(_ => job(), out result);
    }
}
