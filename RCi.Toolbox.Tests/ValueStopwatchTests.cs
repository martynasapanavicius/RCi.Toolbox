using System;
using System.Threading;
using Microsoft.Extensions.Time.Testing;

namespace RCi.Toolbox.Tests
{
    [Parallelizable(ParallelScope.All)]
    public static class ValueStopwatchTests
    {
        [Test]
        public static void Elapsed()
        {
            var sw = ValueStopwatch.StartNew();
            Thread.Sleep(100);
            var actual = sw.Elapsed;
            Assert.That(actual, Is.GreaterThan(TimeSpan.Zero));
        }

        [Test]
        public static void Elapsed_Uninitialized()
        {
            var sw = default(ValueStopwatch);
            Assert.That(sw.IsActive, Is.False);
            Assert.Throws<InvalidOperationException>(() =>
            {
                _ = sw.Elapsed;
            });
        }

        [Test]
        public static void IsActive_Initialized()
        {
            var sw = ValueStopwatch.StartNew();
            Assert.That(sw.IsActive, Is.True);
        }

        [Test]
        public static void TimeProvider_Elapsed()
        {
            var fakeTime = new FakeTimeProvider();
            var sw = ValueStopwatch.StartNew(fakeTime);
            Assert.That(sw.IsActive, Is.True);
            Assert.That(sw.Elapsed, Is.EqualTo(TimeSpan.Zero));

            fakeTime.Advance(TimeSpan.FromSeconds(42));
            Assert.That(sw.Elapsed, Is.EqualTo(TimeSpan.FromSeconds(42)));
        }

        [Test]
        public static void TimeProvider_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => ValueStopwatch.StartNew(null!));
        }
    }
}
