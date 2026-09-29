using FluentAssertions;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Entities;

namespace XUnitTest.Functions
{
    /// <summary>
    /// Run counters live in their own document, and this is the reason: Save and Update replace
    /// the whole function document from a copy loaded earlier, so a counter stored there is
    /// silently overwritten by any run that happened in between. These tests pin the separation
    /// rather than the arithmetic — the arithmetic is Mongo's `$inc`.
    /// </summary>
    public class FunctionRunCountersTests
    {
        [Fact]
        public void The_function_document_carries_no_run_counters()
        {
            var names = typeof(FunctionEntity).GetProperties().Select(p => p.Name).ToList();

            names.Should().NotContain("TotalRuns");
            names.Should().NotContain("LastRunAt");
        }

        [Fact]
        public void Counters_come_from_the_stats_document()
        {
            var function = new FunctionEntity { ItemId = "fn_1", Name = "f" };
            var stats = new FunctionRunStatsEntity
            {
                ItemId = "fn_1",
                TotalRuns = 42,
                LastRunAt = new DateTime(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc),
            };

            var dto = FunctionSummaryDto.From(function, activeVersionNumber: 3, stats);

            dto.TotalRuns.Should().Be(42);
            dto.LastRunAt.Should().Be(stats.LastRunAt);
        }

        [Fact]
        public void A_function_that_has_never_run_reports_zero_not_null()
        {
            // No stats document exists until the first run, and the list still has to render.
            var dto = FunctionSummaryDto.From(
                new FunctionEntity { ItemId = "fn_1", Name = "f" }, activeVersionNumber: null, stats: null);

            dto.TotalRuns.Should().Be(0);
            dto.LastRunAt.Should().BeNull();
        }
    }
}
