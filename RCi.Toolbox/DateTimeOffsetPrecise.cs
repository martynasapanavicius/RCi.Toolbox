using System;

namespace RCi.Toolbox
{
    /// <summary>
    /// Precise routines for <see cref="DateTimeOffset"/>.
    /// </summary>
    public static class DateTimeOffsetPrecise
    {
        /// <summary>
        /// Reference tick count on initialization.
        /// </summary>
        private static readonly DateTimeOffset _bootTimeUtc = DateTimeOffset.UtcNow;

        /// <summary>
        /// <see cref="ValueStopwatch"/> for measuring uptime.
        /// </summary>
        private static readonly ValueStopwatch _stopwatch = ValueStopwatch.StartNew();

        /// <inheritdoc cref="DateTimeOffset.UtcNow"/>
        public static DateTimeOffset UtcNow => _bootTimeUtc + _stopwatch.Elapsed;

        /// <inheritdoc cref="DateTimeOffset.Now"/>
        public static DateTimeOffset Now => UtcNow.ToLocalTime();
    }
}
