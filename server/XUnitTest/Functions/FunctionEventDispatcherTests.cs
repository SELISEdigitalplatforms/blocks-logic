using System.Diagnostics;
using System.Text.Json;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Functions.DomainService.Events;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Mail.DomainService.Mails;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.Functions
{
    /// <summary>
    /// A succeeded run's events go out over the Genesis bus once, as the run's own caller, only if the run's version may
    /// send them, with a bounded number of retries and a per-tenant limit — and every outcome is recorded without a payload.
    /// </summary>
    public class FunctionEventDispatcherTests
    {
        private const string Tenant = "tenant-1";
        private const string RunId = "run-1";
        private static readonly string EventsKey = FunctionQueueKeys.Events(RunId);

        private readonly Mock<IMessageClient> _bus = new();
        private readonly Mock<IFunctionRunEventsRepository> _runEvents = new();
        private readonly Mock<IFunctionEventAllowList> _allowList = new();
        private readonly (IDatabase Database, FakeRedisDatabase Fake) _redis = FakeRedisDatabase.Create();
        private readonly Dictionary<string, string> _store = new(StringComparer.Ordinal);
        private readonly List<ConsumerMessage<SendMail>> _mails = [];
        private readonly List<ConsumerMessage<FunctionEvent>> _functionEvents = [];
        private readonly List<BlocksContext?> _contextAtSend = [];
        private FunctionRunEventsEntity? _saved;
        private long _rateCount;

        private readonly FunctionRunEntity _run = new()
        {
            ItemId = RunId, TenantId = Tenant, FunctionId = "fn-1", VersionId = "v-1", Attempt = 1,
            Status = RunStatus.OutputProcessing, InvokedBy = InvokedByType.Http, InvokedById = "user-1",
        };

        public FunctionEventDispatcherTests()
        {
            _allowList.Setup(a => a.AllowedTypesAsync(Tenant, It.IsAny<FunctionRunEntity>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(["SendMail", "FunctionEvent"]);
            _runEvents.Setup(r => r.SaveAsync(Tenant, It.IsAny<FunctionRunEventsEntity>(), It.IsAny<CancellationToken>()))
                .Callback((string _, FunctionRunEventsEntity d, CancellationToken _) => _saved = d).Returns(Task.CompletedTask);

            _bus.Setup(b => b.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Callback((ConsumerMessage<SendMail> m) => { _contextAtSend.Add(BlocksContext.GetContext()); _mails.Add(m); })
                .Returns(Task.CompletedTask);
            _bus.Setup(b => b.SendToConsumerAsync(It.IsAny<ConsumerMessage<FunctionEvent>>()))
                .Callback((ConsumerMessage<FunctionEvent> m) => { _contextAtSend.Add(BlocksContext.GetContext()); _functionEvents.Add(m); })
                .Returns(Task.CompletedTask);

            // SET NX / SET over a dictionary, so markers behave like Redis; a counter for the rate script.
            _redis.Fake.On("StringSetAsync", a =>
            {
                var key = a[0]!.ToString();
                var when = a.OfType<When>().FirstOrDefault(When.Always);
                if (when == When.NotExists && _store.ContainsKey(key)) return false;
                _store[key] = a[1]!.ToString();
                return true;
            });
            _redis.Fake.On("StringGetAsync", a => _store.TryGetValue(a[0]!.ToString(), out var v) ? (RedisValue)v : RedisValue.Null);
            _redis.Fake.On("KeyDeleteAsync", a => _store.Remove(a[0]!.ToString()));
            _redis.Fake.On("ScriptEvaluateAsync", _ => RedisResult.Create((RedisValue)(++_rateCount), ResultType.Integer));
        }

        private FunctionEventDispatcher Dispatcher(int? perMinute = null, ActivitySource? traces = null)
        {
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(_redis.Database);
            var settings = new Dictionary<string, string?>();
            if (perMinute is not null) settings["Functions:Events:PerTenantPerMinute"] = perMinute.ToString();
            return new FunctionEventDispatcher(
                cache.Object, _bus.Object, _runEvents.Object, _allowList.Object,
                new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
                NullLogger<FunctionEventDispatcher>.Instance, traces)
            {
                RetryDelays = [TimeSpan.Zero, TimeSpan.Zero],
            };
        }

        private static string Mail(int n, string payload = """{"purpose":"welcome","to":["a@b.c"]}""") =>
            "{\"id\":\"run-1-1-" + n + "\",\"type\":\"SendMail\",\"payload\":" + payload + "}";

        private static string Fn(int n, string name = "order.created") =>
            "{\"id\":\"run-1-1-" + n + "\",\"type\":\"FunctionEvent\",\"payload\":{\"name\":\"" + name + "\",\"data\":{\"id\":7}}}";

        private void Events(params string[] items) => _store[EventsKey] = $"[{string.Join(",", items)}]";

        private static FunctionEventsHandoff Handoff(int count, string? traceParent = null, string? rejected = null) =>
            new(EventsKey, count, rejected, traceParent);

        private Task<bool> DispatchAsync(FunctionEventsHandoff handoff, FunctionEventDispatcher? dispatcher = null) =>
            (dispatcher ?? Dispatcher()).DispatchAsync(Tenant, _run, handoff);

        private static string MarkerKey(int n) => FunctionWorkerQueueKeys.EventSentMarker($"run-1-1-{n}");

        // ---- what is sent, and as whom -----------------------------------------------------

        [Fact]
        public async Task A_mail_is_sent_through_the_message_client_to_the_mail_queue_with_the_platform_fields_forced()
        {
            Events(Mail(1, """{"purpose":"welcome","language":"de","to":["a@b.c"],"bodyDataContext":{"name":"Ada"}}"""));

            (await DispatchAsync(Handoff(1))).Should().BeTrue();

            var message = _mails.Should().ContainSingle().Subject;
            message.ConsumerName.Should().Be("blocks_email_listener");
            message.Payload.Purpose.Should().Be("welcome");
            message.Payload.Language.Should().Be("de");
            message.Payload.SendPhoneNumberAsEmail.Should().BeFalse();
            message.Payload.CorrelationId.Should().Be("run-1-1-1");
            message.Payload.BodyDataContext.Should().Equal(new Dictionary<string, string> { ["name"] = "Ada" });
            _saved!.Events.Should().ContainSingle().Which.Status.Should().Be(FunctionEventStatus.Sent);
        }

        [Fact]
        public async Task The_message_context_stays_empty_and_the_identity_comes_from_the_run_record()
        {
            BlocksContext.SetContext(BlocksContext.Create("someone-elses-tenant", ["Admin"], "admin-1", true, "", "", DateTime.MinValue, "", ["all"], "", "", "", "tok", "someone-elses-tenant"), true);
            try
            {
                Events(Mail(1), Fn(2));

                await DispatchAsync(Handoff(2));

                _mails.Single().Context.Should().BeEmpty("a function can never fill the context");
                _functionEvents.Single().Context.Should().BeEmpty();
                _contextAtSend.Should().HaveCount(2).And.OnlyContain(c => c != null);
                foreach (var context in _contextAtSend)
                {
                    context!.TenantId.Should().Be(Tenant);
                    context.UserId.Should().Be("user-1");
                    context.IsAuthenticated.Should().BeFalse("no delegation grant is written for an unauthenticated caller");
                    context.Roles.Should().BeEmpty();
                    context.Permissions.Should().BeEmpty();
                    context.OAuthToken.Should().BeEmpty();
                }

                // The previous context is back once the sends are done.
                BlocksContext.GetContext()!.TenantId.Should().Be("someone-elses-tenant");
                BlocksContext.GetContext()!.UserId.Should().Be("admin-1");
            }
            finally
            {
                BlocksContext.SetContext(null, false);
            }
        }

        [Fact]
        public async Task A_run_started_by_a_workflow_does_not_pass_the_workflow_id_off_as_a_user()
        {
            _run.InvokedBy = InvokedByType.Workflow;
            _run.InvokedById = "execution-7";
            Events(Mail(1));

            await DispatchAsync(Handoff(1));

            _contextAtSend.Single()!.UserId.Should().BeEmpty();
        }

        [Fact]
        public async Task A_function_event_goes_to_its_own_queue_with_the_source_and_depth()
        {
            Events(Fn(1));

            await DispatchAsync(Handoff(1));

            var message = _functionEvents.Should().ContainSingle().Subject;
            message.ConsumerName.Should().Be("blocks_logic_function_event_listener");
            message.Payload.EventId.Should().Be("run-1-1-1");
            message.Payload.Name.Should().Be("order.created");
            message.Payload.DataJson.Should().Be("""{"id":7}""");
            message.Payload.SourceFunctionId.Should().Be("fn-1");
            message.Payload.SourceRunId.Should().Be(RunId);
            message.Payload.Depth.Should().Be(0);
            _saved!.Events.Single().Name.Should().Be("order.created");
        }

        [Fact]
        public async Task The_events_key_is_deleted_and_the_results_written_once_every_event_has_an_outcome()
        {
            Events(Mail(1), Fn(2));

            await DispatchAsync(Handoff(2));

            _store.Should().NotContainKey(EventsKey);
            _saved!.Events.Select(e => (e.Id, e.Status)).Should().Equal(("run-1-1-1", FunctionEventStatus.Sent), ("run-1-1-2", FunctionEventStatus.Sent));
            _saved.RunId.Should().Be(RunId);
            _saved.FunctionId.Should().Be("fn-1");
            _saved.Attempt.Should().Be(1);
        }

        // ---- never trust the runner ------------------------------------------------------------

        [Fact]
        public async Task A_payload_with_an_unknown_field_is_rejected_and_the_others_still_go()
        {
            Events(Mail(1, """{"purpose":"p","to":["a@b.c"],"sendPhoneNumberAsEmail":true}"""), Fn(2));

            await DispatchAsync(Handoff(2));

            _mails.Should().BeEmpty();
            _functionEvents.Should().ContainSingle();
            _saved!.Events[0].Status.Should().Be(FunctionEventStatus.Rejected);
            _saved.Events[0].Error.Should().Contain("unknown field");
            _saved.Events[1].Status.Should().Be(FunctionEventStatus.Sent);
        }

        [Fact]
        public async Task A_type_that_is_not_on_the_versions_list_is_rejected()
        {
            _allowList.Setup(a => a.AllowedTypesAsync(Tenant, It.IsAny<FunctionRunEntity>(), It.IsAny<CancellationToken>())).ReturnsAsync(["SendMail"]);
            Events(Fn(1), Mail(2));

            await DispatchAsync(Handoff(2));

            _functionEvents.Should().BeEmpty();
            _mails.Should().ContainSingle();
            _saved!.Events[0].Status.Should().Be(FunctionEventStatus.Rejected);
            _saved.Events[0].Error.Should().Contain("not allowed");
            _store.Should().NotContainKey(MarkerKey(1), "a rejected event never takes a send-once marker");
        }

        [Fact]
        public async Task An_unknown_type_an_id_of_another_run_and_a_repeated_id_are_rejected()
        {
            Events(
                """{"id":"run-1-1-1","type":"DeleteEverything","payload":{}}""",
                """{"id":"run-2-1-1","type":"SendMail","payload":{"purpose":"p","to":["a@b.c"]}}""",
                Mail(3),
                Mail(3));

            await DispatchAsync(Handoff(4));

            _saved!.Events.Select(e => e.Status).Should().Equal(
                FunctionEventStatus.Rejected, FunctionEventStatus.Rejected, FunctionEventStatus.Sent, FunctionEventStatus.Rejected);
            _saved.Events[1].Id.Should().Be("run-1-1-2", "an id that is not the run's own is replaced, never echoed");
            _mails.Should().ContainSingle();
        }

        [Fact]
        public async Task An_events_key_that_is_not_the_runs_own_is_not_read()
        {
            _store["function:events:run-2"] = "[]";

            await DispatchAsync(new FunctionEventsHandoff("function:events:run-2", 1, null, null));

            _redis.Fake.Calls("StringGetAsync").Should().BeEmpty();
            _mails.Should().BeEmpty();
            _saved!.Rejected.Should().Contain("does not belong");
        }

        [Fact]
        public async Task Events_that_are_not_a_list_or_too_many_are_rejected_as_a_whole()
        {
            _store[EventsKey] = "{\"a\":1}";
            await DispatchAsync(Handoff(1));
            _saved!.Rejected.Should().Contain("EVENTS_REJECTED");

            _store[EventsKey] = $"[{string.Join(",", Enumerable.Range(1, 51).Select(n => Mail(n)))}]";
            await DispatchAsync(Handoff(51));
            _saved!.Rejected.Should().Contain("at most 50");
            _mails.Should().BeEmpty();
        }

        // ---- send once -------------------------------------------------------------------------

        [Fact]
        public async Task A_redelivered_job_does_not_send_an_event_twice()
        {
            Events(Mail(1));
            await DispatchAsync(Handoff(1));
            // The first delivery died before the results document was written: the key and the markers are still there.
            _store[EventsKey] = $"[{Mail(1)}]";

            await DispatchAsync(Handoff(1));

            _mails.Should().ContainSingle();
            _saved!.Events.Single().Status.Should().Be(FunctionEventStatus.Sent);
            _saved.Events.Single().SentAt.Should().NotBeNull();
        }

        [Fact]
        public async Task A_marker_without_an_outcome_is_reported_unknown_delivery_and_never_resent()
        {
            _store[MarkerKey(1)] = JsonSerializer.Serialize(new
            {
                state = "inflight", owner = "dead-worker", at = DateTimeOffset.UtcNow.AddMinutes(-10),
            });
            Events(Mail(1), Fn(2));

            (await DispatchAsync(Handoff(2))).Should().BeTrue();

            _mails.Should().BeEmpty();
            _saved!.Events[0].Status.Should().Be(FunctionEventStatus.Failed);
            _saved.Events[0].Error.Should().Be("UNKNOWN_DELIVERY");
            _saved.Events[0].Attempts.Should().Be(0);
            _functionEvents.Should().ContainSingle("the other events still go");
        }

        [Fact]
        public async Task A_marker_that_is_still_fresh_leaves_the_job_to_the_worker_that_holds_it()
        {
            _store[MarkerKey(1)] = JsonSerializer.Serialize(new { state = "inflight", owner = "other", at = DateTimeOffset.UtcNow });
            Events(Mail(1));

            (await DispatchAsync(Handoff(1))).Should().BeFalse();

            _mails.Should().BeEmpty();
            _saved.Should().BeNull();
            _store.Should().ContainKey(EventsKey, "the key stays until every event has an outcome");
        }

        [Fact]
        public async Task A_run_whose_results_document_exists_is_done_and_sends_nothing()
        {
            _runEvents.Setup(r => r.ExistsAsync(Tenant, RunId, 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            Events(Mail(1));

            (await DispatchAsync(Handoff(1))).Should().BeTrue();

            _mails.Should().BeEmpty();
            _saved.Should().BeNull();
        }

        // ---- retries and the dead letter ---------------------------------------------------------

        [Fact]
        public async Task A_send_that_fails_twice_and_then_works_is_sent_on_the_third_attempt()
        {
            var calls = 0;
            _bus.Setup(b => b.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Returns(() => ++calls < 3 ? Task.FromException(new InvalidOperationException("bus down")) : Task.CompletedTask);
            Events(Mail(1));

            await DispatchAsync(Handoff(1));

            calls.Should().Be(3);
            _saved!.Events.Single().Status.Should().Be(FunctionEventStatus.Sent);
            _saved.Events.Single().Attempts.Should().Be(3);
            _redis.Fake.Calls("StreamAddAsync").Should().BeEmpty();
        }

        [Fact]
        public async Task A_send_that_keeps_failing_is_tried_three_times_then_failed_and_dead_lettered_without_the_payload()
        {
            _bus.Setup(b => b.SendToConsumerAsync(It.IsAny<ConsumerMessage<FunctionEvent>>()))
                .Returns(Task.FromException(new InvalidOperationException("secret-looking message from the bus")));
            Events(Fn(1, "order.created"));

            await DispatchAsync(Handoff(1));

            _bus.Verify(b => b.SendToConsumerAsync(It.IsAny<ConsumerMessage<FunctionEvent>>()), Times.Exactly(3));
            var entry = _saved!.Events.Single();
            entry.Status.Should().Be(FunctionEventStatus.Failed);
            entry.Attempts.Should().Be(3);
            entry.Error.Should().Be("SEND_FAILED: InvalidOperationException", "the bus's own message is never stored");

            var dead = _redis.Fake.Calls("StreamAddAsync").Should().ContainSingle().Subject;
            dead[0]!.ToString().Should().Be(FunctionQueueKeys.DeadEventsStream);
            var fields = ((NameValueEntry[])dead[1]!).ToDictionary(f => f.Name.ToString(), f => f.Value.ToString());
            fields["eventId"].Should().Be("run-1-1-1");
            fields["runId"].Should().Be(RunId);
            fields["tenantId"].Should().Be(Tenant);
            fields["type"].Should().Be("FunctionEvent");
            fields["reason"].Should().Contain("SEND_FAILED");
            fields.Values.Should().NotContain(v => v.Contains("order.created") || v.Contains("secret-looking"));
        }

        [Fact]
        public async Task A_failed_event_is_not_sent_again_when_the_job_is_redelivered()
        {
            _bus.Setup(b => b.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>())).Returns(Task.FromException(new InvalidOperationException()));
            Events(Mail(1));
            await DispatchAsync(Handoff(1));
            _store[EventsKey] = $"[{Mail(1)}]";

            await DispatchAsync(Handoff(1));

            _bus.Verify(b => b.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()), Times.Exactly(3));
            _saved!.Events.Single().Status.Should().Be(FunctionEventStatus.Failed);
            _redis.Fake.Calls("StreamAddAsync").Should().ContainSingle("the dead letter is written once");
        }

        // ---- the per-tenant limit -------------------------------------------------------------------

        [Fact]
        public async Task Events_over_the_tenants_per_minute_limit_fail_as_rate_limited_without_a_send()
        {
            Events(Mail(1), Mail(2), Mail(3));

            await DispatchAsync(Handoff(3), Dispatcher(perMinute: 2));

            _mails.Should().HaveCount(2);
            _saved!.Events.Select(e => e.Status).Should().Equal(FunctionEventStatus.Sent, FunctionEventStatus.Sent, FunctionEventStatus.Failed);
            _saved.Events[2].Error.Should().Be("RATE_LIMITED");
            _saved.Events[2].Attempts.Should().Be(0);
            _redis.Fake.Calls("StreamAddAsync").Should().BeEmpty();
        }

        [Fact]
        public async Task The_limit_counter_is_one_atomic_script_per_tenant_and_minute()
        {
            Events(Mail(1));

            await DispatchAsync(Handoff(1));

            var call = _redis.Fake.Calls("ScriptEvaluateAsync").Should().ContainSingle().Subject;
            call[0]!.ToString().Should().Contain("INCR").And.Contain("EXPIRE");
            _redis.Fake.Calls("StringIncrementAsync").Should().BeEmpty("INCR and EXPIRE are one script, never two steps");
            ((RedisKey[])call[1]!).Single().ToString().Should().StartWith($"function:events-rate:{Tenant}:");
        }

        // ---- expiry, rejection reason, tracing -----------------------------------------------------

        [Fact]
        public async Task An_expired_events_key_fails_every_event_and_sends_nothing()
        {
            await DispatchAsync(Handoff(2));

            _mails.Should().BeEmpty();
            _functionEvents.Should().BeEmpty();
            _saved!.Events.Select(e => (e.Id, e.Status, e.Error)).Should().Equal(
                ("run-1-1-1", FunctionEventStatus.Failed, "EVENTS_EXPIRED"),
                ("run-1-1-2", FunctionEventStatus.Failed, "EVENTS_EXPIRED"));
        }

        [Fact]
        public async Task The_runners_rejection_reason_is_written_to_the_results_document()
        {
            var handoff = new FunctionEventsHandoff(null, 0, "EVENTS_REJECTED: event 2: has a type that is not allowed for this function", null);

            (await DispatchAsync(handoff)).Should().BeTrue();

            _saved!.Events.Should().BeEmpty();
            _saved.Rejected.Should().Be(handoff.Rejected);
            _redis.Fake.Calls("StringGetAsync").Should().BeEmpty();
        }

        [Fact]
        public async Task Each_send_is_an_Emit_span_under_the_runs_trace_with_ids_and_no_payload()
        {
            using var source = new ActivitySource("test-functions");
            var started = new List<Activity>();
            using var listener = new ActivityListener
            {
                ShouldListenTo = s => s.Name == "test-functions",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStarted = started.Add,
            };
            ActivitySource.AddActivityListener(listener);
            const string parent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
            Events(Mail(1, """{"purpose":"p","to":["secret@person.example"]}"""));

            await DispatchAsync(Handoff(1, parent), Dispatcher(traces: source));

            var emit = started.Should().ContainSingle().Subject;
            emit.OperationName.Should().Be("Function::Emit");
            emit.TraceId.ToString().Should().Be("0af7651916cd43dd8448eb211c80319c");
            emit.ParentSpanId.ToString().Should().Be("b7ad6b7169203331");
            var tags = emit.Tags.ToDictionary(t => t.Key, t => t.Value);
            tags["blocks.tenant_id"].Should().Be(Tenant);
            tags["blocks.function.id"].Should().Be("fn-1");
            tags["blocks.function.event_type"].Should().Be("SendMail");
            tags["blocks.function.event_id"].Should().Be("run-1-1-1");
            tags.Values.Should().NotContain(v => v != null && v.Contains("secret@person.example"));
        }

        // ---- the allow list (real one) ---------------------------------------------------------------

        [Fact]
        public async Task The_allow_list_reads_the_versions_row_and_narrows_it_for_a_public_trigger()
        {
            var settings = new Mock<IFunctionEventSettingsRepository>();
            var versions = new Mock<IFunctionVersionRepository>();
            var functions = new Mock<IFunctionRepository>();
            settings.Setup(s => s.GetAsync(Tenant, "fn-1", "v-1", It.IsAny<CancellationToken>())).ReturnsAsync(new FunctionEventSettingsEntity
            {
                Events = [new FunctionEventSetting { Type = "SendMail" }, new FunctionEventSetting { Type = "FunctionEvent" }],
            });
            versions.Setup(v => v.GetByIdAsync(Tenant, "v-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FunctionVersionEntity { ItemId = "v-1", Trigger = new TriggerConfig { AuthMode = AuthMode.Public } });
            var list = new FunctionEventAllowList(settings.Object, versions.Object, functions.Object);

            (await list.AllowedTypesAsync(Tenant, _run)).Should().Equal("SendMail");

            versions.Setup(v => v.GetByIdAsync(Tenant, "v-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FunctionVersionEntity { ItemId = "v-1", Trigger = new TriggerConfig { AuthMode = AuthMode.Token } });
            (await list.AllowedTypesAsync(Tenant, _run)).Should().Equal("SendMail", "FunctionEvent");
        }

        [Fact]
        public async Task A_test_run_uses_the_draft_row_and_a_function_that_is_gone_is_treated_as_public()
        {
            var settings = new Mock<IFunctionEventSettingsRepository>();
            var versions = new Mock<IFunctionVersionRepository>();
            var functions = new Mock<IFunctionRepository>();
            settings.Setup(s => s.GetAsync(Tenant, "fn-1", null, It.IsAny<CancellationToken>())).ReturnsAsync(new FunctionEventSettingsEntity
            {
                Events = [new FunctionEventSetting { Type = "SendMail" }, new FunctionEventSetting { Type = "FunctionEvent" }],
            });
            var list = new FunctionEventAllowList(settings.Object, versions.Object, functions.Object);
            var testRun = new FunctionRunEntity { ItemId = "r", FunctionId = "fn-1", VersionId = null };

            (await list.AllowedTypesAsync(Tenant, testRun)).Should().Equal("SendMail");

            functions.Setup(f => f.GetByIdAsync(Tenant, "fn-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FunctionEntity { ItemId = "fn-1", Trigger = new TriggerConfig { AuthMode = AuthMode.Token } });
            (await list.AllowedTypesAsync(Tenant, testRun)).Should().Equal("SendMail", "FunctionEvent");
        }
    }
}
