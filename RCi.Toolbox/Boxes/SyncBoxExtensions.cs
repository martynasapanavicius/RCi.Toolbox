using System;
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
        extension<T>(ISyncBox<T> box)
        {
            public async Task<bool> WaitForAsync(
                SyncBoxWaitForDelegate<T> isDone,
                TimeSpan timeout,
                TimeProvider timeProvider,
                CancellationToken ct
            )
            {
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
            }

            public bool WaitFor(
                SyncBoxWaitForDelegate<T> isDone,
                TimeSpan timeout,
                TimeProvider timeProvider,
                CancellationToken ct
            )
            {
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

                // waiter will be created if needed,
                // to avoid compiler warnings about variables modified in outer scope,
                // let's create a box (array of one element) to wrap actual object
                var waiterBox = new ManualResetEventSlim?[1];
                var waiterLock = new Lock();

                var alreadyDone = box.AccessLocked(get =>
                {
                    var value = get();
                    if (isDone(value))
                    {
                        // is already in a wanted state, nothing else to do
                        return true;
                    }

                    // create waiter
                    waiterBox[0] = new ManualResetEventSlim(false);

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
                    // link it with the user's token so either one can abort the wait
                    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                        ct,
                        timeoutCts.Token
                    );
                    waitToken = linkedCts.Token;
                }

                try
                {
                    // tell the OS to wait "forever", relying entirely on
                    // our waitToken to wake us up if the event isn't fired
                    waiterBox[0]!.Wait(Timeout.InfiniteTimeSpan, waitToken);
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
                        waiterBox[0]!.Dispose();
                        waiterBox[0] = null;
                    }
                }

                void OnValueChanged(object? sender, T value)
                {
                    if (!isDone(value))
                    {
                        // not yet in our wanted state
                        return;
                    }

                    lock (waiterLock)
                    {
                        // (waiter could be either healthy or null, but never disposed,
                        // because we're accessing/modifying it in a synchronized context)
                        var waiter = waiterBox[0];
                        if (waiter is not null)
                        {
                            // unhook
                            box.ValueChanged -= OnValueChanged;

                            // signal waiter
                            waiter.Set();
                        }
                    }
                }
            }

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
                CancellationToken ct
            ) => box.WaitFor(isDone, timeout, TimeProvider.System, CancellationToken.None);

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
