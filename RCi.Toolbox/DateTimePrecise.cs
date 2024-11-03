using System;

namespace RCi.Toolbox
{
    /// <summary>
    /// Precise routines for <see cref="DateTime"/>.
    /// </summary>
    public static class DateTimePrecise
    {
        /// <inheritdoc cref="DateTime.UtcNow"/>
        public static DateTime UtcNow => DateTimeOffsetPrecise.UtcNow.UtcDateTime;

        /// <inheritdoc cref="DateTime.Now"/>
        public static DateTime Now => DateTimeOffsetPrecise.UtcNow.LocalDateTime;
    }
}
