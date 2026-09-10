using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;

namespace RCi.Toolbox.Tests
{
    [Parallelizable(ParallelScope.All)]
    public static class CoalescingWorkerTests
    {
        [Test]
        public static void Schedule()
        {
            using var waiterJobStarted = new ManualResetEvent(false);
            using var waiterJobIsAllowedToFinish = new ManualResetEvent(false);

            var counter = 0;

            var worker = new CoalescingWorker(() =>
            {
                waiterJobStarted.Set();
                counter++;
                Assert.That(waiterJobIsAllowedToFinish.WaitOne(TimeSpan.FromSeconds(10)), Is.True);
            });

            try
            {
                // schedule first
                var success = worker.Schedule(out var wasCoalesced);
                Assert.That(success, Is.True);
                Assert.That(wasCoalesced, Is.False); // first schedule ever

                // wait until first job started execution
                Assert.That(waiterJobStarted.WaitOne(TimeSpan.FromSeconds(10)), Is.True);

                // schedule the rest while the first job is still executing
                success = worker.Schedule(out wasCoalesced);
                Assert.That(success, Is.True);
                Assert.That(wasCoalesced, Is.False); // is new again, because old one is already executing

                success = worker.Schedule(out wasCoalesced);
                Assert.That(success, Is.True);
                Assert.That(wasCoalesced, Is.True); // ignored

                success = worker.Schedule(out wasCoalesced);
                Assert.That(success, Is.True);
                Assert.That(wasCoalesced, Is.True); // ignored

                success = worker.Schedule(out wasCoalesced);
                Assert.That(success, Is.True);
                Assert.That(wasCoalesced, Is.True); // ignored

                // signal so that first job can finish execution
                waiterJobIsAllowedToFinish.Set();

                // wait until second job started execution
                Assert.That(waiterJobStarted.WaitOne(TimeSpan.FromSeconds(10)), Is.True);

                // signal so that second job can finish execution
                waiterJobIsAllowedToFinish.Set();
            }
            finally
            {
                worker.Dispose();
            }

            // although we invoked schedule 5 times, only 2 jobs were executed
            Assert.That(counter, Is.EqualTo(2));
        }

        [Test]
        public static async Task ScheduleAsync()
        {
            using var waiterJobStarted = new ManualResetEvent(false);
            using var waiterJobIsAllowedToFinish = new ManualResetEvent(false);

            var counter = 0;

            var worker = new CoalescingWorker(() =>
            {
                waiterJobStarted.Set();
                counter++;
                Assert.That(waiterJobIsAllowedToFinish.WaitOne(TimeSpan.FromSeconds(10)), Is.True);
            });

            try
            {
                // schedule first
                var (success, wasCoalesced) = await worker.ScheduleAsync();
                Assert.That(success, Is.True);
                Assert.That(wasCoalesced, Is.False); // first schedule ever

                // wait until first job started execution
                Assert.That(waiterJobStarted.WaitOne(TimeSpan.FromSeconds(10)), Is.True);

                // schedule the rest while the first job is still executing
                (success, wasCoalesced) = await worker.ScheduleAsync();
                Assert.That(success, Is.True);
                Assert.That(wasCoalesced, Is.False); // is new again, because old one is already executing

                (success, wasCoalesced) = await worker.ScheduleAsync();
                Assert.That(success, Is.True);
                Assert.That(wasCoalesced, Is.True); // ignored

                (success, wasCoalesced) = await worker.ScheduleAsync();
                Assert.That(success, Is.True);
                Assert.That(wasCoalesced, Is.True); // ignored

                (success, wasCoalesced) = await worker.ScheduleAsync();
                Assert.That(success, Is.True);
                Assert.That(wasCoalesced, Is.True); // ignored

                // signal so that first job can finish execution
                waiterJobIsAllowedToFinish.Set();

                // wait until second job started execution
                Assert.That(waiterJobStarted.WaitOne(TimeSpan.FromSeconds(10)), Is.True);

                // signal so that second job can finish execution
                waiterJobIsAllowedToFinish.Set();
            }
            finally
            {
                worker.Dispose();
            }

            // although we invoked schedule 5 times, only 2 jobs were executed
            Assert.That(counter, Is.EqualTo(2));
        }

        [Test]
        public static void OnJobExceptionCallback()
        {
            var exceptions = new List<int>();

            using var worker = new CoalescingWorker(
                new CoalescingWorkerParameters
                {
                    OnJobExceptionCallback = x => exceptions.Add(int.Parse(x.Message)),
                },
                () => throw new Exception($"{exceptions.Count}")
            );

            Assert.That(exceptions, Has.Count.EqualTo(0));

            for (var i = 0; i < 100; i++)
            {
                worker.Schedule();
                worker.WaitForIdle();
                Assert.That(exceptions, Has.Count.EqualTo(i + 1));
            }

            Assert.That(exceptions.SequenceEqual(Enumerable.Range(0, 100)), Is.True);
        }

        //

        [Test]
        public static async Task WaitForIdleAsyncSuccessInstant()
        {
            using var worker = new CoalescingWorker(() => { });
            var success = await worker.WaitForIdleAsync(TimeSpan.FromMilliseconds(500));
            Assert.That(success, Is.True);
        }

        [Test]
        public static async Task WaitForIdleAsyncFailTimeout()
        {
            using var waiterJobStarted = new ManualResetEvent(false);
            using var waiterAllowToEndJob = new ManualResetEvent(false);

            using var worker = new CoalescingWorker(() =>
            {
                waiterJobStarted.Set();
                Assert.That(waiterAllowToEndJob.WaitOne(TimeSpan.FromSeconds(10)), Is.True);
            });

            worker.Schedule();

            Assert.That(waiterJobStarted.WaitOne(TimeSpan.FromSeconds(10)), Is.True);

            var success = await worker.WaitForIdleAsync(TimeSpan.FromMilliseconds(500));
            Assert.That(success, Is.False);

            waiterAllowToEndJob.Set();
        }

        [Test]
        public static async Task WaitForIdleAsyncFailCancelled()
        {
            using var cts = new CancellationTokenSource();
            var ct = cts.Token;

            using var waiterJobStarted = new ManualResetEvent(false);
            using var waiterAllowToEndJob = new ManualResetEvent(false);

            using var worker = new CoalescingWorker(() =>
            {
                waiterJobStarted.Set();
                Assert.That(waiterAllowToEndJob.WaitOne(TimeSpan.FromSeconds(10)), Is.True);
            });

            worker.Schedule();

            Assert.That(waiterJobStarted.WaitOne(TimeSpan.FromSeconds(10)), Is.True);

            var waitForIdleTask = worker.WaitForIdleAsync(TimeSpan.FromMilliseconds(1000), ct);

            cts.Cancel();

            var success = await waitForIdleTask;
            Assert.That(success, Is.False);

            waiterAllowToEndJob.Set();
        }

        [Test]
        public static async Task WaitForIdleSuccess()
        {
            using var waiterJobStarted = new ManualResetEvent(false);
            using var waiterAllowToEndJob = new ManualResetEvent(false);

            using var worker = new CoalescingWorker(() =>
            {
                waiterJobStarted.Set();
                Assert.That(waiterAllowToEndJob.WaitOne(TimeSpan.FromSeconds(10)), Is.True);
            });

            worker.Schedule();

            Assert.That(waiterJobStarted.WaitOne(TimeSpan.FromSeconds(10)), Is.True);

            var waitForIdleTask = worker.WaitForIdleAsync(TimeSpan.FromMilliseconds(1000));

            waiterAllowToEndJob.Set();

            var success = await waitForIdleTask;
            Assert.That(success, Is.True);
        }

        //

        [Test]
        public static async Task WaitForBusyAsyncSuccessInstant()
        {
            using var waiterAllowToEndJob = new ManualResetEvent(false);

            using var worker = new CoalescingWorker(() =>
            {
                Assert.That(waiterAllowToEndJob.WaitOne(TimeSpan.FromSeconds(10)), Is.True);
            });

            worker.Schedule();

            var success = await worker.WaitForBusyAsync(TimeSpan.FromMilliseconds(1000));
            Assert.That(success, Is.True);

            waiterAllowToEndJob.Set();
        }

        [Test]
        public static async Task WaitForBusyAsyncFailTimeout()
        {
            using var worker = new CoalescingWorker(() => { });
            var success = await worker.WaitForBusyAsync(TimeSpan.FromMilliseconds(500));
            Assert.That(success, Is.False);
        }

        [Test]
        public static async Task WaitForBusyAsyncFailCancelled()
        {
            using var cts = new CancellationTokenSource();
            var ct = cts.Token;

            using var worker = new CoalescingWorker(() => { });

            var waitForBusyTask = worker.WaitForBusyAsync(TimeSpan.FromMilliseconds(1000), ct);

            cts.Cancel();

            var success = await waitForBusyTask;
            Assert.That(success, Is.False);
        }

        [Test]
        public static async Task WaitForBusyAsyncSuccess()
        {
            using var waiterAllowToEndJob = new ManualResetEvent(false);

            using var worker = new CoalescingWorker(() =>
            {
                Assert.That(waiterAllowToEndJob.WaitOne(TimeSpan.FromSeconds(10)), Is.True);
            });

            var waitForBusyTask = worker.WaitForBusyAsync(TimeSpan.FromMilliseconds(1000));

            worker.Schedule();

            var success = await waitForBusyTask;
            Assert.That(success, Is.True);

            waiterAllowToEndJob.Set();
        }

        [Test]
        public static async Task ScheduleAsync_WithFakeTimeProvider()
        {
            var fakeTime = new FakeTimeProvider();
            var executed = 0;
            using var worker = new CoalescingWorker(
                new CoalescingWorkerParameters { TimeProvider = fakeTime },
                () => Interlocked.Increment(ref executed)
            );

            // Schedule with 10 second delay
            var scheduleTask = worker.ScheduleAsync(TimeSpan.FromSeconds(10));
            Assert.That(scheduleTask.IsCompleted, Is.False);
            Assert.That(executed, Is.EqualTo(0));

            // Advance time by 5 seconds - still not executed
            fakeTime.Advance(TimeSpan.FromSeconds(5));
            Assert.That(scheduleTask.IsCompleted, Is.False);
            Assert.That(executed, Is.EqualTo(0));

            // Advance time remaining 5 seconds
            fakeTime.Advance(TimeSpan.FromSeconds(5));
            var result = await scheduleTask;
            Assert.That(result.Success, Is.True);
            Assert.That(result.WasCoalesced, Is.False);

            worker.WaitForIdle();
            Assert.That(executed, Is.EqualTo(1));
        }

        [Test]
        public static async Task ScheduleAsync_WithFakeTimeProvider_Cancelled()
        {
            var fakeTime = new FakeTimeProvider();
            using var cts = new CancellationTokenSource();
            var executed = 0;
            using var worker = new CoalescingWorker(
                new CoalescingWorkerParameters { TimeProvider = fakeTime },
                () => Interlocked.Increment(ref executed)
            );

            var scheduleTask = worker.ScheduleAsync(TimeSpan.FromSeconds(10), cts.Token);
            Assert.That(scheduleTask.IsCompleted, Is.False);

            cts.Cancel();

            var result = await scheduleTask;
            Assert.That(result.Success, Is.False);
            Assert.That(result.WasCoalesced, Is.False);

            fakeTime.Advance(TimeSpan.FromSeconds(20));
            worker.WaitForIdle();
            Assert.That(executed, Is.EqualTo(0));
        }

        [Test]
        public static void Ctor_NullValidation()
        {
            Assert.Throws<ArgumentNullException>(() => _ = new CoalescingWorker(null!, () => { }));
            Assert.Throws<ArgumentNullException>(() =>
                _ = new CoalescingWorker(CoalescingWorkerParameters.Default, null!)
            );
        }
    }
}
