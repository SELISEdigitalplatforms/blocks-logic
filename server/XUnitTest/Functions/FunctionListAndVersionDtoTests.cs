using FluentAssertions;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using Functions.DomainService.Services;
using Functions.DomainService.Repositories;
using Functions.DomainService.Utils;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The columns the Functions UI specifies (FEATURES-AND-UI §4.1, §4.8, §4.9): "Invoked by" and
    /// "Runs 24 h" on the list, "Peak mem" on a run row, "Packages" and "Runs" on a version.
    /// </summary>
    public class FunctionListAndVersionDtoTests
    {
        [Fact]
        public void A_function_row_carries_its_trigger_and_recent_run_count()
        {
            var function = new FunctionEntity
            {
                ItemId = "fn_1",
                Name = "Reconcile Payouts",
                Trigger = new TriggerConfig { HttpEnabled = true, WorkflowEnabled = true },
            };

            var dto = FunctionSummaryDto.From(
                function, activeVersionNumber: 17,
                stats: new FunctionRunStatsEntity { TotalRuns = 9_000 }, runs24h: 1_204);

            dto.HttpEnabled.Should().BeTrue();
            dto.WorkflowEnabled.Should().BeTrue();
            dto.Runs24h.Should().Be(1_204);
            dto.TotalRuns.Should().Be(9_000);
            dto.ActiveVersionNumber.Should().Be(17);
        }

        [Fact]
        public void Runs_24h_defaults_to_zero_when_nothing_ran()
        {
            var dto = FunctionSummaryDto.From(new FunctionEntity { ItemId = "fn_1" }, null, null);

            dto.Runs24h.Should().Be(0);
            dto.TotalRuns.Should().Be(0);
        }

        [Fact]
        public void A_version_row_carries_its_packages_and_run_count()
        {
            var version = new FunctionVersionEntity
            {
                ItemId = "v_1",
                Number = 17,
                Packages = "zod 3.23.8 · dayjs 1.11.13",
            };

            var dto = FunctionVersionSummaryDto.From(version, runCount: 42);

            dto.Packages.Should().Be("zod 3.23.8 · dayjs 1.11.13");
            dto.RunCount.Should().Be(42);
            dto.Number.Should().Be(17);
        }

        [Fact]
        public void A_run_row_carries_peak_memory()
        {
            var run = new FunctionRunEntity
            {
                ItemId = "run_1",
                Status = RunStatus.Succeeded,
                PeakMemoryBytes = 86_000_000,
                DurationMs = 684,
            };

            var dto = RunSummaryDto.From(run);

            dto.PeakMemoryBytes.Should().Be(86_000_000);
            dto.DurationMs.Should().Be(684);
        }

        [Theory]
        [InlineData(FunctionStarterTemplates.Minimal, "return { received: input }")]
        [InlineData(FunctionStarterTemplates.HttpEcho, "isAuthenticated")]
        [InlineData(FunctionStarterTemplates.FetchTransform, "await fetch(")]
        public void Each_starter_template_seeds_its_own_handler(string template, string marker)
        {
            var source = FunctionStarterTemplates.For(template);

            source.IndexJs.Should().Contain(marker);
            source.PackageJson.Should().Contain("\"type\": \"module\"");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("something-else")]
        public void An_unknown_template_falls_back_to_the_minimal_handler(string? template)
        {
            FunctionStarterTemplates.For(template).IndexJs
                .Should().Be(FunctionStarterTemplates.For(FunctionStarterTemplates.Minimal).IndexJs);
        }

        [Fact]
        public async Task The_runs_filter_passes_the_trigger_and_id_search_through()
        {
            var repository = new Mock<IFunctionRunRepository>();
            FunctionRunFilter? captured = null;
            repository
                .Setup(r => r.GetAllAsync(
                    It.IsAny<string>(),
                    It.IsAny<FunctionRunFilter>(),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Callback<string, FunctionRunFilter, int, int, CancellationToken>(
                    (_, filter, _, _, _) => captured = filter)
                .ReturnsAsync((Array.Empty<FunctionRunEntity>(), 0L));

            var service = RunService(repository);

            await service.GetAllAsync("tenant", new GetRunsRequestDto
            {
                FunctionId = "fn_1",
                Status = "Succeeded",
                InvokedBy = "workflow",
                SearchKey = "run_7f21",
            });

            captured.Should().NotBeNull();
            captured!.FunctionId.Should().Be("fn_1");
            captured.Status.Should().Be(RunStatus.Succeeded);
            captured.InvokedBy.Should().Be(InvokedByType.Workflow);
            captured.IdPrefix.Should().Be("run_7f21");
        }

        [Fact]
        public async Task An_unparseable_trigger_filter_is_ignored_rather_than_failing_the_list()
        {
            var repository = new Mock<IFunctionRunRepository>();
            FunctionRunFilter? captured = null;
            repository
                .Setup(r => r.GetAllAsync(
                    It.IsAny<string>(),
                    It.IsAny<FunctionRunFilter>(),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Callback<string, FunctionRunFilter, int, int, CancellationToken>(
                    (_, filter, _, _, _) => captured = filter)
                .ReturnsAsync((Array.Empty<FunctionRunEntity>(), 0L));

            await RunService(repository).GetAllAsync("tenant", new GetRunsRequestDto
            {
                Status = "not-a-status",
                InvokedBy = "not-a-trigger",
            });

            captured.Should().NotBeNull();
            captured!.Status.Should().BeNull();
            captured.InvokedBy.Should().BeNull();
        }

        [Theory]
        [InlineData("Name", "{ \"Name\" : 1 }")]
        [InlineData("name", "{ \"Name\" : 1 }")]
        [InlineData("Updated", "{ \"LastUpdatedDate\" : -1 }")]
        [InlineData(null, "{ \"LastUpdatedDate\" : -1 }")]
        [InlineData("something-else", "{ \"LastUpdatedDate\" : -1 }")]
        public void The_list_sort_maps_to_the_field_the_UI_offers(string? sortBy, string expected)
        {
            var serializer = BsonSerializer.SerializerRegistry.GetSerializer<FunctionEntity>();
            var rendered = FunctionRepository
                .SortFor(sortBy)
                .Render(new RenderArgs<FunctionEntity>(serializer, BsonSerializer.SerializerRegistry));

            rendered.ToString().Should().Be(expected);
        }

        private static IFunctionRunService RunService(
            Mock<IFunctionRunRepository> repository) =>
            new FunctionRunService(
                repository.Object,
                new Mock<IFunctionRunLogRepository>().Object,
                new Mock<IFunctionAuditService>().Object,
                new Mock<IFunctionInvocationService>().Object,
                new Mock<ICacheClient>().Object,
                new Mock<ILogger<FunctionRunService>>().Object);
    }
}
