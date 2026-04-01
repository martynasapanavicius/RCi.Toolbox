using System;
using System.Threading;
using System.Threading.Tasks;

namespace RCi.Toolbox
{
    public static class SleepExtensions
    {
        extension(TimeSpan delay)
        {
            public bool Sleep(CancellationToken ct)
            {
                if (delay <= TimeSpan.Zero)
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

            public Task<bool> SleepAsync(CancellationToken ct)
            {
                // check delay first (no task/state machine created)
                if (delay <= TimeSpan.Zero)
                {
                    return Task.FromResult(true);
                }

                // check cancellation (no task/state machine created)
                if (ct.IsCancellationRequested)
                {
                    return Task.FromResult(false);
                }

                // only now do we enter the async state machine logic
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
