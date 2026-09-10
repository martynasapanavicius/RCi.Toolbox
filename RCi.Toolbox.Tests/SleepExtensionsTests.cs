using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;

namespace RCi.Toolbox.Tests
{
    [Parallelizable(ParallelScope.All)]
    public static class SleepExtensionsTests
    {
        [Test]
        public static async Task Sleep_AdvancesOnlyWhenTimePasses()
        {
            // Arrange
            var timeProvider = new FakeTimeProvider();
            var delay = TimeSpan.FromSeconds(5);

            // Act
            var sleepTask = Task.Run(() => delay.Sleep(timeProvider));

            // Give background thread time to block in Sleep before advancing virtual time
            while (!sleepTask.IsCompleted)
            {
                await Task.Delay(20);
                timeProvider.Advance(delay);
            }

            // Assert
            await sleepTask;
            Assert.True(sleepTask.IsCompletedSuccessfully);
        }

        [Test]
        public static async Task Sleep_WithCancellation_ReturnsFalseWhenCanceled()
        {
            // Arrange
            var timeProvider = new FakeTimeProvider();
            using var cts = new CancellationTokenSource();
            var delay = TimeSpan.FromMinutes(1); // Long delay to prove we don't wait

            // Act
            var sleepTask = Task.Run(() => delay.Sleep(timeProvider, cts.Token));

            // Cancel while it's blocked
            await cts.CancelAsync();

            var result = await sleepTask;

            // Assert
            Assert.False(result);
        }

        [Test]
        public static void Sleep_FastPaths_DoNotBlockOrUseProvider()
        {
            // Arrange
            var timeProvider = new FakeTimeProvider(); // We won't even advance this
            using var canceledCts = new CancellationTokenSource();
            canceledCts.Cancel();

            // Act & Assert
            // These should return immediately without us needing to call Advance() or Task.Run()

            TimeSpan.Zero.Sleep(timeProvider); // Should return immediately

            var isCompleted = TimeSpan.FromSeconds(5).Sleep(timeProvider, canceledCts.Token);
            Assert.False(isCompleted); // Should return false immediately due to pre-canceled token
        }

        [Test]
        public static async Task SleepAsync_AdvancesOnlyWhenTimePasses()
        {
            // Arrange
            var timeProvider = new FakeTimeProvider();
            var delay = TimeSpan.FromSeconds(10);

            // Act
            // We don't await immediately, we capture the task
            var sleepTask = delay.SleepAsync(timeProvider);

            // Assert: Task is not complete yet because time hasn't advanced
            Assert.False(sleepTask.IsCompleted);

            // Act: Advance the clock by 9 seconds
            timeProvider.Advance(TimeSpan.FromSeconds(9));
            Assert.False(sleepTask.IsCompleted);

            // Act: Advance the final second
            timeProvider.Advance(TimeSpan.FromSeconds(1));

            // Assert: Task should now be complete
            await sleepTask;
            Assert.True(sleepTask.IsCompletedSuccessfully);
        }

        [Test]
        public static async Task SleepAsync_WithCancellation_ReturnsFalseWhenCanceledEarly()
        {
            // Arrange
            var timeProvider = new FakeTimeProvider();
            using var cts = new CancellationTokenSource();
            var delay = TimeSpan.FromSeconds(10);

            // Act
            var sleepTask = delay.SleepAsync(timeProvider, cts.Token);

            // Cancel before time advances
            await cts.CancelAsync();

            var result = await sleepTask;

            // Assert
            Assert.False(result); // Returns false on cancellation
        }

        [Test]
        public static async Task SleepAsync_WithCancellation_ReturnsTrueWhenTimeElapses()
        {
            // Arrange
            var timeProvider = new FakeTimeProvider();
            using var cts = new CancellationTokenSource();
            var delay = TimeSpan.FromSeconds(10);

            // Act
            var sleepTask = delay.SleepAsync(timeProvider, cts.Token);

            // Advance time fully
            timeProvider.Advance(delay);

            var result = await sleepTask;

            // Assert
            Assert.True(result); // Returns true on completion
        }

        [Test]
        public static void Sleep_SystemProvider_NoneToken_SleepsSuccessfully()
        {
            // verify hot-path non-cancellable token branch returns true
            var slept = TimeSpan.FromMilliseconds(1).Sleep(CancellationToken.None);
            Assert.True(slept);
        }

        [Test]
        public static void NullValidation()
        {
            TimeProvider nullTimeProvider = null!;

            // Sleep(timeProvider)
            Assert.Throws<ArgumentNullException>(() => TimeSpan.Zero.Sleep(nullTimeProvider));
            Assert.Throws<ArgumentNullException>(() =>
                TimeSpan.FromSeconds(1).Sleep(nullTimeProvider)
            );

            // Sleep(timeProvider, ct)
            Assert.Throws<ArgumentNullException>(() =>
                TimeSpan.Zero.Sleep(nullTimeProvider, CancellationToken.None)
            );
            Assert.Throws<ArgumentNullException>(() =>
                TimeSpan.FromSeconds(1).Sleep(nullTimeProvider, CancellationToken.None)
            );

            // SleepAsync(timeProvider)
            Assert.Throws<ArgumentNullException>(() => TimeSpan.Zero.SleepAsync(nullTimeProvider));
            Assert.Throws<ArgumentNullException>(() =>
                TimeSpan.FromSeconds(1).SleepAsync(nullTimeProvider)
            );

            // SleepAsync(timeProvider, ct)
            Assert.Throws<ArgumentNullException>(() =>
                TimeSpan.Zero.SleepAsync(nullTimeProvider, CancellationToken.None)
            );
            Assert.Throws<ArgumentNullException>(() =>
                TimeSpan.FromSeconds(1).SleepAsync(nullTimeProvider, CancellationToken.None)
            );
        }
    }
}
