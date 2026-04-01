using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace RCi.Toolbox.Boxes
{
    /// <summary>
    /// Wraps value in a synchronized box with asynchronous value change history stream.
    /// It is guaranteed that whole history will be streamed in order, however if value
    /// is changed rapidly by multiple threads, it could happen that once some observer
    /// receive an event about changed value, actual underlying value might be already
    /// different at that point in time.
    /// </summary>
    public sealed class SyncBoxDeferred<T> : ISyncBox<T>, IDisposable, IAsyncDisposable
    {
        private readonly Lock _lock = new();
        private readonly Func<T, T, bool> _funcEquals;
        private T _value;

        private readonly Channel<T> _eventChannel;
        private Task? _pumpTask;

        /// <summary>
        /// Event handler to receive notifications when value was changed.
        /// Value changes are enqueued and delivered outside locked context,
        /// thus when user receives this notification asynchronously,
        /// the real underlying value might be already changed.
        /// This should be treated as an in-order history stream.
        /// </summary>
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

                    // safely write to the channel inside the lock,
                    // this guarantees the channel receives state changes in strict, chronological order
                    _eventChannel.Writer.TryWrite(value);
                    EnsurePumpingUnsafe();
                }
            }
        }

        public SyncBoxDeferred(T initValue, Func<T, T, bool> funcEquals)
        {
            _value = initValue;
            _funcEquals = funcEquals;

            // initialize an unbounded channel
            // unbounded guarantees TryWrite will never block the lock
            _eventChannel = Channel.CreateUnbounded<T>(
                new UnboundedChannelOptions
                {
                    SingleReader = true, // only our pump will read
                    SingleWriter = false, // multiple threads might update the value
                    AllowSynchronousContinuations = false, // protect against hijacking
                }
            );
        }

        public SyncBoxDeferred(T initValue)
            : this(initValue, EqualityComparer<T>.Default.Equals) { }

        public void Dispose()
        {
            _eventChannel.Writer.TryComplete();

            Task? taskToWait;
            lock (_lock)
            {
                taskToWait = _pumpTask;
            }

            taskToWait?.Wait();
        }

        public async ValueTask DisposeAsync()
        {
            _eventChannel.Writer.TryComplete();

            // asynchronously wait for the pump to finish processing the remaining items
            if (_pumpTask is not null)
            {
                await _pumpTask.ConfigureAwait(false);
            }
        }

        private T GetUnlocked() => _value;

        public void AccessLocked(SyncBoxReadWriteAccessLockedDelegate<T> action)
        {
            lock (_lock)
            {
                action(
                    GetUnlocked,
                    v =>
                    {
                        // re-entrant lock is fine here, though technically
                        // redundant if action is fully synchronous
                        lock (_lock)
                        {
                            if (_funcEquals(_value, v))
                            {
                                return;
                            }
                            _value = v;

                            // strict ordering inside the lock
                            _eventChannel.Writer.TryWrite(v);
                            EnsurePumpingUnsafe();
                        }
                    }
                );
            }
        }

        public TResult AccessLocked<TResult>(
            SyncBoxReadWriteAccessLockedDelegate<T, TResult> action
        )
        {
            TResult result;

            lock (_lock)
            {
                result = action(
                    GetUnlocked,
                    v =>
                    {
                        // re-entrant lock is fine here, though technically
                        // redundant if action is fully synchronous
                        lock (_lock)
                        {
                            if (_funcEquals(_value, v))
                            {
                                return;
                            }

                            _value = v;

                            // strict ordering inside the lock
                            _eventChannel.Writer.TryWrite(v);
                            EnsurePumpingUnsafe();
                        }
                    }
                );
            }

            return result;
        }

        public void AccessLocked(SyncBoxReadOnlyAccessLockedDelegate<T> action)
        {
            lock (_lock)
            {
                action(GetUnlocked);
            }
        }

        public TResult AccessLocked<TResult>(SyncBoxReadOnlyAccessLockedDelegate<T, TResult> action)
        {
            lock (_lock)
            {
                return action(GetUnlocked);
            }
        }

        private void EnsurePumpingUnsafe()
        {
            // NOTE: assumes the caller already holds lock (_lock)
            // If the task is null, or if it somehow crashed and completed, spin up a new one
            if (_pumpTask is null || _pumpTask.IsCompleted)
            {
                _pumpTask = Task.Run(PumpEventsAsync);
            }
        }

        private async Task PumpEventsAsync()
        {
            try
            {
                // ReadAllAsync yields the thread back to the pool when the channel is empty,
                // it only wakes up when new items are written
                await foreach (var value in _eventChannel.Reader.ReadAllAsync())
                {
                    try
                    {
                        // dispatch sequentially outside the lock
                        ValueChanged?.Invoke(this, value);
                    }
                    catch
                    {
                        // ignore issues with customers, keep pumping the queue
                    }
                }
            }
            catch (ChannelClosedException)
            {
                // expected on Dispose
            }
            catch
            {
                // we might want to log catastrophic delegate failures if necessary, but don't crash the pump
            }
        }

        public override string ToString() => $"{Value}";

        public static implicit operator T(SyncBoxDeferred<T> box) => box.Value;
    }
}
