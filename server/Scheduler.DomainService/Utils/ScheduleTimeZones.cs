using Cronos;

namespace Scheduler.DomainService.Utils
{
    /// <summary>
    /// The schedule's optional IANA time zone. Stored null means UTC, so new and existing UTC schedules are
    /// indistinguishable. Only IANA ids are accepted; Windows ids are refused. On Alpine this needs the
    /// <c>tzdata</c> package (see the Dockerfiles).
    /// </summary>
    public static class ScheduleTimeZones
    {
        public const string InvalidMessage = "Time zone is not recognised";

        private const int MaxLength = 64;

        /// <summary>
        /// null, whitespace, "UTC" or "Etc/UTC" (any case) → stored null. Otherwise the id must be a known IANA
        /// zone of at most 64 characters → stored as given.
        /// </summary>
        public static bool TryNormalize(string? input, out string? stored)
        {
            stored = null;
            if (string.IsNullOrWhiteSpace(input) || IsUtcAlias(input))
            {
                return true;
            }

            if (input.Length > MaxLength
                || !TimeZoneInfo.TryFindSystemTimeZoneById(input, out _)
                // An IANA id converts to a Windows one; a Windows id does not, which is how it is refused.
                || !TimeZoneInfo.TryConvertIanaIdToWindowsId(input, out _))
            {
                return false;
            }

            stored = input;
            return true;
        }

        /// <summary>null or empty → UTC; otherwise the system zone with that id.</summary>
        public static bool TryResolve(string? stored, out TimeZoneInfo timeZone)
        {
            if (string.IsNullOrEmpty(stored))
            {
                timeZone = TimeZoneInfo.Utc;
                return true;
            }

            if (TimeZoneInfo.TryFindSystemTimeZoneById(stored, out var found))
            {
                timeZone = found;
                return true;
            }

            timeZone = TimeZoneInfo.Utc;
            return false;
        }

        /// <summary>As <see cref="TryResolve"/>, throwing <see cref="TimeZoneNotFoundException"/> when it fails.</summary>
        public static TimeZoneInfo Resolve(string? stored) =>
            TryResolve(stored, out var timeZone)
                ? timeZone
                : throw new TimeZoneNotFoundException($"Time zone '{stored}' was not found.");

        /// <summary>
        /// The next fire after <paramref name="fromUtc"/> (exclusive) in the schedule's zone, no earlier than
        /// <paramref name="startDate"/> and no later than <paramref name="endDate"/>. Null when there is none or
        /// the cron or zone cannot be read. Never throws.
        /// </summary>
        public static DateTime? NextOccurrenceUtc(string cron, string? timeZone, DateTime fromUtc, DateTime? startDate, DateTime? endDate)
        {
            if (!TryResolve(timeZone, out var zone) || !TryParseCron(cron, out var expression))
            {
                return null;
            }

            var from = AsUtc(fromUtc);
            if (startDate is { } start && AsUtc(start) > from)
            {
                from = AsUtc(start);
            }

            var next = expression.GetNextOccurrence(from, zone);
            if (next is null || (endDate is { } end && next > AsUtc(end)))
            {
                return null;
            }

            return next;
        }

        private static bool IsUtcAlias(string input) =>
            input.Trim().Equals("UTC", StringComparison.OrdinalIgnoreCase)
            || input.Trim().Equals("Etc/UTC", StringComparison.OrdinalIgnoreCase);

        private static bool TryParseCron(string cron, out CronExpression expression)
        {
            expression = null!;
            if (string.IsNullOrWhiteSpace(cron))
            {
                return false;
            }

            if (CronExpression.TryParse(cron, CronFormat.Standard, out var standard))
            {
                expression = standard;
                return true;
            }

            if (CronExpression.TryParse(cron, CronFormat.IncludeSeconds, out var withSeconds))
            {
                expression = withSeconds;
                return true;
            }

            return false;
        }

        private static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
