using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace RCi.Toolbox.Boxes
{
    /// <summary>
    /// User is given a value to evaluate whether this is their desired value.
    /// User should return <see langword="true"/> when desired value is found (this finishes waiting),
    /// or <see langword="false"/> to keep waiting for the desired value.
    /// </summary>
    public delegate bool SyncBoxWaitForDelegate<in T>(T value);

    public static class SyncBoxExtensions
    {
        extension<T>(ISyncBoxReadOnly<T> box)
        {
            public async Task<bool> WaitForAsync(
                SyncBoxWaitForDelegate<T> isDone,
                TimeSpan timeout,
                TimeProvider timeProvider,
                CancellationToken ct
            )
            {
                ArgumentNullException.ThrowIfNull(box);
                ArgumentNullException.ThrowIfNull(isDone);
                ArgumentNullException.ThrowIfNull(timeProvider);

                // special handling for no timeout
                if (timeout == TimeSpan.Zero)
                {
                    // checking for cancellation is arbitrary, we could skip this
                    if (ct.IsCancellationRequested)
                    {
                        return false;
                    }

                    // when we have no timeout, let's just look at the actual value
                    return isDone(box.Value);
                }

                // patch infinite timeout
                if (timeout < TimeSpan.Zero)
                {
                    timeout = Timeout.InfiniteTimeSpan;
                }

                // CRITICAL: force continuations to run asynchronously!
                // without this, the thread that sets the value (firing the event)
                // could be hijacked to execute whatever awaits this method!
                var tcs = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                var alreadyDone = box.AccessLocked(get =>
                {
                    var value = get();
                    if (isDone(value))
                    {
                        // is already in a wanted state, nothing else to do
                        return true;
                    }

                    // hook
                    box.ValueChanged += OnValueChanged;
                    return false;
                });

                if (alreadyDone)
                {
                    // checking for cancellation is arbitrary, we could skip this
                    if (ct.IsCancellationRequested)
                    {
                        return false;
                    }

                    // is already in our wanted state, no memory allocated, we can exit here
                    return true;
                }

                try
                {
                    // modern .NET provides native WaitAsync for timeouts and cancellation
                    return await tcs
                        .Task.WaitAsync(timeout, timeProvider, ct)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // WaitAsync throws TimeoutException if the TimeSpan expires
                    return false;
                }
                catch (OperationCanceledException)
                {
                    // WaitAsync throws OperationCanceledException if the token is canceled
                    return false;
                }
                finally
                {
                    // delegate removal is inherently thread-safe in C#,
                    // whether we succeeded, timed out, or were canceled, we clean up
                    box.ValueChanged -= OnValueChanged; // this is a noop if it was already unhooked in the OnValueChanged callback
                }

                void OnValueChanged(object? sender, T value)
                {
                    try
                    {
                        if (!isDone(value))
                        {
                            // not yet in our wanted state
                            return;
                        }

                        // unhook early to prevent unnecessary subsequent checks if the value keeps changing rapidly
                        box.ValueChanged -= OnValueChanged;

                        // TrySetResult safely completes the task
                        // if the task already timed out or was canceled, this simply returns false and does nothing
                        tcs.TrySetResult(true);
                    }
                    catch (Exception e)
                    {
                        // if the predicate throws, unhook and fault the task immediately rather than causing
                        // the waiter to hang until timeout or crashing the producer thread
                        box.ValueChanged -= OnValueChanged;
                        tcs.TrySetException(e);
                    }
                }
            }

            public bool WaitFor(
                SyncBoxWaitForDelegate<T> isDone,
                TimeSpan timeout,
                TimeProvider timeProvider,
                CancellationToken ct
            )
            {
                ArgumentNullException.ThrowIfNull(box);
                ArgumentNullException.ThrowIfNull(isDone);
                ArgumentNullException.ThrowIfNull(timeProvider);

                // special handling for no timeout
                if (timeout == TimeSpan.Zero)
                {
                    // checking for cancellation is arbitrary, we could skip this
                    if (ct.IsCancellationRequested)
                    {
                        return false;
                    }

                    // when we have no timeout, let's just look at the actual value
                    return isDone(box.Value);
                }

                // patch infinite timeout
                if (timeout < TimeSpan.Zero)
                {
                    timeout = Timeout.InfiniteTimeSpan;
                }

                ManualResetEventSlim? waiter = null;
                var waiterLock = new Lock();
                Exception? predicateException = null;

                var alreadyDone = box.AccessLocked(get =>
                {
                    var value = get();
                    if (isDone(value))
                    {
                        // is already in a wanted state, nothing else to do
                        return true;
                    }

                    // create waiter
                    waiter = new ManualResetEventSlim(false);

                    // hook
                    box.ValueChanged += OnValueChanged;

                    return false;
                });
                if (alreadyDone)
                {
                    // checking for cancellation is arbitrary, we could skip this
                    if (ct.IsCancellationRequested)
                    {
                        return false;
                    }

                    // is already in our wanted state, no memory allocated, we can exit here
                    return true;
                }

                // at this point waiter is allocated

                // let's plug time provider if possible
                CancellationTokenSource? timeoutCts = null;
                CancellationTokenSource? linkedCts = null;
                var waitToken = ct;
                if (timeout != Timeout.InfiniteTimeSpan)
                {
                    // create a token that cancels when the TimeProvider reaches the timeout
                    timeoutCts = new CancellationTokenSource(timeout, timeProvider);
                    if (ct.CanBeCanceled)
                    {
                        // link it with the user's token so either one can abort the wait
                        linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                            ct,
                            timeoutCts.Token
                        );
                        waitToken = linkedCts.Token;
                    }
                    else
                    {
                        waitToken = timeoutCts.Token;
                    }
                }

                try
                {
                    // tell the OS to wait "forever", relying entirely on
                    // our waitToken to wake us up if the event isn't fired
                    waiter!.Wait(Timeout.InfiniteTimeSpan, waitToken);

                    // if the predicate threw an exception, rethrow it to the waiting caller
                    if (predicateException is not null)
                    {
                        ExceptionDispatchInfo.Capture(predicateException).Throw();
                    }

                    return true;
                }
                catch (OperationCanceledException)
                {
                    // throws if either when the user cancelled 'ct' or our 'timeoutCts' ran out of time
                    return false;
                }
                finally
                {
                    // no matter if we waited successfully, ensure we do cleanup
                    linkedCts?.Dispose();
                    timeoutCts?.Dispose();

                    // dedicated lock to prevent race conditions with OnValueChanged without freezing the main Sync<T> box
                    lock (waiterLock)
                    {
                        box.ValueChanged -= OnValueChanged;
                        waiter!.Dispose();
                        waiter = null;
                    }
                }

                void OnValueChanged(object? sender, T value)
                {
                    Exception? ex = null;
                    var done = false;

                    try
                    {
                        done = isDone(value);
                    }
                    catch (Exception e)
                    {
                        ex = e;
                    }

                    // fast path: if not done and no exception, exit immediately without touching the lock!
                    if (!done && ex is null)
                    {
                        return;
                    }

                    lock (waiterLock)
                    {
                        if (waiter is not null)
                        {
                            predicateException = ex;
                            box.ValueChanged -= OnValueChanged;
                            waiter.Set();
                        }
                    }
                }
            }

            public Task<bool> WaitForAsync(
                SyncBoxWaitForDelegate<T> isDone,
                TimeSpan timeout,
                TimeProvider timeProvider
            ) => box.WaitForAsync(isDone, timeout, timeProvider, CancellationToken.None);

            public Task<bool> WaitForAsync(
                SyncBoxWaitForDelegate<T> isDone,
                TimeSpan timeout,
                CancellationToken ct
            ) => box.WaitForAsync(isDone, timeout, TimeProvider.System, ct);

            public Task<bool> WaitForAsync(SyncBoxWaitForDelegate<T> isDone, TimeSpan timeout) =>
                box.WaitForAsync(isDone, timeout, TimeProvider.System, CancellationToken.None);

            public Task<bool> WaitForAsync(
                SyncBoxWaitForDelegate<T> isDone,
                CancellationToken ct
            ) => box.WaitForAsync(isDone, Timeout.InfiniteTimeSpan, TimeProvider.System, ct);

            public Task WaitForAsync(SyncBoxWaitForDelegate<T> isDone) =>
                box.WaitForAsync(
                    isDone,
                    Timeout.InfiniteTimeSpan,
                    TimeProvider.System,
                    CancellationToken.None
                );

            public bool WaitFor(
                SyncBoxWaitForDelegate<T> isDone,
                TimeSpan timeout,
                TimeProvider timeProvider
            ) => box.WaitFor(isDone, timeout, timeProvider, CancellationToken.None);

            public bool WaitFor(
                SyncBoxWaitForDelegate<T> isDone,
                TimeSpan timeout,
                CancellationToken ct
            ) => box.WaitFor(isDone, timeout, TimeProvider.System, ct);

            public bool WaitFor(SyncBoxWaitForDelegate<T> isDone, TimeSpan timeout) =>
                box.WaitFor(isDone, timeout, TimeProvider.System, CancellationToken.None);

            public bool WaitFor(SyncBoxWaitForDelegate<T> isDone, CancellationToken ct) =>
                box.WaitFor(isDone, Timeout.InfiniteTimeSpan, TimeProvider.System, ct);

            public void WaitFor(SyncBoxWaitForDelegate<T> isDone) =>
                box.WaitFor(
                    isDone,
                    Timeout.InfiniteTimeSpan,
                    TimeProvider.System,
                    CancellationToken.None
                );
        }
    }
}
