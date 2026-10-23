using System.Text.Json.Nodes;
using Blocks.Genesis;
using Common.InternalService.Access;
using FluentAssertions;
using Functions.DomainService.Events;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using Blocks.Genesis.Entities.Logic;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The event trigger's start of a run (<see cref="FunctionInvocationService.InvokeFromEventAsync"/>): what the run
    /// records and the function sees, the event queue, authorization by subscription, admission, the depth it stores, and
    /// "one run per event and function".
    /// </summary>
    public class FunctionEventTriggerTests
    {
        private const string Tenant = "tenant-1";
        private const string Data = """{"orderId":7}""";

        private readonly Mock<IFunctionRepository> _functions = new();
        private readonly Mock<IFunctionVersionRepository> _versions = new();
        private readonly Mock<IFunctionRunRepository> _runs = new();
        private readonly Mock<IFunctionAdmissionService> _admission = new();
        private readonly Mock<IFunctionBuildService> _builds = new();
        private readonly Mock<IFunctionDelegationService> _delegation = new();
        private readonly Mock<IEndpointAccessAuthorizer> _access = new();
        private readonly Mock<global::Functions.DomainService.Storage.IFunctionArtifactStore> _artifacts = new();
        private readonly Mock<IFunctionEventSettingsRepository> _settings = new();
        private readonly Mock<IFunctionEventRunStore> _eventRuns = new();
        private readonly (IDatabase Database, FakeRedisDatabase Fake) _redis = FakeRedisDatabase.Create();
        private readonly HttpContextAccessor _http = new() { HttpContext = new DefaultHttpContext() };

        // A Token function: not Public, so the test proves the subscription alone authorizes the start.
        private readonly FunctionEntity _function = new() { ItemId = "fn-1", Status = FunctionStatus.Live, ActiveVersionId = "v-1" };
        private readonly FunctionVersionEntity _version = new()
        {
            ItemId = "v-1", FunctionId = "fn-1", Number = 2, ImageDigest = "sha256:abc",
            Trigger = new TriggerConfig { AuthMode = AuthMode.Token, HttpEnabled = false },
        };
        private FunctionRunEntity? _created;

        public FunctionEventTriggerTests()
        {
            _functions.Setup(f => f.GetByIdAsync(Tenant, "fn-1", It.IsAny<CancellationToken>())).ReturnsAsync(_function);
            _versions.Setup(v => v.GetByIdAsync(Tenant, "v-1", It.IsAny<CancellationToken>())).ReturnsAsync(_version);
            _admission
                .Setup(a => a.AdmitAsync(It.IsAny<FunctionEntity>(), Tenant, It.IsAny<string?>(), It.IsAny<InvokedByType>(), It.IsAny<FunctionVersionEntity?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdmissionResult.Admit());
            _runs
                .Setup(r => r.CreateAsync(Tenant, It.IsAny<FunctionRunEntity>(), It.IsAny<CancellationToken>()))
                .Callback((string _, FunctionRunEntity run, CancellationToken _) => _created = run)
                .Returns(Task.CompletedTask);
            Subscribed("order.created");

            _redis.Fake.On("StringSetAsync", _ => true);
        }

        private void Subscribed(params string[] names) =>
            _settings.Setup(s => s.GetAsync(Tenant, "fn-1", "v-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FunctionEventSettingsEntity { FunctionId = "fn-1", VersionId = "v-1", TriggerEvents = [.. names] });

        private FunctionInvocationService Service()
        {
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(_redis.Database);

            return new FunctionInvocationService(
                _functions.Object, _versions.Object, _runs.Object, _admission.Object,
                new FunctionAuthorizationService(), _builds.Object, _access.Object,
                _delegation.Object, _http, _artifacts.Object, cache.Object, new ConfigurationBuilder().Build(),
                NullLogger<FunctionInvocationService>.Instance, _settings.Object, _eventRuns.Object);
        }

        private static BlocksContext MessageContext() => BlocksContext.Create(
            Tenant, [], string.Empty, false, string.Empty, string.Empty, DateTime.MinValue, string.Empty, [], string.Empty, string.Empty, string.Empty, string.Empty, Tenant);

        private static FunctionEventInvocation Event(string id = "run-0-1-1", string name = "order.created", int depth = 0) =>
            new(id, name, Data, depth);

        private NameValueEntry[] EntryOn(string stream) =>
            (NameValueEntry[])_redis.Fake.Calls("StreamAddAsync").Single(c => (string)(RedisKey)c[0]! == stream)[1]!;

        private JsonNode Envelope()
        {
            var entries = _redis.Fake.Calls("HashSetAsync").SelectMany(c => (HashEntry[])c[1]!).ToList();
            return JsonNode.Parse(entries.Single(e => e.Name == "envelope").Value.ToString())!;
        }

        // ------------------------------------------------------------------- the run ----

        [Fact]
        public async Task The_run_is_started_by_the_event_with_its_data_as_input_and_the_event_and_function_as_idempotency_key()
        {
            var result = await Service().InvokeFromEventAsync(Tenant, "fn-1", Event(), MessageContext());

            _created.Should().NotBeNull();
            result.RunId.Should().Be(_created!.ItemId);
            result.Status.Should().Be(FunctionQueueKeys.Wire.Queued);
            _created.InvokedBy.Should().Be(InvokedByType.Event);
            _created.InvokedById.Should().Be("run-0-1-1");
            _created.Input.Should().Be(Data);
            _created.IdempotencyKey.Should().Be("run-0-1-1-fn-1");
            _created.VersionId.Should().Be("v-1");
            _created.Status.Should().Be(RunStatus.Queued);
        }

        [Fact]
        public async Task The_envelope_gives_the_function_the_event_type_id_and_name_and_the_data_as_input()
        {
            await Service().InvokeFromEventAsync(Tenant, "fn-1", Event(), MessageContext());

            var envelope = Envelope();
            envelope["run"]!["invokedBy"]!["type"]!.GetValue<string>().Should().Be("event");
            envelope["run"]!["invokedBy"]!["id"]!.GetValue<string>().Should().Be("run-0-1-1");
            envelope["run"]!["invokedBy"]!["name"]!.GetValue<string>().Should().Be("order.created");
            envelope["input"]!["orderId"]!.GetValue<int>().Should().Be(7);
        }

        [Fact]
        public async Task The_envelope_of_every_other_run_has_no_event_name()
        {
            var run = new FunctionRunEntity { ItemId = "r", FunctionId = "fn-1", InvokedBy = InvokedByType.Http };

            var envelope = JsonNode.Parse(FunctionEnvelopeBuilder.Build(run, _version, _function, null, null))!;

            envelope["run"]!["invokedBy"]!.AsObject().ContainsKey("name").Should().BeFalse();
        }

        [Fact]
        public async Task The_run_identity_is_the_messages_context_without_a_user()
        {
            await Service().InvokeFromEventAsync(Tenant, "fn-1", Event(), MessageContext());

            var context = Envelope()["context"]!;
            context["tenantId"]!.GetValue<string>().Should().Be(Tenant);
            context["isAuthenticated"]!.GetValue<bool>().Should().BeFalse();
            _delegation.Verify(d => d.CreateGrantAsync(Tenant, It.Is<BlocksContext?>(c => c != null && !c.IsAuthenticated), It.IsAny<AuthMode>()), Times.Once);
        }

        // -------------------------------------------------------------- the event queue ----

        [Fact]
        public async Task The_run_goes_to_the_event_queue_not_the_http_queue()
        {
            await Service().InvokeFromEventAsync(Tenant, "fn-1", Event(), MessageContext());

            var entry = EntryOn(FunctionQueueKeys.EventRunsStream);
            entry.Single(e => e.Name == "runId").Value.ToString().Should().Be(_created!.ItemId);
            entry.Single(e => e.Name == "functionId").Value.ToString().Should().Be("fn-1");
            entry.Should().Contain(e => e.Name == "image").And.Contain(e => e.Name == "attempt").And.Contain(e => e.Name == "protocol");
            _redis.Fake.Calls("StreamAddAsync").Should().NotContain(c => (string)(RedisKey)c[0]! == FunctionQueueKeys.RunsStream);
            _redis.Fake.Calls("Publish").Select(c => c[0]!.ToString()).Should().ContainSingle().Which.Should().Be(FunctionQueueKeys.EventRunsNudgeChannel);
        }

        [Fact]
        public async Task An_http_run_still_goes_to_the_runs_queue()
        {
            _version.Trigger.HttpEnabled = true;
            _access.Setup(a => a.AuthorizeAsync(It.IsAny<HttpRequest>(), Tenant, It.IsAny<EndpointAccessPolicy>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(EndpointAccessDecision.Public());

            await Service().InvokeHttpAsync(Tenant, "fn-1", new global::Functions.DomainService.Dtos.Requests.InvokeFunctionRequestDto
            {
                Method = "POST", Path = "", Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            });

            _redis.Fake.Calls("StreamAddAsync").Should().ContainSingle(c => (string)(RedisKey)c[0]! == FunctionQueueKeys.RunsStream);
            _redis.Fake.Calls("StreamAddAsync").Should().NotContain(c => (string)(RedisKey)c[0]! == FunctionQueueKeys.EventRunsStream);
        }

        // ------------------------------------------------------------- authorization ----

        [Fact]
        public async Task The_subscription_authorizes_the_start_without_the_function_being_public_or_http_enabled()
        {
            _version.Trigger.AuthMode.Should().Be(AuthMode.Token);
            _version.Trigger.HttpEnabled.Should().BeFalse();

            var act = () => Service().InvokeFromEventAsync(Tenant, "fn-1", Event(), MessageContext());

            await act.Should().NotThrowAsync();
            _created.Should().NotBeNull();
        }

        [Fact]
        public async Task A_function_whose_deployed_version_does_not_subscribe_is_refused_and_no_run_exists()
        {
            Subscribed("something.else");

            var act = () => Service().InvokeFromEventAsync(Tenant, "fn-1", Event(), MessageContext());

            await act.Should().ThrowAsync<FunctionAuthorizationException>();
            _created.Should().BeNull();
            _redis.Fake.Calls("StreamAddAsync").Should().BeEmpty();
        }

        [Fact]
        public async Task A_function_with_no_settings_row_is_refused()
        {
            _settings.Setup(s => s.GetAsync(Tenant, "fn-1", "v-1", It.IsAny<CancellationToken>())).ReturnsAsync((FunctionEventSettingsEntity?)null);

            var act = () => Service().InvokeFromEventAsync(Tenant, "fn-1", Event(), MessageContext());

            await act.Should().ThrowAsync<FunctionAuthorizationException>();
        }

        [Fact]
        public void Authorization_by_event_is_exactly_the_subscription_to_the_name()
        {
            var auth = new FunctionAuthorizationService();
            var row = new FunctionEventSettingsEntity { TriggerEvents = ["order.created"] };

            auth.AuthorizeForEvent(row, "order.created").Allowed.Should().BeTrue();
            auth.AuthorizeForEvent(row, "Order.Created").Allowed.Should().BeFalse("names are exact");
            auth.AuthorizeForEvent(row, "order.updated").Allowed.Should().BeFalse();
            auth.AuthorizeForEvent(row, string.Empty).Allowed.Should().BeFalse();
            auth.AuthorizeForEvent(null, "order.created").Allowed.Should().BeFalse();
        }

        [Fact]
        public async Task A_function_that_is_not_deployed_or_not_live_cannot_be_started()
        {
            _function.Status = FunctionStatus.Paused;

            var act = () => Service().InvokeFromEventAsync(Tenant, "fn-1", Event(), MessageContext());

            await act.Should().ThrowAsync<FunctionValidationException>();
            _created.Should().BeNull();
        }

        [Fact]
        public async Task An_unknown_function_is_not_found()
        {
            var act = () => Service().InvokeFromEventAsync(Tenant, "fn-404", Event(), MessageContext());

            await act.Should().ThrowAsync<FunctionNotFoundException>();
        }

        // ----------------------------------------------------------------- admission ----

        [Fact]
        public async Task Admission_is_asked_as_an_event_and_a_refusal_creates_no_run()
        {
            _admission
                .Setup(a => a.AdmitAsync(_function, Tenant, Data, InvokedByType.Event, _version, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new FunctionRateLimitedException("too many", 5));

            var act = () => Service().InvokeFromEventAsync(Tenant, "fn-1", Event(), MessageContext());

            await act.Should().ThrowAsync<FunctionRateLimitedException>();
            _created.Should().BeNull();
            _eventRuns.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<FunctionEventRunInfo>()), Times.Never);
        }

        // ---------------------------------------------------------------------- depth ----

        [Fact]
        public async Task The_events_depth_and_name_are_stored_for_the_run_before_it_is_recorded()
        {
            var order = new List<string>();
            _eventRuns.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<FunctionEventRunInfo>()))
                .Callback(() => order.Add("store")).Returns(Task.CompletedTask);
            _runs.Setup(r => r.CreateAsync(Tenant, It.IsAny<FunctionRunEntity>(), It.IsAny<CancellationToken>()))
                .Callback((string _, FunctionRunEntity run, CancellationToken _) => { _created = run; order.Add("insert"); })
                .Returns(Task.CompletedTask);

            await Service().InvokeFromEventAsync(Tenant, "fn-1", Event(depth: 3), MessageContext());

            _eventRuns.Verify(s => s.SaveAsync(_created!.ItemId, new FunctionEventRunInfo("run-0-1-1", "order.created", 3)), Times.Once);
            order.Should().Equal("store", "insert");
        }

        [Fact]
        public async Task When_the_depth_cannot_be_stored_no_run_is_created()
        {
            _eventRuns.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<FunctionEventRunInfo>())).ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

            var act = () => Service().InvokeFromEventAsync(Tenant, "fn-1", Event(), MessageContext());

            await act.Should().ThrowAsync<RedisConnectionException>();
            _created.Should().BeNull();
        }

        [Fact]
        public void A_run_started_by_an_event_sends_events_one_deeper_than_the_event_that_started_it()
        {
            var eventRun = new FunctionRunEntity { InvokedBy = InvokedByType.Event };

            FunctionEventDepth.ForRun(eventRun, 0).Should().Be(1);
            FunctionEventDepth.ForRun(eventRun, 3).Should().Be(4);
            FunctionEventDepth.ForRun(eventRun, null).Should().Be(FunctionEventDepth.EventStartedRunDepth, "unknown: one hop");
            FunctionEventDepth.ForRun(new FunctionRunEntity { InvokedBy = InvokedByType.Http }, 4).Should().Be(0, "not an event run");
        }

        // ------------------------------------------------------ one run per event ----

        [Fact]
        public async Task A_redelivered_event_that_already_has_a_run_starts_no_second_one()
        {
            _runs.Setup(r => r.FindEventRunAsync(Tenant, "fn-1", "run-0-1-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FunctionRunEntity { ItemId = "run-first", FunctionId = "fn-1" });

            var result = await Service().InvokeFromEventAsync(Tenant, "fn-1", Event(), MessageContext());

            result.RunId.Should().Be("run-first");
            _created.Should().BeNull();
            _admission.Verify(a => a.AdmitAsync(It.IsAny<FunctionEntity>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<InvokedByType>(), It.IsAny<FunctionVersionEntity?>(), It.IsAny<CancellationToken>()), Times.Never);
            _redis.Fake.Calls("StreamAddAsync").Should().BeEmpty();
        }

        [Fact]
        public async Task An_event_being_started_by_another_worker_right_now_starts_no_second_run()
        {
            _redis.Fake.On("StringSetAsync", _ => false);

            var result = await Service().InvokeFromEventAsync(Tenant, "fn-1", Event(), MessageContext());

            result.RunId.Should().BeEmpty();
            _created.Should().BeNull();
            _runs.Verify(r => r.FindEventRunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task The_start_lock_is_per_event_and_function_and_is_released_even_when_the_start_fails()
        {
            _admission
                .Setup(a => a.AdmitAsync(It.IsAny<FunctionEntity>(), Tenant, It.IsAny<string?>(), InvokedByType.Event, It.IsAny<FunctionVersionEntity?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new FunctionRateLimitedException("too many", 5));

            var act = () => Service().InvokeFromEventAsync(Tenant, "fn-1", Event(), MessageContext());
            await act.Should().ThrowAsync<FunctionRateLimitedException>();

            var key = FunctionWorkerQueueKeys.EventStartLock("run-0-1-1", "fn-1");
            _redis.Fake.Calls("StringSetAsync").Should().ContainSingle(c => (string)(RedisKey)c[0]! == key);
            _redis.Fake.Calls("KeyDeleteAsync").Should().ContainSingle(c => (string)(RedisKey)c[0]! == key);
        }

        [Fact]
        public async Task The_event_queue_carries_the_run_through_the_normal_retry_policy_attempts()
        {
            _version.Retry = new RetryPolicy { Attempts = 3 };

            await Service().InvokeFromEventAsync(Tenant, "fn-1", Event(), MessageContext());

            _created!.MaxAttempts.Should().Be(3);
            _created.Attempt.Should().Be(1);
        }
    }
}
