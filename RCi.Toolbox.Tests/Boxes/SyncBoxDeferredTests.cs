using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using RCi.Toolbox.Boxes;

namespace RCi.Toolbox.Tests.Boxes
{
    [Parallelizable(ParallelScope.All)]
    public static class SyncBoxDeferredTests
    {
        [Test]
        public static void Ctor_InitValue_FuncEquals() =>
            AssertCtor(
                true,
                true,
                [(123, 456), (456, 456), (456, 789)],
                [(true, 456), (true, 789)]
            );

        [Test]
        public static void Ctor_InitValue() =>
            AssertCtor(true, false, [], [(true, 456), (true, 789)]);

        [Test]
        public static void Ctor_FuncEquals() =>
            AssertCtor(false, true, [(0, 456), (456, 456), (456, 789)], [(true, 456), (true, 789)]);

        [Test]
        public static void Ctor() => AssertCtor(false, false, [], [(true, 456), (true, 789)]);

        private static void AssertCtor(
            bool useInitValue,
            bool useFuncEquals,
            ImmutableArray<(int Left, int Right)> funcEqualsHistoryExpected,
            ImmutableArray<(bool IsSameSender, int Value)> valueChangedHistoryExpected
        )
        {
            var funcEqualsHistoryActual = new List<(int Left, int Right)>();
            var valueChangedHistoryActual = new List<(bool IsSameSender, int Value)>();

            var initValue = useInitValue ? 123 : 0;

            // provide funcEquals
            var funcEquals = new Func<int, int, bool>(
                (left, right) =>
                {
                    funcEqualsHistoryActual.Add((left, right));
                    return left == right;
                }
            );

            // call ctor
            SyncBoxDeferred<int> actual;
            if (useInitValue)
            {
                actual = useFuncEquals
                    ? new SyncBoxDeferred<int>(123, funcEquals)
                    : new SyncBoxDeferred<int>(123);
            }
            else
            {
                actual = useFuncEquals
                    ? new SyncBoxDeferred<int>(0, funcEquals)
                    : new SyncBoxDeferred<int>(0);
            }

            using (actual)
            {
                // hook ValueChanged
                actual.ValueChanged += (sender, newValue) =>
                    valueChangedHistoryActual.Add((ReferenceEquals(sender, actual), newValue));

                // check if seeding initial value works
                Assert.That(actual.Value, Is.EqualTo(initValue));
                Assert.That(funcEqualsHistoryActual, Has.Count.EqualTo(0));
                Assert.That(valueChangedHistoryActual, Has.Count.EqualTo(0));

                // set new value
                actual.Value = 456;

                // set to the same value
                actual.Value = 456;

                // set to a different value
                actual.Value = 789;
            }

            Assert.That(funcEqualsHistoryActual.SequenceEqual(funcEqualsHistoryExpected), Is.True);
            Assert.That(
                valueChangedHistoryActual.SequenceEqual(valueChangedHistoryExpected),
                Is.True
            );
        }

        [Test]
        public static void GenericParameters()
        {
            var valueType = new SyncBoxDeferred<int>(0);
            valueType.Value = 1;
            //valueType.Value = null; // <--- compile error

            var valueTypeNullable = new SyncBoxDeferred<int?>(null);
            valueTypeNullable.Value = 1;
            valueTypeNullable.Value = null;

            var referenceType = new SyncBoxDeferred<object>(null!);
            referenceType.Value = new object();
            //referenceType.Value = null;  // <--- compile warning

            var referenceTypeNullable = new SyncBoxDeferred<object?>(null);
            referenceTypeNullable.Value = new object();
            referenceTypeNullable.Value = null;
        }

        [Test]
        public static void ImplicitOperator()
        {
            var actual = new SyncBoxDeferred<int>(123);
            int value = actual;
            Assert.That(value, Is.EqualTo(123));
        }

        [Test]
        public static void ToString_()
        {
            var actual = new SyncBoxDeferred<int>(123);
            Assert.That(actual.ToString(), Is.EqualTo("123"));
        }

        [Test]
        public static void AccessLocked_ReadWriteAccessLockedDelegate()
        {
            AssertAccessLocked(sync =>
            {
                sync.AccessLocked(
                    (getter, setter) =>
                    {
                        var valueSet = Random.Shared.Next();
                        setter(valueSet);
                        var valueGet = getter();
                        Assert.That(valueGet, Is.EqualTo(valueSet));
                    }
                );
            });
        }

        [Test]
        public static void AccessLocked_ReadWriteAccessLockedDelegate_TResult()
        {
            AssertAccessLocked(sync =>
            {
                var valueSet = Random.Shared.Next();
                var valueGet = sync.AccessLocked(
                    (getter, setter) =>
                    {
                        setter(valueSet);
                        return getter();
                    }
                );
                Assert.That(valueGet, Is.EqualTo(valueSet));
            });
        }

        [Test]
        public static void AccessLocked_ReadOnlyAccessLockedDelegate()
        {
            AssertAccessLocked(sync =>
            {
                sync.AccessLocked(getter =>
                {
                    var valueSet = Random.Shared.Next();
                    sync.Value = valueSet;
                    var valueGet = getter();
                    Assert.That(valueGet, Is.EqualTo(valueSet));
                });
            });
        }

        [Test]
        public static void AccessLocked_ReadOnlyAccessLockedDelegate_TResult()
        {
            AssertAccessLocked(sync =>
            {
                var valueSet = Random.Shared.Next();
                var valueGet = sync.AccessLocked(getter =>
                {
                    sync.Value = valueSet;
                    return getter();
                });
                Assert.That(valueGet, Is.EqualTo(valueSet));
            });
        }

        private static void AssertAccessLocked(Action<SyncBoxDeferred<long>> actionAssert)
        {
            var sw = ValueStopwatch.StartNew();
            var threadsTimeout = TimeSpan.FromSeconds(1);
            var box = new SyncBoxDeferred<long>(0);
            var tasks = new Task[10];
            for (var i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    while (true)
                    {
                        if (sw.Elapsed > threadsTimeout)
                        {
                            return;
                        }
                        actionAssert(box);
                    }
                });
            }
            Task.WaitAll(tasks);
        }

        //

        [Test]
        public static async Task WaitForAsync_ConditionMetInstantly_ReturnsTrue()
        {
            // Arrange
            var box = new SyncBoxDeferred<int>(5);
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

        [Test]
        public static async Task WaitForAsync_NeverMet_TimesOut_Resiliently()
        {
            // Arrange
            var box = new SyncBoxDeferred<int>(0);
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

        [Test]
        public static async Task WaitForAsync_ConditionMetLater_Succeeds_Resiliently()
        {
            // Arrange
            var box = new SyncBoxDeferred<int>(0);
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

        [Test]
        public static async Task WaitForAsync_CancelledViaToken_AbortsImmediately()
        {
            // Arrange
            var box = new SyncBoxDeferred<int>(0);
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

        [Test]
        public static async Task WaitForAsync_ZeroTimeout_EvaluatesSynchronously()
        {
            // Arrange
            var box = new SyncBoxDeferred<int>(0);
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

        ////

        //[Test]
        //public static void WaitFor_ConditionMetInstantly_ReturnsTrue()
        //{
        //    // Arrange
        //    var box = new SyncBox<int>(5);
        //    var fakeTime = new FakeTimeProvider();

        //    // Act
        //    // We don't need Task.Run here because it will return instantly
        //    var result = box.WaitFor(
        //        v => v == 5,
        //        TimeSpan.FromMinutes(1),
        //        fakeTime,
        //        CancellationToken.None
        //    );

        //    // Assert
        //    Assert.True(result);
        //}

        //[Test]
        //public static async Task WaitFor_NeverMet_TimesOut_Resiliently()
        //{
        //    var box = new SyncBox<int>(0);
        //    var fakeTime = new FakeTimeProvider();
        //    var timeout = TimeSpan.FromMinutes(1);

        //    var waitTask = Task.Run(() =>
        //        box.WaitFor(v => v == 5, timeout, fakeTime, CancellationToken.None)
        //    );

        //    // Keep advancing virtual time until the task completes.
        //    // This guarantees we eventually cross the token's scheduled cancellation time,
        //    // regardless of exactly when the thread pool started the task.
        //    while (!waitTask.IsCompleted)
        //    {
        //        fakeTime.Advance(timeout);

        //        // Briefly yield the main thread so the thread pool can process the task
        //        await Task.Yield();
        //    }

        //    var result = await waitTask;
        //    Assert.False(
        //        result,
        //        "Should return false exactly when the virtual timeout is reached."
        //    );
        //}

        //[Test]
        //public static async Task WaitFor_ConditionMetLater_Succeeds_Resiliently()
        //{
        //    var box = new SyncBox<int>(0);
        //    var fakeTime = new FakeTimeProvider();
        //    var timeout = TimeSpan.FromSeconds(5);

        //    var waitTask = Task.Run(() =>
        //        box.WaitFor(v => v == 5, timeout, fakeTime, CancellationToken.None)
        //    );

        //    // We want to test the "wake up from wait" path, not the "instant success" path.
        //    // A tiny real-world delay gives the Task.Run time to block.
        //    // If a CI lag spike makes this delay miss its mark, the test still safely passes
        //    // via the instant-success path without failing the build.
        //    await Task.Delay(500);

        //    // Mutate the state. This fires the event and unblocks the ManualResetEventSlim.
        //    box.Value = 5;

        //    // Since the value is met, the task should complete. If it's stubbornly blocking,
        //    // this await will hang, which test engine will catch as a test failure.
        //    var result = await waitTask;
        //    Assert.True(result, "Should return true because value was updated before timeout.");
        //}
    }
}
