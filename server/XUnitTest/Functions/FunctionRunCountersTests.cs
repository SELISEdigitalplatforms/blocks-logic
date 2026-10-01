using FluentAssertions;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Entities;
using Functions.DomainService.Utils;

namespace XUnitTest.Functions
{
    /// <summary>
    /// Run counters are fields of the function document. That is only safe because nothing
    /// writes the document whole any more — every write is a field-level update, and the counters
    /// are written by one atomic <c>$inc</c>/<c>$max</c> (IFunctionRepository.RecordRunStartedAsync).
    /// The arithmetic is Mongo's; these pin where the numbers come from.
    /// </summary>
    public class FunctionRunCountersTests
    {
        [Fact]
        public void Counters_come_from_the_function_itself()
        {
            var function = new FunctionEntity
            {
                ItemId = "fn_1",
                Name = "f",
                TotalRuns = 42,
                LastRunAt = new DateTime(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc),
            };

            var dto = FunctionSummaryDto.From(function, activeVersionNumber: 3);

            dto.TotalRuns.Should().Be(42);
            dto.LastRunAt.Should().Be(function.LastRunAt);
        }

        [Fact]
        public void A_function_that_has_never_run_reports_zero_not_null()
        {
            var dto = FunctionSummaryDto.From(new FunctionEntity { ItemId = "fn_1", Name = "f" }, activeVersionNumber: null);

            dto.TotalRuns.Should().Be(0);
            dto.LastRunAt.Should().BeNull();
        }

        [Fact]
        public void The_retired_collection_keeps_its_name_for_the_carry_over()
        {
            // Renaming it would make the migration look in an empty collection and drop nothing.
            FunctionsConstants.LegacyRunStatsCollection.Should().Be("FunctionRunStats");
        }
    }
}
