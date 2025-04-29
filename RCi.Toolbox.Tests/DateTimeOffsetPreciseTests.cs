using System;

namespace RCi.Toolbox.Tests
{
    [Parallelizable]
    public static class DateTimeOffsetPreciseTests
    {
        [SetUp]
        public static void Setup()
        {
            // warmup
            _ = DateTimeOffsetPrecise.UtcNow;
        }

        [Test]
        public static void UtcNow()
        {
            var expected = DateTimeOffset.UtcNow;
            var actual = DateTimeOffsetPrecise.UtcNow;
            var diff = actual - expected;
            Assert.That(
                diff,
                Is.InRange(TimeSpan.FromMilliseconds(-100), TimeSpan.FromMilliseconds(100))
            );
        }

        [Test]
        public static void Now()
        {
            var expected = DateTimeOffset.Now;
            var actual = DateTimeOffsetPrecise.Now;
            var diff = actual - expected;
            Assert.That(
                diff,
                Is.InRange(TimeSpan.FromMilliseconds(-100), TimeSpan.FromMilliseconds(100))
            );
        }
    }
}
