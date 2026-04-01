using System;

namespace RCi.Toolbox.Boxes
{
    public delegate void SyncBoxReadWriteAccessLockedDelegate<T>(Func<T> getter, Action<T> setter);

    public delegate TResult SyncBoxReadWriteAccessLockedDelegate<T, out TResult>(
        Func<T> getter,
        Action<T> setter
    );

    public delegate void SyncBoxReadOnlyAccessLockedDelegate<in T>(Func<T> getter);

    public delegate TResult SyncBoxReadOnlyAccessLockedDelegate<in T, out TResult>(Func<T> getter);

    public interface ISyncBoxReadOnly<out T>
    {
        T Value { get; }

        event EventHandler<T> ValueChanged;

        void AccessLocked(SyncBoxReadOnlyAccessLockedDelegate<T> action);

        TResult AccessLocked<TResult>(SyncBoxReadOnlyAccessLockedDelegate<T, TResult> action);
    }

    public interface ISyncBox<T> : ISyncBoxReadOnly<T>
    {
        new T Value { get; set; }

        void AccessLocked(SyncBoxReadWriteAccessLockedDelegate<T> action);

        TResult AccessLocked<TResult>(SyncBoxReadWriteAccessLockedDelegate<T, TResult> action);
    }
}
