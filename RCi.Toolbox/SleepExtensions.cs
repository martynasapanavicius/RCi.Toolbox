using System;
using System.Threading;
using System.Threading.Tasks;

namespace RCi.Toolbox
{
    /// <summary>
    /// Provides extension methods for <see cref="TimeSpan"/> to safely and efficiently execute synchronous and asynchronous delays.
    /// </summary>
    public static class SleepExtensions
    {
        extension(TimeSpan delay)
        {
            /// <summary>
            /// Synchronously blocks the current thread for the specified duration.
            /// If delay is zero or negative (excluding <see cref="Timeout.InfiniteTimeSpan"/>), the method returns immediately.
            /// </summary>
            /// <param name="timeProvider">The <see cref="TimeProvider"/> with which to interpret delay.</param>
            /// <remarks>
            /// This method uses <see cref="Thread.Sleep(TimeSpan)"/> under the hood, which completely blocks the calling thread.
            /// </remarks>
            public void Sleep(TimeProvider timeProvider)
            {
                if (delay <= TimeSpan.Zero && delay != Timeout.InfiniteTimeSpan)
                {
                    return;
                }

                // hot path for: use standard Thread.Sleep
                if (ReferenceEquals(timeProvider, TimeProvider.System))
                {
                    Thread.Sleep(delay);
                    return;
                }

                // cold path: block on the provider's task
                Task.Delay(delay, timeProvider).GetAwaiter().GetResult();
            }

            /// <inheritdoc cref="Sleep(TimeSpan, TimeProvider)"/>
            public void Sleep() => delay.Sleep(TimeProvider.System);

            /// <summary>
            /// Synchronously blocks the current thread for the specified duration, or until the provided cancellation token is triggered.
            /// If delay is zero or negative (excluding <see cref="Timeout.InfiniteTimeSpan"/>), the method returns immediately.
            /// </summary>
            /// <param name="timeProvider">The <see cref="TimeProvider"/> with which to interpret delay.</param>
            /// <param name="ct">The <see cref="CancellationToken"/> to observe for early termination of the wait.</param>
            /// <returns>
            /// <see langword="true"/> if the thread successfully slept for the full duration;
            /// <see langword="false"/> if cancellation was requested or the token's source was disposed.
            /// </returns>
            /// <remarks>
            /// This method avoids the overhead of Task allocation by using the token's underlying <see cref="WaitHandle"/>.
            /// </remarks>
            public bool Sleep(TimeProvider timeProvider, CancellationToken ct)
            {
                if (delay <= TimeSpan.Zero && delay != Timeout.InfiniteTimeSpan)
                {
                    return true;
                }
                if (ct.IsCancellationRequested)
                {
                    return false;
                }

                // hot path: use OS-level wait handle
                if (ReferenceEquals(timeProvider, TimeProvider.System))
                {
                    try
                    {
                        return !ct.WaitHandle.WaitOne(delay);
                    }
                    catch (ObjectDisposedException)
                    {
                        return false;
                    }
                }

                // cold path: block on the provider's task
                try
                {
                    Task.Delay(delay, timeProvider, ct).GetAwaiter().GetResult();
                    return true;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }

            /// <inheritdoc cref="Sleep(TimeSpan,TimeProvider,CancellationToken)"/>
            public bool Sleep(CancellationToken ct) => delay.Sleep(TimeProvider.System, ct);

            /// <summary>
            /// Asynchronously creates a delay for the specified duration.
            /// If delay is zero or negative (excluding <see cref="Timeout.InfiniteTimeSpan"/>), the method returns immediately.
            /// </summary>
            /// <param name="timeProvider">The <see cref="TimeProvider"/> with which to interpret delay.</param>
            /// <returns>A task that represents the asynchronous wait.</returns>
            /// <remarks>
            /// This method is non-blocking and uses <see cref="Task.Delay(TimeSpan)"/>.
            /// </remarks>
            public Task SleepAsync(TimeProvider timeProvider)
            {
                if (delay <= TimeSpan.Zero && delay != Timeout.InfiniteTimeSpan)
                {
                    return Task.CompletedTask;
                }
                return Task.Delay(delay, timeProvider);
            }

            /// <inheritdoc cref="SleepAsync(TimeSpan, TimeProvider)"/>
            public Task SleepAsync() => delay.SleepAsync(TimeProvider.System);

            /// <summary>
            /// Asynchronously creates a delay for the specified duration, or until the provided cancellation token is triggered.
            /// If delay is zero or negative (excluding <see cref="Timeout.InfiniteTimeSpan"/>), the method returns immediately.
            /// </summary>
            /// <param name="timeProvider">The <see cref="TimeProvider"/> with which to interpret delay.</param>
            /// <param name="ct">The <see cref="CancellationToken"/> to observe for early termination of the wait.</param>
            /// <returns>
            /// A task that resolves to <see langword="true"/> if the full delay elapsed,
            /// or <see langword="false"/> if cancellation was requested.
            /// </returns>
            /// <remarks>
            /// This method suppresses <see cref="OperationCanceledException"/> and returns a boolean state instead.
            /// It is highly optimized to avoid generating an asynchronous state machine if the wait can be skipped.
            /// </remarks>
            public Task<bool> SleepAsync(TimeProvider timeProvider, CancellationToken ct)
            {
                if (delay <= TimeSpan.Zero && delay != Timeout.InfiniteTimeSpan)
                {
                    return Task.FromResult(true);
                }
                if (ct.IsCancellationRequested)
                {
                    return Task.FromResult(false);
                }
                return ExecuteSleepAsync(delay, timeProvider, ct);

                static async Task<bool> ExecuteSleepAsync(
                    TimeSpan delay,
                    TimeProvider timeProvider,
                    CancellationToken ct
                )
                {
                    try
                    {
                        await Task.Delay(delay, timeProvider, ct).ConfigureAwait(false);
                        return true;
                    }
                    catch (OperationCanceledException)
                    {
                        return false;
                    }
                }
            }

            /// <inheritdoc cref="SleepAsync(TimeSpan,TimeProvider,CancellationToken)"/>
            public Task<bool> SleepAsync(CancellationToken ct) =>
                delay.SleepAsync(TimeProvider.System, ct);
        }
    }
}
