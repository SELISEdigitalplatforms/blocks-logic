using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Scheduler.DomainService.Dtos.Requests;
using Scheduler.DomainService.Repositories;
using Scheduler.DomainService.Services;

namespace XUnitTest.Scheduler
{
    /// <summary><see cref="ScheduleExecutionQueryService"/>: the schedule guard, filters, paging and row mapping (§5.5).</summary>
    public class ScheduleExecutionQueryServiceTests
    {
        private readonly Mock<IScheduleRepository> _schedules = new();
        private readonly Mock<IScheduleExecutionRepository> _executions = new();
        private readonly Mock<ILogger<ScheduleExecutionQueryService>> _logger = new();
        private readonly ScheduleExecutionQueryService _service;
        private ScheduleExecutionFilter? _pageFilter;
        private ScheduleExecutionFilter? _countFilter;
        private (int PageNumber, int PageSize)? _paging;

        private static readonly DateTime Fired = new(2026, 10, 26, 10, 5, 0, 120, DateTimeKind.Utc);

        public ScheduleExecutionQueryServiceTests()
        {
            _service = new ScheduleExecutionQueryService(_schedules.Object, _executions.Object, _logger.Object);
            SetupSchedule(ScheduleKind.Application);
            _executions.Setup(r => r.CountAsync(It.IsAny<ScheduleExecutionFilter>()))
                .Callback<ScheduleExecutionFilter>(f => _countFilter = f)
                .ReturnsAsync(1234);
            _executions.Setup(r => r.GetPageAsync(It.IsAny<ScheduleExecutionFilter>(), It.IsAny<int>(), It.IsAny<int>()))
                .Callback<ScheduleExecutionFilter, int, int>((f, n, s) => { _pageFilter = f; _paging = (n, s); })
                .ReturnsAsync([Row()]);
        }

        private void SetupSchedule(ScheduleKind? kind) =>
            _schedules.Setup(r => r.GetByIdAsync("s1", It.IsAny<string>()))
                .ReturnsAsync(kind is null ? null : new Schedule { ItemId = "s1", Kind = kind.Value });

        private static ScheduleExecution Row() => Row(200, "{\"ok\":true}");

        private static ScheduleExecution Row(object? status, object? body = null) => new()
        {
            ItemId = "e1",
            ScheduleId = "s1",
            TenantId = "t1",
            ScheduleName = "Nightly sync",
            FiredUtc = Fired,
            FinishedUtc = Fired.AddMilliseconds(340),
            Response = status is null ? null : body is null
                ? new Dictionary<string, object> { ["Status"] = status }
                : new Dictionary<string, object> { ["Status"] = status, ["Body"] = body },
            Snapshot = "{\"itemId\":\"s1\"}",
        };

        private static GetScheduleExecutionsRequestDto List(string? outcome = null, int page = 0, int size = 10, DateTime? start = null, DateTime? end = null) =>
            new() { ScheduleId = "s1", Outcome = outcome, PageNumber = page, PageSize = size, StartDate = start, EndDate = end };

        // ---------- Schedule guard ----------

        public static TheoryData<ScheduleKind?> NotFoundKinds => new() { null, ScheduleKind.Internal };

        [Theory]
        [MemberData(nameof(NotFoundKinds))]
        public async Task List_MissingOrInternalSchedule_IsNotFound_AndQueriesNothing(ScheduleKind? kind)
        {
            SetupSchedule(kind);

            var result = await _service.GetScheduleExecutionsAsync(List());

            result.IsSuccess.Should().BeFalse();
            result.Data.Should().BeNull();
            result.Errors.Should().Equal(new Dictionary<string, string> { ["not_found"] = "Schedule not found" });
            _executions.VerifyNoOtherCalls();
        }

        [Theory]
        [MemberData(nameof(NotFoundKinds))]
        public async Task Detail_MissingOrInternalSchedule_IsNotFound(ScheduleKind? kind)
        {
            SetupSchedule(kind);

            var result = await _service.GetScheduleExecutionAsync(new GetScheduleExecutionRequestDto { ScheduleId = "s1", ExecutionId = "e1" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Equal(new Dictionary<string, string> { ["not_found"] = "Schedule not found" });
            _executions.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Detail_UnknownExecution_IsExecutionNotFound()
        {
            var result = await _service.GetScheduleExecutionAsync(new GetScheduleExecutionRequestDto { ScheduleId = "s1", ExecutionId = "nope" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Equal(new Dictionary<string, string> { ["not_found"] = "Execution not found" });
        }

        [Fact]
        public async Task List_UnexpectedException_IsSomethingWentWrong()
        {
            _executions.Setup(r => r.CountAsync(It.IsAny<ScheduleExecutionFilter>())).ThrowsAsync(new TimeoutException());

            var result = await _service.GetScheduleExecutionsAsync(List());

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Equal(new Dictionary<string, string> { ["Message"] = "Something went wrong" });
        }

        // ---------- Paging and filters ----------

        [Fact]
        public async Task List_PassesPaging_AndCountsWithTheSameFilter()
        {
            var result = await _service.GetScheduleExecutionsAsync(List(page: 3, size: 50));

            result.IsSuccess.Should().BeTrue();
            result.TotalCount.Should().Be(1234);
            result.Data.Should().ContainSingle();
            _paging.Should().Be((3, 50));
            _pageFilter.Should().Be(_countFilter);
            _pageFilter!.ScheduleId.Should().Be("s1");
            _pageFilter.HasOutcome.Should().BeFalse();
        }

        [Fact]
        public async Task List_PagePastTheEnd_ReturnsEmptyData_WithTheRealTotal()
        {
            _executions.Setup(r => r.GetPageAsync(It.IsAny<ScheduleExecutionFilter>(), It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync([]);

            var result = await _service.GetScheduleExecutionsAsync(List(page: 10_000, size: 50));

            result.IsSuccess.Should().BeTrue();
            result.Data.Should().BeEmpty();
            result.TotalCount.Should().Be(1234);
        }

        [Fact]
        public async Task List_Succeeded_IsEvery2xxCode()
        {
            await _service.GetScheduleExecutionsAsync(List(outcome: "Succeeded"));

            _pageFilter!.Statuses.Should().Equal(Enumerable.Range(200, 100));
            _pageFilter.NonSuccessRange.Should().BeFalse();
        }

        [Fact]
        public async Task List_Failed_IsTheStoredNon2xxCodes()
        {
            _executions.Setup(r => r.GetDistinctStatusesAsync("s1")).ReturnsAsync([200, 204, 502, 500, 422]);

            await _service.GetScheduleExecutionsAsync(List(outcome: "Failed"));

            _pageFilter!.Statuses.Should().Equal(422, 500, 502);
            _countFilter.Should().Be(_pageFilter);
        }

        [Fact]
        public async Task List_Failed_NeverFailed_IsEmpty_WithoutQueryingRows()
        {
            _executions.Setup(r => r.GetDistinctStatusesAsync("s1")).ReturnsAsync([200, 201]);

            var result = await _service.GetScheduleExecutionsAsync(List(outcome: "Failed"));

            result.IsSuccess.Should().BeTrue();
            result.Data.Should().BeEmpty();
            result.TotalCount.Should().Be(0);
            _executions.Verify(r => r.GetPageAsync(It.IsAny<ScheduleExecutionFilter>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
            _executions.Verify(r => r.CountAsync(It.IsAny<ScheduleExecutionFilter>()), Times.Never);
        }

        [Fact]
        public async Task List_Failed_MoreThan150Codes_FallsBackToTheRange_AndLogsOnce()
        {
            _executions.Setup(r => r.GetDistinctStatusesAsync("s1")).ReturnsAsync(Enumerable.Range(300, 151).ToList());

            await _service.GetScheduleExecutionsAsync(List(outcome: "Failed"));
            await _service.GetScheduleExecutionsAsync(List(outcome: "Failed"));

            _pageFilter!.Statuses.Should().BeNull();
            _pageFilter.NonSuccessRange.Should().BeTrue();
            _logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }

        [Fact]
        public async Task List_Failed_Exactly150Codes_StaysAnInList()
        {
            _executions.Setup(r => r.GetDistinctStatusesAsync("s1")).ReturnsAsync(Enumerable.Range(300, 150).ToList());

            await _service.GetScheduleExecutionsAsync(List(outcome: "Failed"));

            _pageFilter!.Statuses.Should().HaveCount(150);
        }

        [Fact]
        public async Task List_DateRange_PassesBothBounds_AsUtc()
        {
            var start = new DateTime(2026, 10, 19, 18, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2026, 10, 26, 18, 0, 0, DateTimeKind.Utc);

            await _service.GetScheduleExecutionsAsync(List(start: start, end: end));

            _pageFilter!.StartUtc.Should().Be(start);
            _pageFilter.EndUtc.Should().Be(end);
            _countFilter.Should().Be(_pageFilter);
        }

        [Fact]
        public async Task List_DateRange_EachBoundAlone()
        {
            var start = new DateTime(2026, 10, 19, 18, 0, 0, DateTimeKind.Utc);

            await _service.GetScheduleExecutionsAsync(List(start: start));
            _pageFilter!.StartUtc.Should().Be(start);
            _pageFilter.EndUtc.Should().BeNull();

            await _service.GetScheduleExecutionsAsync(List(end: start));
            _pageFilter!.StartUtc.Should().BeNull();
            _pageFilter.EndUtc.Should().Be(start);
        }

        [Fact]
        public async Task List_DateRange_NormalisesKindToUtc()
        {
            var unspecified = new DateTime(2026, 10, 19, 18, 0, 0, DateTimeKind.Unspecified);
            var local = new DateTime(2026, 10, 26, 18, 0, 0, DateTimeKind.Local);

            await _service.GetScheduleExecutionsAsync(List(start: unspecified, end: local));

            _pageFilter!.StartUtc.Should().Be(new DateTime(2026, 10, 19, 18, 0, 0, DateTimeKind.Utc));
            _pageFilter.StartUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
            _pageFilter.EndUtc.Should().Be(local.ToUniversalTime());
            _pageFilter.EndUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
        }

        [Fact]
        public async Task List_DateRangeAndOutcome_Combine()
        {
            _executions.Setup(r => r.GetDistinctStatusesAsync("s1")).ReturnsAsync([500]);
            var start = new DateTime(2026, 10, 19, 18, 0, 0, DateTimeKind.Utc);

            await _service.GetScheduleExecutionsAsync(List(outcome: "Failed", start: start));

            _pageFilter.Should().Be(new ScheduleExecutionFilter("s1", _pageFilter!.Statuses, start, null, false));
            _pageFilter.Statuses.Should().Equal(500);
            // The failure set comes from the whole schedule, not the date range.
            _executions.Verify(r => r.GetDistinctStatusesAsync("s1"), Times.Once);
        }

        // ---------- Mapping ----------

        [Fact]
        public async Task List_MapsLightRows()
        {
            var result = await _service.GetScheduleExecutionsAsync(List());

            var item = result.Data!.Single();
            item.Id.Should().Be("e1");
            item.FiredUtc.Should().Be(Fired);
            item.FinishedUtc.Should().Be(Fired.AddMilliseconds(340));
            item.DurationMs.Should().Be(340);
            item.Status.Should().Be(200);
            item.Outcome.Should().Be("Succeeded");
        }

        [Fact]
        public async Task Detail_MapsTheFullRow()
        {
            _executions.Setup(r => r.GetByIdAsync("s1", "e1")).ReturnsAsync(Row(status: 502, body: "Network error: x"));

            var result = await _service.GetScheduleExecutionAsync(new GetScheduleExecutionRequestDto { ScheduleId = "s1", ExecutionId = "e1" });

            result.IsSuccess.Should().BeTrue();
            var dto = result.Data!;
            dto.Id.Should().Be("e1");
            dto.ScheduleId.Should().Be("s1");
            dto.ScheduleName.Should().Be("Nightly sync");
            dto.DurationMs.Should().Be(340);
            dto.Status.Should().Be(502);
            dto.Outcome.Should().Be("Failed");
            dto.Body.Should().Be("Network error: x");
            dto.Snapshot.Should().Be("{\"itemId\":\"s1\"}");
        }

        [Fact]
        public void Mapping_NullAndPartialRows_AreTolerated()
        {
            var partial = new ScheduleExecution { ItemId = "e2", ScheduleId = "s1" };

            var item = ScheduleExecutionQueryService.ToListItem(partial);
            item.FiredUtc.Should().BeNull();
            item.DurationMs.Should().BeNull();
            item.Status.Should().BeNull();
            item.Outcome.Should().Be("Failed");

            var detail = ScheduleExecutionQueryService.ToDetail(partial);
            detail.ScheduleName.Should().BeNull();
            detail.Body.Should().BeNull();
            detail.Snapshot.Should().BeNull();
            detail.Outcome.Should().Be("Failed");

            ScheduleExecutionQueryService.ToDetail(new ScheduleExecution { ItemId = "e3", ScheduleId = "s1", Response = new Dictionary<string, object>() })
                .Status.Should().BeNull();
        }

        [Fact]
        public void Mapping_StatusAndBodyTypes()
        {
            ScheduleExecutionQueryService.ToDetail(Row(status: 204L, body: null)).Status.Should().Be(204);
            ScheduleExecutionQueryService.ToDetail(Row(status: "200")).Status.Should().BeNull();
            ScheduleExecutionQueryService.ToDetail(Row(status: "200")).Outcome.Should().Be("Failed");
            ScheduleExecutionQueryService.ToDetail(Row(status: 200, body: 42)).Body.Should().Be("42");
            ScheduleExecutionQueryService.ToDetail(Row(status: 299)).Outcome.Should().Be("Succeeded");
            ScheduleExecutionQueryService.ToDetail(Row(status: 300)).Outcome.Should().Be("Failed");
            ScheduleExecutionQueryService.ToDetail(Row(status: 199)).Outcome.Should().Be("Failed");
        }
    }
}
