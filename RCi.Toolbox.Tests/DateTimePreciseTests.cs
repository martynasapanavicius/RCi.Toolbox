using System;

namespace RCi.Toolbox.Tests
{
    [Parallelizable]
    public static class DateTimePreciseTests
    {
        [SetUp]
        public static void Setup()
        {
            // warmup
            _ = DateTimePrecise.UtcNow;
        }

        [Test]
        public static void UtcNow()
        {
            var expected = DateTime.UtcNow;
            var actual = DateTimePrecise.UtcNow;
            Assert.That(actual.Kind, Is.EqualTo(expected.Kind));
            Assert.That(actual.IsDaylightSavingTime(), Is.EqualTo(expected.IsDaylightSavingTime()));
            var diff = actual - expected;
            Assert.That(diff, Is.InRange(TimeSpan.FromMilliseconds(-100), TimeSpan.FromMilliseconds(100)));
        }

        [Test]
        public static void Now()
        {
            var expected = DateTime.Now;
            var actual = DateTimePrecise.Now;
            Assert.That(actual.Kind, Is.EqualTo(expected.Kind));
            Assert.That(actual.IsDaylightSavingTime(), Is.EqualTo(expected.IsDaylightSavingTime()));
            var diff = actual - expected;
            Assert.That(diff, Is.InRange(TimeSpan.FromMilliseconds(-100), TimeSpan.FromMilliseconds(100)));
        }
    }
}
