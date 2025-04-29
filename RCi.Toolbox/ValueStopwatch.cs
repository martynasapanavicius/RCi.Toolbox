using System;
using System.Diagnostics;

namespace RCi.Toolbox
{
    public readonly struct ValueStopwatch
    {
        private readonly long _startTimestamp;

        private ValueStopwatch(long startTimestamp) => _startTimestamp = startTimestamp;

        /// <see cref="Stopwatch.StartNew"/>
        public static ValueStopwatch StartNew() => new(Stopwatch.GetTimestamp());

        /// <see cref="Stopwatch.Elapsed"/>
        public TimeSpan Elapsed =>
            _startTimestamp == 0L
                ? throw new InvalidOperationException("uninitialized")
                : Stopwatch.GetElapsedTime(_startTimestamp, Stopwatch.GetTimestamp());
    }
}
