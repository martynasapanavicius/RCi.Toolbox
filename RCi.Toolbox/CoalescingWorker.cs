using System;
using System.Threading;
using System.Threading.Tasks;
using RCi.Toolbox.Boxes;

namespace RCi.Toolbox
{
    /// <summary>
    /// Job queue with 1 worker. Only executes last schedule (not for each schedule).
    /// If worker is busy and new schedules arrive, job queue marks that only 1 new job
    /// needs to be executed afterwards (not job for each new schedule)
    /// </summary>
    public interface ICoalescingWorker
    {
        /// <summary>
        /// Triggers a job execution using coalescing logic.
        /// If a job is already pending for the next cycle, this request is merged into it.
        /// </summary>
        /// <param name="wasCoalesced">
        /// When this method returns <see langword="true"/>:
        /// <list type="bullet">
        ///     <item>
        ///         <term><see langword="true"/></term>
        ///         <description>The request was merged into an already-pending job. No new task was added to the queue.</description>
        ///     </item>
        ///     <item>
        ///         <term><see langword="false"/></term>
        ///         <description>This request successfully queued a new execution (either starting it immediately or scheduling it for after the current run).</description>
        ///     </item>
        /// </list>
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the request was accepted and a job is guaranteed to run;
        /// <see langword="false"/> if the request was rejected because the worker is cancelled or disposed.
        /// </returns>
        bool Schedule(out bool wasCoalesced);

        /// <inheritdoc cref="Schedule(out bool)" />
        bool Schedule(TimeSpan waitBeforeScheduling, out bool wasCoalesced);

        /// <inheritdoc cref="Schedule(out bool)" />
        bool Schedule(TimeSpan waitBeforeScheduling, CancellationToken ct, out bool wasCoalesced);

        /// <inheritdoc cref="Schedule(out bool)" />
        bool Schedule();

        /// <inheritdoc cref="Schedule(out bool)" />
        bool Schedule(TimeSpan waitBeforeScheduling);

        /// <inheritdoc cref="Schedule(out bool)" />
        bool Schedule(TimeSpan waitBeforeScheduling, CancellationToken ct);

        //

        /// <summary>
        /// Asynchronously triggers a job execution. See <see cref="Schedule(out bool)"/> for coalescing details.
        /// </summary>
        /// <returns>
        /// A tuple where <c>Success</c> indicates if the request was accepted,
        /// and <c>WasCoalesced</c> indicates if it was merged into a pending job.
        /// </returns>
        Task<(bool Success, bool WasCoalesced)> ScheduleAsync();

        /// <inheritdoc cref="Schedule(out bool)" />
        Task<(bool Success, bool WasCoalesced)> ScheduleAsync(TimeSpan waitBeforeScheduling);

        /// <inheritdoc cref="Schedule(out bool)" />
        Task<(bool Success, bool WasCoalesced)> ScheduleAsync(
            TimeSpan waitBeforeScheduling,
            CancellationToken ct
        );

        bool WaitForBusy(TimeSpan timeout, CancellationToken ct);
        bool WaitForBusy(TimeSpan timeout);
        bool WaitForBusy(CancellationToken ct);
        void WaitForBusy();
        Task<bool> WaitForBusyAsync(TimeSpan timeout, CancellationToken ct);
        Task<bool> WaitForBusyAsync(TimeSpan timeout);
        Task<bool> WaitForBusyAsync(CancellationToken ct);
        Task WaitForBusyAsync();

        bool WaitForIdle(TimeSpan timeout, CancellationToken ct);
        bool WaitForIdle(TimeSpan timeout);
        bool WaitForIdle(CancellationToken ct);
        void WaitForIdle();
        Task<bool> WaitForIdleAsync(TimeSpan timeout, CancellationToken ct);
        Task<bool> WaitForIdleAsync(TimeSpan timeout);
        Task<bool> WaitForIdleAsync(CancellationToken ct);
        Task WaitForIdleAsync();

        event EventHandler<bool>? IsBusyChanged;
    }

    public interface ICoalescingWorkerDisposable : ICoalescingWorker, IDisposable;

    public sealed record CoalescingWorkerParameters
    {
        public static readonly CoalescingWorkerParameters Default = new();

        public string? Name { get; init; }
        public bool UseBackgroundThread { get; init; } = true;
        public ThreadPriority ThreadPriority { get; init; } = ThreadPriority.BelowNormal;
        public Action<Exception>? OnJobExceptionCallback { get; init; }
    }

    public sealed class CoalescingWorker : ICoalescingWorkerDisposable
    {
        private readonly record struct State(bool IsScheduled, bool IsExecuting);

        private readonly AtomicGate _disposed = new();
        private readonly CancellationTokenSource _cts;
        private readonly CancellationToken _ct;
        private readonly Action _job;
        private readonly Action<Exception>? _onJobExceptionCallback;
        private readonly SyncBox<State> _stateBox; // synchronized state, internals can rely on it
        private readonly JobQueue _jobQueue;

        private readonly SyncBox<bool> _isBusyBox = new(false); // this box is only for observers
        public event EventHandler<bool>? IsBusyChanged
        {
            add => _isBusyBox.ValueChanged += value;
            remove => _isBusyBox.ValueChanged -= value;
        }

        public CoalescingWorker(CoalescingWorkerParameters parameters, Action job)
        {
            _cts = new CancellationTokenSource();
            _ct = _cts.Token;
            _job = job;
            _onJobExceptionCallback = parameters.OnJobExceptionCallback;

            _stateBox = new SyncBox<State>(new State(false, false));
            _stateBox.ValueChanged += StateBoxOnValueChanged;

            _jobQueue = new JobQueue(
                new JobQueueParameters
                {
                    WorkerCount = 1,
                    UseBackgroundThreads = parameters.UseBackgroundThread,
                    ThreadPriority = parameters.ThreadPriority,
                    Name = parameters.Name ?? nameof(CoalescingWorker),
                }
            );
        }

        public CoalescingWorker(Action job)
            : this(CoalescingWorkerParameters.Default, job) { }

        public void Dispose() => _disposed.TryExecute(DisposeWrapped);

        private void DisposeWrapped()
        {
            _cts.Cancel();

            // drain the queue and wait for last execution to finish
            // NOTE: we're not using timeout or cancellation token,
            // it's up to user to make sure no deadlocks occur during Dispose()
            WaitForIdle();

            // unhook from internal state changes
            _stateBox.ValueChanged -= StateBoxOnValueChanged;

            // job queue is drained, thus we can safely dispose it
            _jobQueue.Dispose();

            _cts.Dispose();
        }

        // private api

        private void StateBoxOnValueChanged(object? sender, State state)
        {
            _isBusyBox.Value = state.IsScheduled || state.IsExecuting;
        }

        private void Job()
        {
            var execute = false;
            _stateBox.AccessLocked(
                (g, s) =>
                {
                    var currentState = g();
                    if (currentState.IsScheduled)
                    {
                        execute = true;
                        s(new State(false, true));
                    }
                }
            );
            if (!execute)
            {
                return;
            }

            try
            {
                _job();
            }
            catch (Exception e)
            {
                // jobs should be wrapped by user, otherwise our
                // state's consistency cannot be assured,
                // report error to the user
                _onJobExceptionCallback?.Invoke(e);
            }
            finally
            {
                _stateBox.AccessLocked(
                    (g, s) =>
                    {
                        // only set idle when no more jobs were
                        // scheduled during this job's execution
                        if (!g().IsScheduled)
                        {
                            s(new State(false, false));
                        }
                    }
                );
            }
        }

        private bool ScheduleRaw(CancellationToken ct, out bool wasCoalesced)
        {
            (var success, wasCoalesced) = _stateBox.AccessLocked(
                (g, s) =>
                {
                    if (ct.IsCancellationRequested)
                    {
                        return (false, false);
                    }

                    // get current state
                    var stateBefore = g();

                    // only post if we're the first to schedule (no matter if job is currently executing)
                    if (stateBefore.IsScheduled)
                    {
                        // already scheduled by previous invocations
                        return (true, true);
                    }

                    // update state
                    s(stateBefore with { IsScheduled = true });

                    // schedule on a vendor
                    var success = _jobQueue.Post(Job);
                    if (!success)
                    {
                        // this shouldn't ever happen
                        // because job queue rejects jobs only when it is cancelled
                        // and this happens only when it is disposed (or during dispose)
                        // however we ourselves discard new schedules when cancelled
                        return (false, false);
                    }

                    // give feedback that we just scheduled a new job
                    // (no matter if worker is busy executing previous job)
                    return (true, false);
                }
            );
            return success;
        }

        private bool ScheduleInternal(out bool wasCoalesced) => ScheduleRaw(_ct, out wasCoalesced);

        private bool ScheduleInternalWithCancellationToken(
            CancellationToken ct,
            out bool wasCoalesced
        )
        {
            using var ctsMerged = CancellationTokenSource.CreateLinkedTokenSource(_ct, ct);
            var ctMerged = ctsMerged.Token;
            return ScheduleRaw(ctMerged, out wasCoalesced);
        }

        private bool ScheduleInternal(TimeSpan waitBeforeScheduling, out bool wasCoalesced)
        {
            if (waitBeforeScheduling <= TimeSpan.Zero)
            {
                return ScheduleInternal(out wasCoalesced);
            }

            if (!waitBeforeScheduling.Sleep(_ct))
            {
                wasCoalesced = false;
                return false;
            }

            return ScheduleRaw(_ct, out wasCoalesced);
        }

        private bool ScheduleInternalWithCancellationToken(
            TimeSpan waitBeforeScheduling,
            CancellationToken ct,
            out bool wasCoalesced
        )
        {
            if (waitBeforeScheduling <= TimeSpan.Zero)
            {
                return ScheduleInternalWithCancellationToken(ct, out wasCoalesced);
            }

            using var ctsMerged = CancellationTokenSource.CreateLinkedTokenSource(_ct, ct);
            var ctMerged = ctsMerged.Token;

            if (!waitBeforeScheduling.Sleep(ctMerged))
            {
                wasCoalesced = false;
                return false;
            }

            return ScheduleRaw(ctMerged, out wasCoalesced);
        }

        private Task<(bool Success, bool WasCoalesced)> ScheduleInternalAsync()
        {
            var success = ScheduleInternal(out var wasCoalesced);
            return Task.FromResult((success, wasCoalesced));
        }

        private async Task<(bool Success, bool WasCoalesced)> ScheduleInternalAsync(
            TimeSpan waitBeforeScheduling
        )
        {
            bool success;
            bool wasCoalesced;

            if (waitBeforeScheduling <= TimeSpan.Zero)
            {
                success = ScheduleInternal(out wasCoalesced);
                return (success, wasCoalesced);
            }

            if (!await waitBeforeScheduling.SleepAsync(_ct))
            {
                return (false, false);
            }

            success = ScheduleRaw(_ct, out wasCoalesced);
            return (success, wasCoalesced);
        }

        private async Task<(
            bool Success,
            bool WasCoalesced
        )> ScheduleInternalWithCancellationTokenAsync(
            TimeSpan waitBeforeScheduling,
            CancellationToken ct
        )
        {
            bool success;
            bool wasCoalesced;

            if (waitBeforeScheduling <= TimeSpan.Zero)
            {
                success = ScheduleInternalWithCancellationToken(ct, out wasCoalesced);
                return (success, wasCoalesced);
            }

            using var ctsMerged = CancellationTokenSource.CreateLinkedTokenSource(_ct, ct);
            var ctMerged = ctsMerged.Token;

            if (!await waitBeforeScheduling.SleepAsync(ctMerged))
            {
                return (false, false);
            }

            success = ScheduleRaw(ctMerged, out wasCoalesced);
            return (success, wasCoalesced);
        }

        // public api

        public bool Schedule(out bool wasCoalesced) => ScheduleInternal(out wasCoalesced);

        public bool Schedule(TimeSpan waitBeforeScheduling, out bool wasCoalesced) =>
            ScheduleInternal(waitBeforeScheduling, out wasCoalesced);

        public bool Schedule(
            TimeSpan waitBeforeScheduling,
            CancellationToken ct,
            out bool wasCoalesced
        ) => ScheduleInternalWithCancellationToken(waitBeforeScheduling, ct, out wasCoalesced);

        public bool Schedule() => ScheduleInternal(out _);

        public bool Schedule(TimeSpan waitBeforeScheduling) =>
            ScheduleInternal(waitBeforeScheduling, out _);

        public bool Schedule(TimeSpan waitBeforeScheduling, CancellationToken ct) =>
            ScheduleInternalWithCancellationToken(waitBeforeScheduling, ct, out _);

        public Task<(bool Success, bool WasCoalesced)> ScheduleAsync() => ScheduleInternalAsync();

        public Task<(bool Success, bool WasCoalesced)> ScheduleAsync(
            TimeSpan waitBeforeScheduling
        ) => ScheduleInternalAsync(waitBeforeScheduling);

        public Task<(bool Success, bool WasCoalesced)> ScheduleAsync(
            TimeSpan waitBeforeScheduling,
            CancellationToken ct
        ) => ScheduleInternalWithCancellationTokenAsync(waitBeforeScheduling, ct);

        //

        private Task<bool> WaitForAsync(bool isBusy, TimeSpan timeout, CancellationToken ct) =>
            _stateBox.WaitForAsync(x => (x.IsScheduled || x.IsExecuting) == isBusy, timeout, ct);

        private bool WaitFor(bool isBusy, TimeSpan timeout, CancellationToken ct) =>
            _stateBox.WaitFor(x => (x.IsScheduled || x.IsExecuting) == isBusy, timeout, ct);

        //

        public Task<bool> WaitForIdleAsync(TimeSpan timeout, CancellationToken ct) =>
            WaitForAsync(false, timeout, ct);

        public Task<bool> WaitForIdleAsync(TimeSpan timeout) =>
            WaitForAsync(false, timeout, CancellationToken.None);

        public Task<bool> WaitForIdleAsync(CancellationToken ct) =>
            WaitForAsync(false, Timeout.InfiniteTimeSpan, ct);

        public Task WaitForIdleAsync() =>
            WaitForAsync(false, Timeout.InfiniteTimeSpan, CancellationToken.None);

        public bool WaitForIdle(TimeSpan timeout, CancellationToken ct) =>
            WaitFor(false, timeout, ct);

        public bool WaitForIdle(TimeSpan timeout) =>
            WaitFor(false, timeout, CancellationToken.None);

        public bool WaitForIdle(CancellationToken ct) =>
            WaitFor(false, Timeout.InfiniteTimeSpan, ct);

        public void WaitForIdle() =>
            WaitFor(false, Timeout.InfiniteTimeSpan, CancellationToken.None);

        //

        public Task<bool> WaitForBusyAsync(TimeSpan timeout, CancellationToken ct) =>
            WaitForAsync(true, timeout, ct);

        public Task<bool> WaitForBusyAsync(TimeSpan timeout) =>
            WaitForAsync(true, timeout, CancellationToken.None);

        public Task<bool> WaitForBusyAsync(CancellationToken ct) =>
            WaitForAsync(true, Timeout.InfiniteTimeSpan, ct);

        public Task WaitForBusyAsync() =>
            WaitForAsync(true, Timeout.InfiniteTimeSpan, CancellationToken.None);

        public bool WaitForBusy(TimeSpan timeout, CancellationToken ct) =>
            WaitFor(true, timeout, ct);

        public bool WaitForBusy(TimeSpan timeout) => WaitFor(true, timeout, CancellationToken.None);

        public bool WaitForBusy(CancellationToken ct) =>
            WaitFor(true, Timeout.InfiniteTimeSpan, ct);

        public void WaitForBusy() =>
            WaitFor(true, Timeout.InfiniteTimeSpan, CancellationToken.None);
    }
}
