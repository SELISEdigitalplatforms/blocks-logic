using FluentAssertions;
using Scheduler.DomainService.Utils;

namespace XUnitTest.Scheduler
{
    /// <summary><see cref="ScheduleTimeZones"/>: normalising, resolving and next-occurrence rules (SPEC6 §5.2, §5.5).</summary>
    public class ScheduleTimeZonesTests
    {
        private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

        // ---------- TryNormalize ----------

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("UTC")]
        [InlineData("utc")]
        [InlineData("Etc/UTC")]
        [InlineData("etc/utc")]
        public void TryNormalize_UtcAliases_StoreNull(string? input)
        {
            ScheduleTimeZones.TryNormalize(input, out var stored).Should().BeTrue();
            stored.Should().BeNull();
        }

        [Theory]
        [InlineData("Asia/Dhaka")]
        [InlineData("America/New_York")]
        [InlineData("Asia/Kathmandu")]
        public void TryNormalize_IanaId_IsStoredAsGiven(string input)
        {
            ScheduleTimeZones.TryNormalize(input, out var stored).Should().BeTrue();
            stored.Should().Be(input);
        }

        [Theory]
        [InlineData("Mars/Olympus")]
        [InlineData("Bangladesh Standard Time")]
        [InlineData("Eastern Standard Time")]
        public void TryNormalize_UnknownOrWindowsId_IsRefused(string input)
        {
            ScheduleTimeZones.TryNormalize(input, out var stored).Should().BeFalse();
            stored.Should().BeNull();
        }

        [Fact]
        public void TryNormalize_TooLong_IsRefused()
        {
            ScheduleTimeZones.TryNormalize(new string('a', 65), out _).Should().BeFalse();
        }

        // ---------- TryResolve / Resolve ----------

        [Fact]
        public void TryResolve_NullOrEmpty_IsUtc()
        {
            ScheduleTimeZones.TryResolve(null, out var tz).Should().BeTrue();
            tz.Should().Be(TimeZoneInfo.Utc);
            ScheduleTimeZones.TryResolve("", out tz).Should().BeTrue();
            tz.Should().Be(TimeZoneInfo.Utc);
        }

        [Fact]
        public void TryResolve_KnownAndUnknown()
        {
            ScheduleTimeZones.TryResolve("Asia/Dhaka", out var dhaka).Should().BeTrue();
            dhaka.GetUtcOffset(Utc(2026, 10, 26)).Should().Be(TimeSpan.FromHours(6));
            ScheduleTimeZones.TryResolve("Mars/Olympus", out _).Should().BeFalse();
        }

        [Fact]
        public void Resolve_Throws_ForAnUnknownZone()
        {
            ScheduleTimeZones.Resolve(null).Should().Be(TimeZoneInfo.Utc);
            FluentActions.Invoking(() => ScheduleTimeZones.Resolve("Mars/Olympus")).Should().Throw<TimeZoneNotFoundException>();
        }

        // ---------- NextOccurrenceUtc ----------

        [Fact]
        public void NextOccurrence_FiresAtLocalTime_InTheZone()
        {
            ScheduleTimeZones.NextOccurrenceUtc("0 9 * * *", "Asia/Dhaka", Utc(2026, 10, 26, 10, 10), null, null)
                .Should().Be(Utc(2026, 10, 27, 3));
            ScheduleTimeZones.NextOccurrenceUtc("0 9 * * *", null, Utc(2026, 10, 26, 10, 10), null, null)
                .Should().Be(Utc(2026, 10, 27, 9));
        }

        [Fact]
        public void NextOccurrence_StartsNoEarlierThanTheStartDate()
        {
            ScheduleTimeZones.NextOccurrenceUtc("0 9 * * *", "Asia/Dhaka", Utc(2026, 10, 26), Utc(2026, 11, 5), null)
                .Should().Be(Utc(2026, 11, 5, 3));
        }

        [Fact]
        public void NextOccurrence_AfterTheEndDate_IsNull()
        {
            ScheduleTimeZones.NextOccurrenceUtc("0 9 * * *", "Asia/Dhaka", Utc(2026, 10, 26, 10), null, Utc(2026, 10, 27, 2))
                .Should().BeNull();
        }

        [Theory]
        [InlineData("not-a-cron", null)]
        [InlineData("", null)]
        [InlineData("0 9 * * *", "Mars/Olympus")]
        public void NextOccurrence_UnreadableCronOrZone_IsNull(string cron, string? zone)
        {
            ScheduleTimeZones.NextOccurrenceUtc(cron, zone, Utc(2026, 10, 26), null, null).Should().BeNull();
        }

        [Fact]
        public void NextOccurrence_AcceptsSixFieldCrons_AndUnspecifiedKinds()
        {
            ScheduleTimeZones.NextOccurrenceUtc("0 0 9 * * *", null, new DateTime(2026, 10, 26, 10, 0, 0), null, null)
                .Should().Be(Utc(2026, 10, 27, 9));
        }

        [Fact]
        public void NextOccurrence_SpringForward_SkippedTimeFiresOnceAtTheTransition()
        {
            // New York, 2026-03-08: 02:00 jumps to 03:00, so 02:30 does not exist and fires once at 03:00 EDT (07:00Z).
            var first = ScheduleTimeZones.NextOccurrenceUtc("30 2 * * *", "America/New_York", Utc(2026, 3, 8, 5), null, null);
            first.Should().Be(Utc(2026, 3, 8, 7));
            ScheduleTimeZones.NextOccurrenceUtc("30 2 * * *", "America/New_York", first!.Value, null, null)
                .Should().Be(Utc(2026, 3, 9, 6, 30));
        }

        [Fact]
        public void NextOccurrence_FallBack_AmbiguousTimeFiresOnce()
        {
            // New York, 2026-11-01: 01:30 happens twice; it fires once (EDT, 05:30Z), then the next day in EST.
            var first = ScheduleTimeZones.NextOccurrenceUtc("30 1 * * *", "America/New_York", Utc(2026, 11, 1, 4), null, null);
            first.Should().Be(Utc(2026, 11, 1, 5, 30));
            ScheduleTimeZones.NextOccurrenceUtc("30 1 * * *", "America/New_York", first!.Value, null, null)
                .Should().Be(Utc(2026, 11, 2, 6, 30));
        }
    }
}
