using System.Text.Json;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using BlocksTemplate.Api.Controllers;
using Common.InternalService.Access;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Events;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Functions.DomainService.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The event settings of a function (its trigger events): validation on save, the draft row, the copy a deploy takes for its version, the
    /// cleanup when versions or the function go, and the endpoints on top — scoped to the caller's tenant.
    /// </summary>
    public class FunctionEventSettingsTests
    {
        private const string Tenant = "tenant-1";

        private readonly Mock<IFunctionRepository> _functions = new();
        private readonly Mock<IFunctionRunRepository> _runs = new();
        private readonly Mock<IFunctionEventSettingsRepository> _settings = new();
        private readonly Mock<IFunctionAuditService> _audit = new();
        private readonly IValidator<SaveEventSettingsRequestDto> _validator = new SaveEventSettingsRequestValidator();
        private readonly Mock<global::Workflow.DomainService.Actions.IActionsRepository> _actions = new();

        public FunctionEventSettingsTests()
        {
            // The tenant's actions: every name the tests save, one with its Function switch off.
            _actions.Setup(a => a.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            [
                .. new[] { "order.created", "iam.user.created" }.Concat(Enumerable.Range(0, 21).Select(i => $"event.n{i:00}"))
                    .Select(name => new ActionDefinition { Name = name, Function = true }),
                new ActionDefinition { Name = "iam.user.deactivated", Function = false },
            ]);
            _functions.Setup(f => f.GetByIdAsync(Tenant, "fn-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FunctionEntity { ItemId = "fn-1" });
        }

        private FunctionEventSettingsService Service() =>
            new(_functions.Object, _runs.Object, _settings.Object, _audit.Object, _validator, _actions.Object);

        private static SaveEventSettingsRequestDto Request(string[]? triggers = null) => new()
        {
            FunctionId = "fn-1",
            TriggerEvents = [.. triggers ?? []],
        };

        private static async Task<string> SaveErrorAsync(FunctionEventSettingsService service, SaveEventSettingsRequestDto request)
        {
            var act = () => service.SaveAsync(Tenant, request, "user-1", "u@x");
            return (await act.Should().ThrowAsync<FunctionValidationException>()).Which.Message;
        }

        // ---- validation on save -----------------------------------------------------

        [Fact]
        public async Task A_valid_save_writes_the_draft_and_an_audit_entry()
        {
            await Service().SaveAsync(Tenant, Request(["order.created"]), "user-1", "u@x");

            _settings.Verify(s => s.SaveDraftAsync(
                Tenant, "fn-1",
                It.Is<List<string>>(t => t.SequenceEqual(new[] { "order.created" })),
                "user-1", It.IsAny<CancellationToken>()), Times.Once);
            _audit.Verify(a => a.RecordAsync(
                Tenant, "fn-1", FunctionsConstants.AuditActions.EventsUpdated, "user-1", "u@x", It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Trigger_events_are_limited_to_20_valid_names_without_duplicates()
        {
            var tooMany = Enumerable.Range(0, 21).Select(i => $"event.n{i:00}").ToArray();

            (await SaveErrorAsync(Service(), Request(tooMany))).Should().Contain("at most 20");
            (await SaveErrorAsync(Service(), Request(["order.created", "Bad Name"]))).Should().Contain("triggerEvents[1]");
            (await SaveErrorAsync(Service(), Request(["order.created", "order.created"]))).Should().Contain("triggerEvents[1]").And.Contain("twice");

            var twenty = Enumerable.Range(0, 20).Select(i => $"event.n{i:00}").ToArray();
            await Service().SaveAsync(Tenant, Request(twenty), "user-1", "u@x");
        }

        [Fact]
        public async Task A_name_that_is_not_an_action_of_the_tenant_is_refused_with_ACTION_NOT_FOUND_and_nothing_is_saved()
        {
            var message = await SaveErrorAsync(Service(), Request(["order.created", "order.paid"]));

            message.Should().Be("triggerEvents[1]: ACTION_NOT_FOUND: 'order.paid' is not an action of this project");
            _settings.Verify(s => s.SaveDraftAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task An_action_whose_Function_switch_is_off_can_still_be_kept()
        {
            // The switch is the admin's in OS; turning it off must not block other edits of the list.
            await Service().SaveAsync(Tenant, Request(["iam.user.deactivated"]), "user-1", "u@x");

            _settings.Verify(s => s.SaveDraftAsync(
                Tenant, "fn-1", It.IsAny<List<string>>(), "user-1", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Missing_lists_and_an_empty_function_id_are_refused()
        {
            var request = new SaveEventSettingsRequestDto { FunctionId = "", TriggerEvents = null! };

            await Service().Invoking(s => s.SaveAsync(Tenant, request, null, null)).Should().ThrowAsync<FunctionValidationException>();
        }

        [Fact]
        public async Task A_function_of_another_tenant_is_not_found_on_save_and_on_read()
        {
            var request = Request(null);
            request.FunctionId = "fn-of-another-tenant";

            await Service().Invoking(s => s.SaveAsync(Tenant, request, null, null)).Should().ThrowAsync<FunctionNotFoundException>();
            await Service().Invoking(s => s.GetAsync(Tenant, "fn-of-another-tenant")).Should().ThrowAsync<FunctionNotFoundException>();
            _settings.VerifyNoOtherCalls();
        }

        // ---- reading ------------------------------------------------------------------

        [Fact]
        public async Task Reading_a_function_without_settings_gives_empty_lists()
        {
            var dto = await Service().GetAsync(Tenant, "fn-1");

            dto.FunctionId.Should().Be("fn-1");
            dto.TriggerEvents.Should().BeEmpty();
        }

        [Fact]
        public async Task Reading_gives_the_draft_rows_trigger_events_and_ignores_an_old_events_list()
        {
            _settings.Setup(s => s.GetAsync(Tenant, "fn-1", null, It.IsAny<CancellationToken>())).ReturnsAsync(new FunctionEventSettingsEntity
            {
                FunctionId = "fn-1",
                TriggerEvents = ["order.created"],
            });

            var dto = await Service().GetAsync(Tenant, "fn-1");

            dto.TriggerEvents.Should().Equal("order.created");
        }

        // ---- deploy copies the draft -----------------------------------------------------

        [Fact]
        public async Task Deploy_copies_the_draft_trigger_events_to_the_new_version_before_the_pointer_moves()
        {
            var functions = new Mock<IFunctionRepository>();
            var versions = new Mock<IFunctionVersionRepository>();
            var builds = new Mock<IFunctionBuildService>();
            var validator = new Mock<IValidator<DeployFunctionRequestDto>>();
            var order = new List<string>();
            FunctionVersionEntity? created = null;

            functions.Setup(f => f.GetByIdAsync(Tenant, "fn-1", It.IsAny<CancellationToken>())).ReturnsAsync(new FunctionEntity { ItemId = "fn-1" });
            functions.Setup(f => f.SetActiveVersionAsync(Tenant, "fn-1", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("pointer")).Returns(Task.CompletedTask);
            builds.Setup(b => b.EnsureImageAsync(Tenant, It.IsAny<FunctionEntity>(), It.IsAny<CancellationToken>(), It.IsAny<int?>(), It.IsAny<bool>()))
                .ReturnsAsync(new FunctionBuildEntity { ItemId = "b-1", Status = BuildStatus.Succeeded, ImageDigest = "registry/fn@sha256:new" });
            versions.Setup(v => v.CreateAsync(Tenant, It.IsAny<FunctionVersionEntity>(), It.IsAny<CancellationToken>()))
                .Callback((string _, FunctionVersionEntity v, CancellationToken _) => { created = v; order.Add("version"); }).Returns(Task.CompletedTask);
            validator.Setup(v => v.ValidateAsync(It.IsAny<DeployFunctionRequestDto>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
            _settings.Setup(s => s.CopyDraftToVersionAsync(Tenant, "fn-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("copy")).Returns(Task.CompletedTask);

            var (database, _) = FakeRedisDatabase.Create();
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(database);
            var service = new FunctionDeploymentService(
                functions.Object, versions.Object, builds.Object, new Mock<IFunctionVersionRetentionService>().Object,
                new Mock<IFunctionImagePinService>().Object, new Mock<IFunctionAuditService>().Object, validator.Object,
                cache.Object, new ConfigurationBuilder().Build(), NullLogger<FunctionDeploymentService>.Instance,
                _settings.Object);

            await service.DeployAsync(Tenant, new DeployFunctionRequestDto { FunctionId = "fn-1" }, "user-1", "u@x");

            order.Should().Equal("version", "copy", "pointer");
            _settings.Verify(s => s.CopyDraftToVersionAsync(Tenant, "fn-1", created!.ItemId, It.IsAny<CancellationToken>()), Times.Once);
        }

        // ---- cleanup -----------------------------------------------------------------------

        [Fact]
        public async Task Pruned_versions_lose_their_event_settings_rows()
        {
            var versions = new Mock<IFunctionVersionRepository>();
            versions.Setup(v => v.GetAllForFunctionAsync(Tenant, "fn-1", It.IsAny<CancellationToken>())).ReturnsAsync(
            [
                new FunctionVersionEntity { ItemId = "v3", FunctionId = "fn-1", Number = 3 },
                new FunctionVersionEntity { ItemId = "v2", FunctionId = "fn-1", Number = 2 },
                new FunctionVersionEntity { ItemId = "v1", FunctionId = "fn-1", Number = 1 },
            ]);
            versions.Setup(v => v.DeleteManyAsync(Tenant, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
            _runs.Setup(r => r.GetVersionIdsWithActiveRunsAsync(Tenant, "fn-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HashSet<string>() as IReadOnlySet<string>);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Functions:MaxVersionsPerFunction"] = "2" }).Build();
            var service = new FunctionVersionRetentionService(
                versions.Object, _runs.Object, new Mock<IFunctionImagePinService>().Object, configuration,
                NullLogger<FunctionVersionRetentionService>.Instance, _settings.Object);

            var pruned = await service.PruneAsync(Tenant, "fn-1", "v3");

            pruned.Should().Be(1);
            _settings.Verify(s => s.DeleteVersionsAsync(
                Tenant, It.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { "v1" })), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Pruning_still_succeeds_when_the_event_settings_rows_cannot_be_deleted()
        {
            var versions = new Mock<IFunctionVersionRepository>();
            versions.Setup(v => v.GetAllForFunctionAsync(Tenant, "fn-1", It.IsAny<CancellationToken>())).ReturnsAsync(
            [
                new FunctionVersionEntity { ItemId = "v2", FunctionId = "fn-1", Number = 2 },
                new FunctionVersionEntity { ItemId = "v1", FunctionId = "fn-1", Number = 1 },
            ]);
            versions.Setup(v => v.DeleteManyAsync(Tenant, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
            _runs.Setup(r => r.GetVersionIdsWithActiveRunsAsync(Tenant, "fn-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HashSet<string>() as IReadOnlySet<string>);
            _settings.Setup(s => s.DeleteVersionsAsync(Tenant, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("mongo down"));
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Functions:MaxVersionsPerFunction"] = "1" }).Build();
            var service = new FunctionVersionRetentionService(
                versions.Object, _runs.Object, new Mock<IFunctionImagePinService>().Object, configuration,
                NullLogger<FunctionVersionRetentionService>.Instance, _settings.Object);

            (await service.PruneAsync(Tenant, "fn-1", "v2")).Should().Be(1);
        }

        [Fact]
        public async Task Purging_a_function_removes_its_event_settings_rows()
        {
            var versions = new Mock<IFunctionVersionRepository>();
            var builds = new Mock<IFunctionBuildRepository>();
            var logs = new Mock<IFunctionRunLogRepository>();
            var pins = new Mock<IFunctionImagePinService>();
            versions.Setup(v => v.GetAllForFunctionAsync(Tenant, "fn-1", It.IsAny<CancellationToken>())).ReturnsAsync([]);
            builds.Setup(b => b.GetAllForFunctionAsync(Tenant, "fn-1", It.IsAny<CancellationToken>())).ReturnsAsync([]);
            _runs.Setup(r => r.GetAllAsync(Tenant, It.IsAny<FunctionRunFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(((IReadOnlyList<FunctionRunEntity>)[], 0L));
            var (database, _) = FakeRedisDatabase.Create();
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(database);
            var service = new FunctionPurgeService(
                versions.Object, builds.Object, _runs.Object, logs.Object, pins.Object, cache.Object,
                NullLogger<FunctionPurgeService>.Instance, _settings.Object);

            await service.PurgeAsync(Tenant, "fn-1");

            _settings.Verify(s => s.DeleteAllForFunctionAsync(Tenant, "fn-1", It.IsAny<CancellationToken>()), Times.Once);
        }

        // ---- the endpoints --------------------------------------------------------------

        private FunctionsController Controller()
        {
            var controller = new FunctionsController(
                new Mock<IFunctionService>().Object,
                new Mock<IFunctionDeploymentService>().Object,
                new Mock<IFunctionInvocationService>().Object,
                new Mock<IFunctionRunService>().Object,
                new Mock<IFunctionBuildService>().Object,
                new Mock<IFunctionAuditService>().Object,
                new Mock<IEndpointAccessAuthorizer>().Object,
                new Mock<IFunctionPollTokenService>().Object,
                NullLogger<FunctionsController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
            BlocksContext.SetContext(
                BlocksContext.Create(Tenant, [], "user-1", true, "", "", DateTime.MinValue, "u@x", [], "", "", "", "", Tenant), true);
            return controller;
        }

        [Fact]
        public async Task The_endpoints_read_and_write_inside_the_callers_tenant()
        {
            var controller = Controller();
            var service = Service();
            try
            {
                var settings = await controller.GetEventSettings("fn-1", service);
                var saved = await controller.SaveEventSettings(Request(["order.created"]), service);

                settings.FunctionId.Should().Be("fn-1");
                saved.IsSuccess.Should().BeTrue();
                _functions.Verify(f => f.GetByIdAsync(Tenant, "fn-1", It.IsAny<CancellationToken>()), Times.Exactly(2));
                _settings.Verify(s => s.SaveDraftAsync(Tenant, "fn-1", It.IsAny<List<string>>(), "user-1", It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                BlocksContext.SetContext(null, false);
            }
        }

        [Fact]
        public async Task A_function_of_another_tenant_is_a_not_found_through_the_endpoints()
        {
            var controller = Controller();
            var service = Service();
            try
            {
                await controller.Invoking(c => c.GetEventSettings("fn-9", service)).Should().ThrowAsync<FunctionNotFoundException>();
                await controller.Invoking(c => c.SaveEventSettings(new SaveEventSettingsRequestDto { FunctionId = "fn-9" }, service))
                    .Should().ThrowAsync<FunctionNotFoundException>();
            }
            finally
            {
                BlocksContext.SetContext(null, false);
            }
        }

        // ---- the queue status -----------------------------------------------------------

        [Fact]
        public async Task The_queue_status_counts_http_and_event_runs_apart_with_the_oldest_event_time_in_utc()
        {
            _runs.Setup(r => r.GetQueueCountsAsync(Tenant, "fn-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FunctionQueueCounts(3, 2, new DateTime(2026, 10, 23, 8, 30, 0, DateTimeKind.Unspecified)));

            var status = await Service().GetQueueStatusAsync(Tenant, "fn-1");

            status.HttpQueued.Should().Be(3);
            status.EventQueued.Should().Be(2);
            status.OldestEventQueuedAt.Should().Be("2026-10-23T08:30:00.0000000Z");
        }

        [Fact]
        public async Task With_no_waiting_event_run_the_oldest_time_is_null()
        {
            _runs.Setup(r => r.GetQueueCountsAsync(Tenant, "fn-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FunctionQueueCounts(0, 0, null));

            var status = await Service().GetQueueStatusAsync(Tenant, "fn-1");

            (status.HttpQueued, status.EventQueued, status.OldestEventQueuedAt).Should().Be((0L, 0L, (string?)null));
        }

        [Fact]
        public void The_queue_status_json_has_exactly_the_contract_field_names()
        {
            var json = System.Text.Json.JsonSerializer.Serialize(
                new QueueStatusDto { HttpQueued = 1, EventQueued = 2, OldestEventQueuedAt = null },
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

            json.Should().Be("""{"httpQueued":1,"eventQueued":2,"oldestEventQueuedAt":null}""");
        }

        [Fact]
        public async Task The_queue_status_of_a_function_of_another_tenant_is_a_not_found_and_reads_no_counts()
        {
            var controller = Controller();
            try
            {
                await controller.Invoking(c => c.GetQueueStatus("fn-9", Service())).Should().ThrowAsync<FunctionNotFoundException>();
                _runs.Verify(r => r.GetQueueCountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

                _runs.Setup(r => r.GetQueueCountsAsync(Tenant, "fn-1", It.IsAny<CancellationToken>())).ReturnsAsync(new FunctionQueueCounts(1, 0, null));
                var status = await controller.GetQueueStatus("fn-1", Service());
                status.Should().NotBeNull();
                _runs.Verify(r => r.GetQueueCountsAsync(Tenant, "fn-1", It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                BlocksContext.SetContext(null, false);
            }
        }

        [Theory]
        [InlineData("GetEventSettings", "blocks-logic::function::read")]
        [InlineData("GetQueueStatus", "blocks-logic::function::read")]
        [InlineData("SaveEventSettings", "blocks-logic::function::manage")]
        public void Each_endpoint_is_protected_with_the_read_or_manage_permission(string action, string permission)
        {
            var method = typeof(FunctionsController).GetMethod(action)!;

            var attribute = method.GetCustomAttributes(typeof(ProtectedEndPointAttribute), true).Should().ContainSingle().Subject;
            ((ProtectedEndPointAttribute)attribute).ResourceName.Should().Be(permission);
        }
    }
}
