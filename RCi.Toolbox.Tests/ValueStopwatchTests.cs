using System;
using System.Threading;

namespace RCi.Toolbox.Tests
{
    [Parallelizable]
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
            Assert.Throws<InvalidOperationException>(() =>
            {
                _ = sw.Elapsed;
            });
        }
    }
}
