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
    /// The events a function may send: validation on save, the draft row, the copy a deploy takes for its version, the
    /// cleanup when versions or the function go, and the endpoints on top — scoped to the caller's tenant.
    /// </summary>
    public class FunctionEventSettingsTests
    {
        private const string Tenant = "tenant-1";

        private readonly Mock<IFunctionRepository> _functions = new();
        private readonly Mock<IFunctionRunRepository> _runs = new();
        private readonly Mock<IFunctionEventSettingsRepository> _settings = new();
        private readonly Mock<IFunctionRunEventsRepository> _runEvents = new();
        private readonly Mock<IFunctionAuditService> _audit = new();
        private readonly IValidator<SaveEventSettingsRequestDto> _validator = new SaveEventSettingsRequestValidator();

        public FunctionEventSettingsTests()
        {
            _functions.Setup(f => f.GetByIdAsync(Tenant, "fn-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FunctionEntity { ItemId = "fn-1" });
        }

        private FunctionEventSettingsService Service() =>
            new(_functions.Object, _runs.Object, _settings.Object, _runEvents.Object, _audit.Object, _validator);

        private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);

        private static SaveEventSettingsRequestDto Request(
            string[]? triggers = null, params (string Type, string Payload)[] events) => new()
        {
            FunctionId = "fn-1",
            Events = [.. events.Select(e => new FunctionEventSettingDto { Type = e.Type, Payload = Json(e.Payload) })],
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
            var template = FunctionEventCatalog.Find("SendMail")!.PayloadTemplateJson;

            await Service().SaveAsync(Tenant, Request(["order.created"], ("SendMail", template), ("FunctionEvent", """{"name":"order.created","data":{}}""")), "user-1", "u@x");

            _settings.Verify(s => s.SaveDraftAsync(
                Tenant, "fn-1",
                It.Is<List<FunctionEventSetting>>(e => e.Select(x => x.Type).SequenceEqual(new[] { "SendMail", "FunctionEvent" })),
                It.Is<List<string>>(t => t.SequenceEqual(new[] { "order.created" })),
                "user-1", It.IsAny<CancellationToken>()), Times.Once);
            _audit.Verify(a => a.RecordAsync(
                Tenant, "fn-1", FunctionsConstants.AuditActions.EventsUpdated, "user-1", "u@x", It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task An_event_type_outside_the_catalog_is_refused_with_its_field()
        {
            var message = await SaveErrorAsync(Service(), Request(null, ("DeleteEverything", "{}")));

            message.Should().Contain("events[0].type").And.Contain("not an event this platform offers");
            _settings.Verify(s => s.SaveDraftAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<FunctionEventSetting>>(), It.IsAny<List<string>>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task The_same_type_twice_is_refused()
        {
            var message = await SaveErrorAsync(Service(), Request(null, ("FunctionEvent", """{"name":"abc"}"""), ("FunctionEvent", """{"name":"abd"}""")));

            message.Should().Contain("events[1].type").And.Contain("listed twice");
        }

        [Fact]
        public async Task A_payload_with_an_unknown_field_or_a_bad_name_is_refused()
        {
            (await SaveErrorAsync(Service(), Request(null, ("SendMail", """{"sendPhoneNumberAsEmail":true}""")))).Should().Contain("events[0].payload").And.Contain("unknown field");
            (await SaveErrorAsync(Service(), Request(null, ("FunctionEvent", """{"name":"NO"}""")))).Should().Contain("events[0].payload");
            (await SaveErrorAsync(Service(), Request(null, ("SendMail", "[]")))).Should().Contain("JSON object");
        }

        [Fact]
        public async Task A_payload_that_is_missing_is_refused_without_a_crash()
        {
            var request = new SaveEventSettingsRequestDto
            {
                FunctionId = "fn-1",
                Events = [new FunctionEventSettingDto { Type = "SendMail" }],
            };

            (await SaveErrorAsync(Service(), request)).Should().Contain("JSON object");
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
        public async Task Missing_lists_and_an_empty_function_id_are_refused()
        {
            var request = new SaveEventSettingsRequestDto { FunctionId = "", Events = null!, TriggerEvents = null! };

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
            dto.Events.Should().BeEmpty();
            dto.TriggerEvents.Should().BeEmpty();
        }

        [Fact]
        public async Task Reading_gives_the_draft_row_with_payloads_as_json_objects()
        {
            _settings.Setup(s => s.GetAsync(Tenant, "fn-1", null, It.IsAny<CancellationToken>())).ReturnsAsync(new FunctionEventSettingsEntity
            {
                FunctionId = "fn-1",
                Events = [new FunctionEventSetting { Type = "FunctionEvent", PayloadTemplateJson = """{"name":"order.created","data":{}}""" }],
                TriggerEvents = ["order.created"],
            });

            var dto = await Service().GetAsync(Tenant, "fn-1");

            dto.Events.Should().ContainSingle().Which.Type.Should().Be("FunctionEvent");
            dto.Events[0].Payload.GetProperty("name").GetString().Should().Be("order.created");
            dto.TriggerEvents.Should().Equal("order.created");
        }

        [Fact]
        public void The_catalog_lists_both_events_with_templates_as_objects()
        {
            var catalog = Service().GetCatalog();

            catalog.Items.Select(i => i.Type).Should().Equal("SendMail", "FunctionEvent");
            catalog.Items.Should().OnlyContain(i => i.PayloadTemplate.ValueKind == JsonValueKind.Object && i.Title.Length > 0);
            catalog.Items.Single(i => i.Type == "SendMail").PublicAllowed.Should().BeTrue();
            catalog.Items.Single(i => i.Type == "FunctionEvent").PublicAllowed.Should().BeFalse();
        }

        // ---- a run's events -------------------------------------------------------------

        [Fact]
        public async Task A_runs_events_come_from_its_newest_attempt_with_the_rejection_reason()
        {
            _runs.Setup(r => r.GetByIdAsync(Tenant, "run-1", It.IsAny<CancellationToken>())).ReturnsAsync(new FunctionRunEntity { ItemId = "run-1" });
            _runEvents.Setup(r => r.GetLatestAsync(Tenant, "run-1", It.IsAny<CancellationToken>())).ReturnsAsync(new FunctionRunEventsEntity
            {
                RunId = "run-1",
                Rejected = "EVENTS_REJECTED: 51 events",
                Events = [new FunctionRunEventEntry { Id = "run-1-1-1", Type = "SendMail", Status = FunctionEventStatus.Sent, Attempts = 1, SentAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }],
            });

            var dto = await Service().GetRunEventsAsync(Tenant, "run-1");

            dto.Rejected.Should().Be("EVENTS_REJECTED: 51 events");
            var item = dto.Items.Should().ContainSingle().Subject;
            item.Status.Should().Be("Sent");
            item.Id.Should().Be("run-1-1-1");
        }

        [Fact]
        public async Task A_run_without_an_events_document_has_no_items()
        {
            _runs.Setup(r => r.GetByIdAsync(Tenant, "run-1", It.IsAny<CancellationToken>())).ReturnsAsync(new FunctionRunEntity { ItemId = "run-1" });

            var dto = await Service().GetRunEventsAsync(Tenant, "run-1");

            dto.Items.Should().BeEmpty();
            dto.Rejected.Should().BeNull();
        }

        [Fact]
        public async Task A_run_of_another_tenant_is_not_found()
        {
            await Service().Invoking(s => s.GetRunEventsAsync(Tenant, "run-elsewhere")).Should().ThrowAsync<FunctionNotFoundException>();
            _runEvents.Verify(r => r.GetLatestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // ---- deploy copies the draft -----------------------------------------------------

        [Fact]
        public async Task Deploy_copies_the_draft_events_to_the_new_version_before_the_pointer_moves()
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
                null, _settings.Object);

            await service.DeployAsync(Tenant, new DeployFunctionRequestDto { FunctionId = "fn-1" }, "user-1", "u@x");

            order.Should().Equal("version", "copy", "pointer");
            _settings.Verify(s => s.CopyDraftToVersionAsync(Tenant, "fn-1", created!.ItemId, It.IsAny<CancellationToken>()), Times.Once);
        }

        // ---- cleanup -----------------------------------------------------------------------

        [Fact]
        public async Task Pruned_versions_lose_their_events_rows()
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
        public async Task Pruning_still_succeeds_when_the_events_rows_cannot_be_deleted()
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
        public async Task Purging_a_function_removes_its_events_rows_and_run_events()
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
                NullLogger<FunctionPurgeService>.Instance, _settings.Object, _runEvents.Object);

            await service.PurgeAsync(Tenant, "fn-1");

            _settings.Verify(s => s.DeleteAllForFunctionAsync(Tenant, "fn-1", It.IsAny<CancellationToken>()), Times.Once);
            _runEvents.Verify(r => r.DeleteAllForFunctionAsync(Tenant, "fn-1", It.IsAny<CancellationToken>()), Times.Once);
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
                var catalog = controller.GetEventCatalog(service);
                var settings = await controller.GetEventSettings("fn-1", service);
                var saved = await controller.SaveEventSettings(Request(["order.created"]), service);

                catalog.Items.Should().HaveCount(2);
                settings.FunctionId.Should().Be("fn-1");
                saved.IsSuccess.Should().BeTrue();
                _functions.Verify(f => f.GetByIdAsync(Tenant, "fn-1", It.IsAny<CancellationToken>()), Times.Exactly(2));
                _settings.Verify(s => s.SaveDraftAsync(Tenant, "fn-1", It.IsAny<List<FunctionEventSetting>>(), It.IsAny<List<string>>(), "user-1", It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                BlocksContext.SetContext(null, false);
            }
        }

        [Fact]
        public async Task A_function_or_run_of_another_tenant_is_a_not_found_through_the_endpoints()
        {
            var controller = Controller();
            var service = Service();
            try
            {
                await controller.Invoking(c => c.GetEventSettings("fn-9", service)).Should().ThrowAsync<FunctionNotFoundException>();
                await controller.Invoking(c => c.GetRunEvents("run-9", service)).Should().ThrowAsync<FunctionNotFoundException>();
                await controller.Invoking(c => c.SaveEventSettings(new SaveEventSettingsRequestDto { FunctionId = "fn-9" }, service))
                    .Should().ThrowAsync<FunctionNotFoundException>();
            }
            finally
            {
                BlocksContext.SetContext(null, false);
            }
        }

        [Theory]
        [InlineData("GetEventCatalog", "blocks-logic::function::read")]
        [InlineData("GetEventSettings", "blocks-logic::function::read")]
        [InlineData("GetRunEvents", "blocks-logic::function::read")]
        [InlineData("SaveEventSettings", "blocks-logic::function::manage")]
        public void Each_endpoint_is_protected_with_the_read_or_manage_permission(string action, string permission)
        {
            var method = typeof(FunctionsController).GetMethod(action)!;

            var attribute = method.GetCustomAttributes(typeof(ProtectedEndPointAttribute), true).Should().ContainSingle().Subject;
            ((ProtectedEndPointAttribute)attribute).ResourceName.Should().Be(permission);
        }
    }
}
