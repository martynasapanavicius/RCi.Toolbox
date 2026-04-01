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
            /// <remarks>
            /// This method uses <see cref="Thread.Sleep(TimeSpan)"/> under the hood, which completely blocks the calling thread.
            /// </remarks>
            public void Sleep()
            {
                if (delay <= TimeSpan.Zero && delay != Timeout.InfiniteTimeSpan)
                {
                    return;
                }
                Thread.Sleep(delay);
            }

            /// <summary>
            /// Synchronously blocks the current thread for the specified duration, or until the provided cancellation token is triggered.
            /// If delay is zero or negative (excluding <see cref="Timeout.InfiniteTimeSpan"/>), the method returns immediately.
            /// </summary>
            /// <param name="ct">The <see cref="CancellationToken"/> to observe for early termination of the wait.</param>
            /// <returns>
            /// <see langword="true"/> if the thread successfully slept for the full duration;
            /// <see langword="false"/> if cancellation was requested or the token's source was disposed.
            /// </returns>
            /// <remarks>
            /// This method avoids the overhead of Task allocation by using the token's underlying <see cref="WaitHandle"/>.
            /// </remarks>
            public bool Sleep(CancellationToken ct)
            {
                if (delay <= TimeSpan.Zero && delay != Timeout.InfiniteTimeSpan)
                {
                    return true;
                }
                if (ct.IsCancellationRequested)
                {
                    return false;
                }
                try
                {
                    return !ct.WaitHandle.WaitOne(delay);
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
            }

            /// <summary>
            /// Asynchronously creates a delay for the specified duration.
            /// If delay is zero or negative (excluding <see cref="Timeout.InfiniteTimeSpan"/>), the method returns immediately.
            /// </summary>
            /// <returns>A task that represents the asynchronous wait.</returns>
            /// <remarks>
            /// This method is non-blocking and uses <see cref="Task.Delay(TimeSpan)"/>.
            /// </remarks>
            public async Task SleepAsync()
            {
                if (delay <= TimeSpan.Zero && delay != Timeout.InfiniteTimeSpan)
                {
                    return;
                }
                await Task.Delay(delay).ConfigureAwait(false);
            }

            /// <summary>
            /// Asynchronously creates a delay for the specified duration, or until the provided cancellation token is triggered.
            /// If delay is zero or negative (excluding <see cref="Timeout.InfiniteTimeSpan"/>), the method returns immediately.
            /// </summary>
            /// <param name="ct">The <see cref="CancellationToken"/> to observe for early termination of the wait.</param>
            /// <returns>
            /// A task that resolves to <see langword="true"/> if the full delay elapsed,
            /// or <see langword="false"/> if cancellation was requested.
            /// </returns>
            /// <remarks>
            /// This method suppresses <see cref="OperationCanceledException"/> and returns a boolean state instead.
            /// It is highly optimized to avoid generating an asynchronous state machine if the wait can be skipped.
            /// </remarks>
            public Task<bool> SleepAsync(CancellationToken ct)
            {
                if (delay <= TimeSpan.Zero && delay != Timeout.InfiniteTimeSpan)
                {
                    return Task.FromResult(true);
                }
                if (ct.IsCancellationRequested)
                {
                    return Task.FromResult(false);
                }
                return ExecuteSleepAsync(delay, ct);

                static async Task<bool> ExecuteSleepAsync(TimeSpan delay, CancellationToken ct)
                {
                    try
                    {
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                        return true;
                    }
                    catch (OperationCanceledException)
                    {
                        return false;
                    }
                }
            }
        }
    }
}
