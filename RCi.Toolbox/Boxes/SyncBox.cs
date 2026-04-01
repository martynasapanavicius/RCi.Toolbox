using System;
using System.Collections.Generic;
using System.Threading;

namespace RCi.Toolbox.Boxes
{
    /// <summary>
    /// Wraps value in a fully synchronous box.
    /// Events are fired within locked context, thus when observers receive them,
    /// the underlying value is what the event says.
    /// </summary>
    public sealed class SyncBox<T> : ISyncBox<T>
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

                    // NOTE: notify observers within locked context
                    // if users cause deadlocks, stack-overflow or similar, so be it
                    // we want to maintain synchronization between notifications and current state
                    ValueChanged?.Invoke(this, value);
                }
            }
        }

        public SyncBox(T initValue, Func<T, T, bool> funcEquals)
        {
            _value = initValue;
            _funcEquals = funcEquals;
        }

        public SyncBox(T initValue)
            : this(initValue, EqualityComparer<T>.Default.Equals) { }

        private T GetUnlocked() => _value;

        public void AccessLocked(SyncBoxReadWriteAccessLockedDelegate<T> action)
        {
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

                            // NOTE: notify observers within locked context
                            // if users cause deadlocks, stack-overflow or similar, so be it
                            // we want to maintain synchronization between notifications and current state
                            ValueChanged?.Invoke(this, v);
                        }
                    }
                );
            }
        }

        public TResult AccessLocked<TResult>(
            SyncBoxReadWriteAccessLockedDelegate<T, TResult> action
        )
        {
            lock (_lock)
            {
                return action(
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

                            // NOTE: notify observers within locked context
                            // if users cause deadlocks, stack-overflow or similar, so be it
                            // we want to maintain synchronization between notifications and current state
                            ValueChanged?.Invoke(this, v);
                        }
                    }
                );
            }
        }

        public void AccessLocked(SyncBoxReadOnlyAccessLockedDelegate<T> action)
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

        public static implicit operator T(SyncBox<T> box) => box.Value;
    }
}
