using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;

namespace RCi.Toolbox.Tests
{
    [Parallelizable(ParallelScope.All)]
    public static class SyncTests
    {
        [Test]
        public static void Ctor_InitValue_FuncEquals() => AssertCtor(true, true);

        [Test]
        public static void Ctor_InitValue() => AssertCtor(true, false);

        [Test]
        public static void Ctor_FuncEquals() => AssertCtor(false, true);

        [Test]
        public static void Ctor_FuncEquals_Throws()
        {
            var actual = default(Sync<int>);
            Assert.Throws<ArgumentNullException>(() => actual = new Sync<int>(null!));
            Assert.That(actual, Is.Null);
        }

        [Test]
        public static void Ctor() => AssertCtor(false, false);

        [Test]
        public static void GenericParameters()
        {
            var valueType = new Sync<int>();
            valueType.Value = 1;
            //valueType.Value = null; // <--- compile error

            var valueTypeNullable = new Sync<int?>();
            valueTypeNullable.Value = 1;
            valueTypeNullable.Value = null;

            var referenceType = new Sync<object>();
            referenceType.Value = new object();
            //referenceType.Value = null;  // <--- compile warning

            var referenceTypeNullable = new Sync<object?>();
            referenceTypeNullable.Value = new object();
            referenceTypeNullable.Value = null;
        }

        private static void AssertCtor(bool useInitValue, bool useFuncEquals)
        {
            var initValue = useInitValue ? 123 : 0;

            // provide funcEquals
            var funcEqualsCounter = 0;
            var funcEqualsArgsLeft = 0;
            var funcEqualsArgsRight = 0;
            var funcEquals = new Func<int, int, bool>(
                (left, right) =>
                {
                    funcEqualsCounter++;
                    funcEqualsArgsLeft = left;
                    funcEqualsArgsRight = right;
                    return left == right;
                }
            );

            // call ctor
            Sync<int> actual;
            if (useInitValue)
            {
                actual = useFuncEquals ? new Sync<int>(123, funcEquals) : new Sync<int>(123);
            }
            else
            {
                actual = useFuncEquals ? new Sync<int>(funcEquals) : new Sync<int>();
            }

            // hook ValueChanged
            var valueChangedCounter = 0;
            var valueChangedLastSender = default(object);
            var valueChangedLastValue = 0;
            actual.ValueChanged += (sender, newValue) =>
            {
                valueChangedCounter++;
                valueChangedLastSender = sender;
                valueChangedLastValue = newValue;
            };

            // check if seeding initial value works
            Assert.That(actual.Value, Is.EqualTo(initValue));

            if (useFuncEquals)
            {
                // make sure funcEquals wasn't invoked on ctor
                Assert.That(funcEqualsCounter, Is.EqualTo(0));
                Assert.That(funcEqualsArgsLeft, Is.EqualTo(0));
                Assert.That(funcEqualsArgsRight, Is.EqualTo(0));
            }

            // make sure ValueChanged wasn't invoked on ctor
            Assert.That(valueChangedCounter, Is.EqualTo(0));
            Assert.That(valueChangedLastSender, Is.Null);
            Assert.That(valueChangedLastValue, Is.EqualTo(0));

            // set new value
            actual.Value = 456;

            // ensure value is set
            Assert.That(actual.Value, Is.EqualTo(456));

            if (useFuncEquals)
            {
                // ensure equality check works
                Assert.That(funcEqualsCounter, Is.EqualTo(1));
                Assert.That(funcEqualsArgsLeft, Is.EqualTo(initValue));
                Assert.That(funcEqualsArgsRight, Is.EqualTo(456));
            }

            // ensure ValueChanged fired
            Assert.That(valueChangedCounter, Is.EqualTo(1));
            Assert.That(ReferenceEquals(actual, valueChangedLastSender));
            Assert.That(valueChangedLastValue, Is.EqualTo(456));

            // set to the same value
            actual.Value = 456;

            if (useFuncEquals)
            {
                // ensure equality check was invoked
                Assert.That(funcEqualsCounter, Is.EqualTo(2));
                Assert.That(funcEqualsArgsLeft, Is.EqualTo(456));
                Assert.That(funcEqualsArgsRight, Is.EqualTo(456));
            }

            // ensure ValueChanged wasn't fired
            Assert.That(valueChangedCounter, Is.EqualTo(1));
            Assert.That(ReferenceEquals(actual, valueChangedLastSender));
            Assert.That(valueChangedLastValue, Is.EqualTo(456));
        }

        [Test]
        public static void ImplicitOperator()
        {
            var actual = new Sync<int>(123);
            int value = actual;
            Assert.That(value, Is.EqualTo(123));
        }

        [Test]
        public static void ToString_()
        {
            var actual = new Sync<int>(123);
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

        private static void AssertAccessLocked(Action<Sync<long>> actionAssert)
        {
            var stopwatch = Stopwatch.StartNew();
            var threadsTimeout = new TimeSpan(0, 0, 0, 1);
            var sync = new Sync<long>();
            var tasksReadOnly = new Task[10];
            for (var i = 0; i < tasksReadOnly.Length; i++)
            {
                tasksReadOnly[i] = Task.Factory.StartNew(() =>
                {
                    while (true)
                    {
                        if (stopwatch.Elapsed > threadsTimeout)
                        {
                            return;
                        }
                        actionAssert(sync);
                    }
                });
            }
            Task.WaitAll(tasksReadOnly);
        }

        //

        [Test]
        public static async Task WaitForAsync_ConditionMetInstantly_ReturnsTrue()
        {
            // Arrange
            var box = new Sync<int>(5);
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
            var box = new Sync<int>(0);
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
            var box = new Sync<int>(0);
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
            var box = new Sync<int>(0);
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
            var box = new Sync<int>(0);
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

        //

        [Test]
        public static void WaitFor_ConditionMetInstantly_ReturnsTrue()
        {
            // Arrange
            var box = new Sync<int>(5);
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

        [Test]
        public static async Task WaitFor_NeverMet_TimesOut_Resiliently()
        {
            var box = new Sync<int>(0);
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

        [Test]
        public static async Task WaitFor_ConditionMetLater_Succeeds_Resiliently()
        {
            var box = new Sync<int>(0);
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
    }
}
