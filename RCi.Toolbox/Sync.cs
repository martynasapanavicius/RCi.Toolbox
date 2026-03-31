using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RCi.Toolbox
{
    public delegate void SyncReadWriteAccessLockedDelegate<T>(Func<T> getter, Action<T> setter);

    public delegate TResult SyncReadWriteAccessLockedDelegate<T, out TResult>(
        Func<T> getter,
        Action<T> setter
    );

    public delegate void SyncReadOnlyAccessLockedDelegate<in T>(Func<T> getter);

    public delegate TResult SyncReadOnlyAccessLockedDelegate<in T, out TResult>(Func<T> getter);

    public interface ISyncReadOnly<out T>
    {
        T Value { get; }
        event EventHandler<T> ValueChanged;

        void AccessLocked(SyncReadOnlyAccessLockedDelegate<T> action);

        TResult AccessLocked<TResult>(SyncReadOnlyAccessLockedDelegate<T, TResult> action);
    }

    public interface ISync<T> : ISyncReadOnly<T>
    {
        new T Value { get; set; }

        void AccessLocked(SyncReadWriteAccessLockedDelegate<T> action);

        TResult AccessLocked<TResult>(SyncReadWriteAccessLockedDelegate<T, TResult> action);
    }

    public sealed class Sync<T> : ISync<T>
    {
        private readonly Lock _lock = new();
        private readonly Func<T, T, bool> _funcEquals;
        private T _value;

        public event EventHandler<T>? ValueChanged;

        public T Value
        {
            get
            {
                lock (_lock)
                {
                    return _value;
                }
            }
            set
            {
                lock (_lock)
                {
                    if (_funcEquals(_value, value))
                    {
                        return;
                    }
                    _value = value;
                }

                // fire safely outside the lock
                ValueChanged?.Invoke(this, value);
            }
        }

        public Sync(T initValue, Func<T, T, bool> funcEquals)
        {
            _value = initValue;
            _funcEquals = funcEquals ?? throw new ArgumentNullException(nameof(funcEquals));
        }

        public Sync(T initValue)
            : this(initValue, EqualityComparer<T>.Default.Equals) { }

        public Sync(Func<T, T, bool> funcEquals)
            : this(default!, funcEquals) { }

        public Sync()
            : this(default(T)!) { }

        private T GetUnlocked() => _value;

        public void AccessLocked(SyncReadWriteAccessLockedDelegate<T> action)
        {
            var changed = false;
            T newValue = default!;

            lock (_lock)
            {
                action(
                    GetUnlocked,
                    v =>
                    {
                        lock (_lock)
                        {
                            if (_funcEquals(_value, v))
                            {
                                return;
                            }
                            _value = v;
                            changed = true;
                            newValue = v;
                        }
                    }
                );
            }

            // fire the event only after the lock is completely released
            if (changed)
            {
                ValueChanged?.Invoke(this, newValue);
            }
        }

        public TResult AccessLocked<TResult>(SyncReadWriteAccessLockedDelegate<T, TResult> action)
        {
            var changed = false;
            T newValue = default!;
            TResult result;

            lock (_lock)
            {
                result = action(
                    GetUnlocked,
                    v =>
                    {
                        lock (_lock)
                        {
                            if (_funcEquals(_value, v))
                            {
                                return;
                            }
                            _value = v;
                            changed = true;
                            newValue = v;
                        }
                    }
                );
            }

            if (changed)
            {
                ValueChanged?.Invoke(this, newValue);
            }

            return result;
        }

        public void AccessLocked(SyncReadOnlyAccessLockedDelegate<T> action)
        {
            lock (_lock)
            {
                action(GetUnlocked);
            }
        }

        public TResult AccessLocked<TResult>(SyncReadOnlyAccessLockedDelegate<T, TResult> action)
        {
            lock (_lock)
            {
                return action(GetUnlocked);
            }
        }

        public override string ToString() => $"{Value}";

        public static implicit operator T(Sync<T> sync) => sync.Value;
    }

    public static class SyncExtensions
    {
        extension<T>(Sync<T> box)
        {
            public async Task<bool> WaitForAsync(
                Func<T, bool> isDone, // callback to know if we need to stop
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
                    box.ValueChanged -= OnValueChanged;
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
                Func<T, bool> isDone, // callback to know if we need to stop
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
                Func<T, bool> isDone, // callback to know if we need to stop
                TimeSpan timeout,
                CancellationToken ct
            ) => box.WaitForAsync(isDone, timeout, TimeProvider.System, ct);

            public Task<bool> WaitForAsync(
                Func<T, bool> isDone, // callback to know if we need to stop
                TimeSpan timeout
            ) => box.WaitForAsync(isDone, timeout, TimeProvider.System, CancellationToken.None);

            public Task<bool> WaitForAsync(
                Func<T, bool> isDone, // callback to know if we need to stop
                CancellationToken ct
            ) => box.WaitForAsync(isDone, Timeout.InfiniteTimeSpan, TimeProvider.System, ct);

            public Task WaitForAsync(
                Func<T, bool> isDone // callback to know if we need to stop
            ) =>
                box.WaitForAsync(
                    isDone,
                    Timeout.InfiniteTimeSpan,
                    TimeProvider.System,
                    CancellationToken.None
                );

            public bool WaitFor(
                Func<T, bool> isDone, // callback to know if we need to stop
                TimeSpan timeout,
                CancellationToken ct
            ) => box.WaitFor(isDone, timeout, TimeProvider.System, CancellationToken.None);

            public bool WaitFor(
                Func<T, bool> isDone, // callback to know if we need to stop
                TimeSpan timeout
            ) => box.WaitFor(isDone, timeout, TimeProvider.System, CancellationToken.None);

            public bool WaitFor(
                Func<T, bool> isDone, // callback to know if we need to stop
                CancellationToken ct
            ) => box.WaitFor(isDone, Timeout.InfiniteTimeSpan, TimeProvider.System, ct);

            public void WaitFor(
                Func<T, bool> isDone // callback to know if we need to stop
            ) =>
                box.WaitFor(
                    isDone,
                    Timeout.InfiniteTimeSpan,
                    TimeProvider.System,
                    CancellationToken.None
                );
        }
    }
}
