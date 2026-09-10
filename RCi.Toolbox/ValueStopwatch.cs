using System;
using System.Diagnostics;

namespace RCi.Toolbox
{
    public readonly struct ValueStopwatch
    {
        private readonly long _startTimestamp;
        private readonly TimeProvider? _timeProvider;

        // IMPROVEMENT: Allows callers to verify if the struct has been initialized without throwing an exception.
        public bool IsActive => _startTimestamp != 0L;

        private ValueStopwatch(long startTimestamp, TimeProvider? timeProvider = null)
        {
            _startTimestamp = startTimestamp;
            _timeProvider = timeProvider;
        }

        /// <see cref="Stopwatch.StartNew"/>
        public static ValueStopwatch StartNew()
        {
            var ts = Stopwatch.GetTimestamp();
            if (ts == 0L)
            {
                // Guarantee non-zero timestamp so 0 remains the uninitialized sentinel
                ts = 1L;
            }
            return new ValueStopwatch(ts);
        }

        public static ValueStopwatch StartNew(TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            var ts = timeProvider.GetTimestamp();
            if (ts == 0L)
            {
                // Guarantee non-zero timestamp so 0 remains the uninitialized sentinel
                ts = 1L;
            }
            return new ValueStopwatch(ts, timeProvider);
        }

        /// <see cref="Stopwatch.Elapsed"/>
        public TimeSpan Elapsed =>
            _startTimestamp == 0L
                ? throw new InvalidOperationException("uninitialized")
                : _timeProvider?.GetElapsedTime(_startTimestamp)
                    ?? Stopwatch.GetElapsedTime(_startTimestamp, Stopwatch.GetTimestamp());
    }
}
