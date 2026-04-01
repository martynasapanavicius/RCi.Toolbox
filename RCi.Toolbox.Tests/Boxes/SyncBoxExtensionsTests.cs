using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using RCi.Toolbox.Boxes;

namespace RCi.Toolbox.Tests.Boxes
{
    [Parallelizable(ParallelScope.All)]
    public static class SyncBoxExtensionsTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public static async Task WaitForAsync_ConditionMetInstantly_ReturnsTrue(bool useDeferred)
        {
            // Arrange
            ISyncBox<int> box = useDeferred ? new SyncBoxDeferred<int>(5) : new SyncBox<int>(5);
            var fakeTime = new FakeTimeProvider();

            // Act
            var result = await box.WaitForAsync(
                v => v == 5,
                TimeSpan.FromMinutes(1),
                fakeTime,
                CancellationToken.None
            );

            // Assert
            Assert.True(result, "Should return true synchronously without waiting.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public static async Task WaitForAsync_NeverMet_TimesOut_Resiliently(bool useDeferred)
        {
            // Arrange
            ISyncBox<int> box = useDeferred ? new SyncBoxDeferred<int>(0) : new SyncBox<int>(0);
            var fakeTime = new FakeTimeProvider();

            // We use a massive timeout to defeat any possible CI thread-pool starvation.
            var timeout = TimeSpan.FromMinutes(1);

            // Act: Start the wait task (it will block asynchronously)
            var waitTask = box.WaitForAsync(v => v == 5, timeout, fakeTime, CancellationToken.None);

            // Instantly fast-forward virtual time past the timeout.
            // This takes 0 actual CPU seconds but triggers the WaitAsync TimeoutException immediately.
            fakeTime.Advance(timeout);

            // Assert
            var result = await waitTask;
            Assert.False(
                result,
                "Should return false exactly when the virtual timeout is reached."
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public static async Task WaitForAsync_ConditionMetLater_Succeeds_Resiliently(
            bool useDeferred
        )
        {
            // Arrange
            ISyncBox<int> box = useDeferred ? new SyncBoxDeferred<int>(0) : new SyncBox<int>(0);
            var fakeTime = new FakeTimeProvider();
            var timeout = TimeSpan.FromMinutes(1);

            // Act: Start the wait
            var waitTask = box.WaitForAsync(v => v == 5, timeout, fakeTime, CancellationToken.None);

            // Simulate some time passing (e.g., 30 seconds).
            // This proves the wait is holding and hasn't timed out prematurely.
            fakeTime.Advance(TimeSpan.FromSeconds(30));

            // Mutate the state. This will synchronously fire the event and complete the TaskCompletionSource.
            box.Value = 5;

            // Assert
            var result = await waitTask;
            Assert.True(
                result,
                "Should return true because the value was updated before the 5-minute virtual timeout."
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public static async Task WaitForAsync_CancelledViaToken_AbortsImmediately(bool useDeferred)
        {
            // Arrange
            ISyncBox<int> box = useDeferred ? new SyncBoxDeferred<int>(0) : new SyncBox<int>(0);
            var fakeTime = new FakeTimeProvider();
            var timeout = TimeSpan.FromMinutes(1);
            using var cts = new CancellationTokenSource();

            // Act
            var waitTask = box.WaitForAsync(v => v == 5, timeout, fakeTime, cts.Token);

            // Cancel the token explicitly (simulating a user clicking 'cancel' or a host shutting down)
            await cts.CancelAsync();

            // Assert
            var result = await waitTask;
            Assert.False(
                result,
                "Should return false because the operation was explicitly cancelled."
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public static async Task WaitForAsync_ZeroTimeout_EvaluatesSynchronously(bool useDeferred)
        {
            // Arrange
            ISyncBox<int> box = useDeferred ? new SyncBoxDeferred<int>(0) : new SyncBox<int>(0);
            var fakeTime = new FakeTimeProvider();

            // Act
            var resultFalse = await box.WaitForAsync(
                v => v == 5,
                TimeSpan.Zero,
                fakeTime,
                CancellationToken.None
            );

            box.Value = 5;
            var resultTrue = await box.WaitForAsync(
                v => v == 5,
                TimeSpan.Zero,
                fakeTime,
                CancellationToken.None
            );

            // Assert
            Assert.False(resultFalse);
            Assert.True(resultTrue);
        }

        [TestCase(false)]
        [TestCase(true)]
        public static async Task WaitForAsync_MultipleWaiters_AllCompleteSuccessfully(
            bool useDeferred
        )
        {
            // Arrange
            ISyncBox<int> box = useDeferred ? new SyncBoxDeferred<int>(0) : new SyncBox<int>(0);
            var fakeTime = new FakeTimeProvider();
            var timeout = TimeSpan.FromMinutes(1);

            // Act: Create 50 concurrent waiters
            var tasks = new List<Task<bool>>();
            for (var i = 0; i < 50; i++)
            {
                tasks.Add(box.WaitForAsync(v => v == 5, timeout, fakeTime, CancellationToken.None));
            }

            // Ensure all tasks have yielded and hooked into the event
            await Task.Yield();

            // Mutate the state, triggering the event broadcast
            box.Value = 5;

            // Wait for all 50 tasks to complete
            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.That(
                results,
                Has.All.True,
                "All 50 concurrent waiters should wake up and return true."
            );
        }

        //

        [TestCase(false)]
        [TestCase(true)]
        public static void WaitFor_ConditionMetInstantly_ReturnsTrue(bool useDeferred)
        {
            // Arrange
            ISyncBox<int> box = useDeferred ? new SyncBoxDeferred<int>(5) : new SyncBox<int>(5);
            var fakeTime = new FakeTimeProvider();

            // Act
            // We don't need Task.Run here because it will return instantly
            var result = box.WaitFor(
                v => v == 5,
                TimeSpan.FromMinutes(1),
                fakeTime,
                CancellationToken.None
            );

            // Assert
            Assert.True(result);
        }

        [TestCase(false)]
        [TestCase(true)]
        public static async Task WaitFor_NeverMet_TimesOut_Resiliently(bool useDeferred)
        {
            ISyncBox<int> box = useDeferred ? new SyncBoxDeferred<int>(0) : new SyncBox<int>(0);
            var fakeTime = new FakeTimeProvider();
            var timeout = TimeSpan.FromMinutes(1);

            var waitTask = Task.Run(() =>
                box.WaitFor(v => v == 5, timeout, fakeTime, CancellationToken.None)
            );

            // Keep advancing virtual time until the task completes.
            // This guarantees we eventually cross the token's scheduled cancellation time,
            // regardless of exactly when the thread pool started the task.
            while (!waitTask.IsCompleted)
            {
                fakeTime.Advance(timeout);

                // Briefly yield the main thread so the thread pool can process the task
                await Task.Yield();
            }

            var result = await waitTask;
            Assert.False(
                result,
                "Should return false exactly when the virtual timeout is reached."
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public static async Task WaitFor_ConditionMetLater_Succeeds_Resiliently(bool useDeferred)
        {
            ISyncBox<int> box = useDeferred ? new SyncBoxDeferred<int>(0) : new SyncBox<int>(0);
            var fakeTime = new FakeTimeProvider();
            var timeout = TimeSpan.FromSeconds(5);

            var waitTask = Task.Run(() =>
                box.WaitFor(v => v == 5, timeout, fakeTime, CancellationToken.None)
            );

            // We want to test the "wake up from wait" path, not the "instant success" path.
            // A tiny real-world delay gives the Task.Run time to block.
            // If a CI lag spike makes this delay miss its mark, the test still safely passes
            // via the instant-success path without failing the build.
            await Task.Delay(500);

            // Mutate the state. This fires the event and unblocks the ManualResetEventSlim.
            box.Value = 5;

            // Since the value is met, the task should complete. If it's stubbornly blocking,
            // this await will hang, which test engine will catch as a test failure.
            var result = await waitTask;
            Assert.True(result, "Should return true because value was updated before timeout.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public static async Task WaitFor_CancelledViaToken_AbortsImmediately(bool useDeferred)
        {
            // Arrange
            ISyncBox<int> box = useDeferred ? new SyncBoxDeferred<int>(0) : new SyncBox<int>(0);
            var fakeTime = new FakeTimeProvider();
            var timeout = TimeSpan.FromMinutes(1);
            using var cts = new CancellationTokenSource();

            // Act
            var waitTask = Task.Run(() => box.WaitFor(v => v == 5, timeout, fakeTime, cts.Token));

            // Give the Task.Run a tiny window to enter the Wait() block
            await Task.Delay(500);

            // Cancel the token to violently wake up the ManualResetEventSlim
            await cts.CancelAsync();

            // Assert
            var result = await waitTask;
            Assert.False(
                result,
                "Should return false because the synchronous wait was explicitly cancelled."
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public static void WaitFor_ZeroTimeout_EvaluatesSynchronously(bool useDeferred)
        {
            // Arrange
            ISyncBox<int> box = useDeferred ? new SyncBoxDeferred<int>(0) : new SyncBox<int>(0);
            var fakeTime = new FakeTimeProvider();

            // Act
            var resultFalse = box.WaitFor(
                v => v == 5,
                TimeSpan.Zero,
                fakeTime,
                CancellationToken.None
            );

            box.Value = 5;
            var resultTrue = box.WaitFor(
                v => v == 5,
                TimeSpan.Zero,
                fakeTime,
                CancellationToken.None
            );

            // Assert
            Assert.False(resultFalse);
            Assert.True(resultTrue);
        }

        [TestCase(false)]
        [TestCase(true)]
        public static async Task WaitFor_MultipleWaiters_AllCompleteSuccessfully(bool useDeferred)
        {
            // Arrange
            ISyncBox<int> box = useDeferred ? new SyncBoxDeferred<int>(0) : new SyncBox<int>(0);
            var fakeTime = new FakeTimeProvider();
            var timeout = TimeSpan.FromMinutes(1);

            // Act: Create 50 concurrent blocking waiters.
            var tasks = new List<Task<bool>>();
            for (var i = 0; i < 50; i++)
            {
                // Use LongRunning to spin up dedicated OS threads.
                // This prevents the 50 blocking waiters from starving the ThreadPool,
                // allowing the rest of the test (and the async event pump) to function normally.
                tasks.Add(
                    Task.Factory.StartNew(
                        () => box.WaitFor(v => v == 5, timeout, fakeTime, CancellationToken.None),
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default
                    )
                );
            }

            // Give the dedicated threads a moment to spin up and hit their Wait() blocks.
            await Task.Delay(500);

            // Mutate the state.
            // This broadcasts the event, concurrently triggering Set() on 50 different ManualResetEventSlims.
            box.Value = 5;

            // Wait for all background threads to unblock, cleanup, and return.
            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.That(
                results,
                Has.All.True,
                "All 50 concurrent blocking waiters should wake up and return true."
            );
        }
    }
}
