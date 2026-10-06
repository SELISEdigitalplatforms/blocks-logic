using Blocks.Genesis;
using Common.InternalService.Access;
using FluentAssertions;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The invocation side of sandbox reuse and synchronous answers (sandbox/REUSE.md): the run
    /// entry's <c>reuse</c> flag comes from the deployed version only and never rides a test; the
    /// public route answers the trigger's verbs and 405s the rest; and the wait a public caller
    /// gets is decided by the deployed response mode and <c>Prefer</c>, bounded by the HTTP cap.
    /// </summary>
    public class FunctionReuseInvocationTests
    {
        private const string Tenant = "tenant-1";

        private readonly Mock<IFunctionRepository> _functions = new();
        private readonly Mock<IFunctionVersionRepository> _versions = new();
        private readonly Mock<IFunctionRunRepository> _runs = new();
        private readonly Mock<IFunctionAdmissionService> _admission = new();
        private readonly Mock<IFunctionAuthorizationService> _authorization = new();
        private readonly Mock<IFunctionBuildService> _builds = new();
        private readonly Mock<IFunctionDelegationService> _delegation = new();
        private readonly Mock<IEndpointAccessAuthorizer> _access = new();
        private readonly Mock<global::Functions.DomainService.Storage.IFunctionArtifactStore> _artifacts = new();
        private readonly (IDatabase Database, FakeRedisDatabase Fake) _redis = FakeRedisDatabase.Create();
        private readonly HttpContextAccessor _http = new() { HttpContext = new DefaultHttpContext() };

        private readonly FunctionEntity _function = new() { ItemId = "fn-1", Status = FunctionStatus.Live, ActiveVersionId = "v-1" };
        private readonly FunctionVersionEntity _version = new() { ItemId = "v-1", FunctionId = "fn-1", Number = 1, ImageDigest = "sha256:abc" };
        private FunctionRunEntity? _created;

        public FunctionReuseInvocationTests()
        {
            _functions.Setup(f => f.GetByIdAsync(Tenant, "fn-1", It.IsAny<CancellationToken>())).ReturnsAsync(_function);
            _versions.Setup(v => v.GetByIdAsync(Tenant, "v-1", It.IsAny<CancellationToken>())).ReturnsAsync(_version);
            _authorization
                .Setup(a => a.AuthorizeForWorkflow(It.IsAny<FunctionEntity>(), It.IsAny<FunctionVersionEntity?>(), It.IsAny<BlocksContext?>()))
                .Returns(new AuthorizationResult(true));
            _admission
                .Setup(a => a.AdmitAsync(It.IsAny<FunctionEntity>(), Tenant, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AdmissionResult.Admit());
            _runs
                .Setup(r => r.CreateAsync(Tenant, It.IsAny<FunctionRunEntity>(), It.IsAny<CancellationToken>()))
                .Callback((string _, FunctionRunEntity run, CancellationToken _) => _created = run)
                .Returns(Task.CompletedTask);
            _builds
                .Setup(b => b.CreateTestBuildAsync(Tenant, It.IsAny<FunctionEntity>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((new FunctionBuildEntity { ItemId = "tb-1", Status = BuildStatus.Queued, Ephemeral = true }, "function:source:tb-1"));
            _access
                .Setup(a => a.AuthorizeAsync(It.IsAny<HttpRequest>(), Tenant, It.IsAny<EndpointAccessPolicy>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(EndpointAccessDecision.Public());

            // Pub/sub unavailable: the wait polls the record alone, which is all these tests need.
            _redis.Fake.On("get_Multiplexer", _ => throw new InvalidOperationException("no pub/sub"));
            _redis.Fake.On("StringSetAsync", _ => true);
        }

        private FunctionInvocationService Service(params (string Key, string Value)[] settings)
        {
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(_redis.Database);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
                .Build();

            return new FunctionInvocationService(
                _functions.Object, _versions.Object, _runs.Object, _admission.Object,
                _authorization.Object, _builds.Object, _access.Object,
                _delegation.Object, _http, _artifacts.Object, cache.Object, configuration,
                NullLogger<FunctionInvocationService>.Instance);
        }

        private NameValueEntry[] RunEntry() =>
            (NameValueEntry[])_redis.Fake.Calls("StreamAddAsync").Single(c => (string)(RedisKey)c[0]! == FunctionQueueKeys.RunsStream)[1]!;

        private static InvokeFunctionRequestDto Call(string method = "POST", int? preferWait = null, bool preferAsync = false) => new()
        {
            Method = method,
            Path = "",
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            PreferWaitSeconds = preferWait,
            PreferAsync = preferAsync,
        };

        private void RunIs(RunStatus status) =>
            _runs.Setup(r => r.GetByIdAsync(Tenant, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new FunctionRunEntity { ItemId = _created?.ItemId ?? "run", Status = status, Result = "{\"ok\":true}" });

        // ----------------------------------------------------------- run entry flag ----

        [Fact]
        public async Task A_deployed_version_that_opted_in_queues_reuse_1()
        {
            _version.Trigger.ReuseSandbox = true;

            await Service().InvokeHttpAsync(Tenant, "fn-1", Call());

            RunEntry().Single(e => e.Name == FunctionQueueKeys.RunReuseField).Value.ToString().Should().Be("1");
        }

        [Fact]
        public async Task Every_http_call_of_a_deployed_version_asks_for_a_warm_sandbox_switch_or_not()
        {
            // Always on since 2026-10-06; the stored per-function flag is ignored either way.
            _function.Trigger.ReuseSandbox = false;
            _version.Trigger.ReuseSandbox = false;

            await Service().InvokeHttpAsync(Tenant, "fn-1", Call());

            RunEntry().Single(e => e.Name == FunctionQueueKeys.RunReuseField).Value.ToString().Should().Be("1");
        }

        [Fact]
        public async Task A_test_never_carries_the_reuse_flag_and_keeps_its_own_stream()
        {
            _function.Trigger.ReuseSandbox = true;
            _version.Trigger.ReuseSandbox = true;
            RunIs(RunStatus.Succeeded);

            await Service().TestAsync(Tenant, "fn-1", new TestFunctionRequestDto { FunctionId = "fn-1" });

            var adds = _redis.Fake.Calls("StreamAddAsync");
            adds.Should().ContainSingle();
            ((string)(RedisKey)adds[0][0]!).Should().Be(FunctionQueueKeys.TestsStream);
            ((NameValueEntry[])adds[0][1]!).Should().NotContain(e => e.Name == FunctionQueueKeys.RunReuseField);
        }

        // ------------------------------------------------------------------- verbs ----

        [Theory]
        [InlineData("GET")]
        [InlineData("put")]
        [InlineData("DELETE")]
        public async Task A_listed_verb_is_accepted(string method)
        {
            _version.Trigger.HttpMethods = ["GET", "PUT", "DELETE"];

            var result = await Service().InvokeHttpAsync(Tenant, "fn-1", Call(method));

            result.Status.Should().Be(FunctionQueueKeys.Wire.Queued);
        }

        [Fact]
        public async Task An_unlisted_verb_is_405_naming_every_allowed_verb()
        {
            _version.Trigger.HttpMethods = ["GET", "PATCH"];

            var act = () => Service().InvokeHttpAsync(Tenant, "fn-1", Call("POST"));

            (await act.Should().ThrowAsync<FunctionMethodNotAllowedException>()).Which.Allowed.Should().Be("GET, PATCH");
            _created.Should().BeNull("nothing is queued for a refused verb");
        }

        [Fact]
        public async Task With_no_list_the_legacy_method_alone_is_allowed()
        {
            _version.Trigger.HttpMethod = HttpTriggerMethod.Get;

            var act = () => Service().InvokeHttpAsync(Tenant, "fn-1", Call("PUT"));

            (await act.Should().ThrowAsync<FunctionMethodNotAllowedException>()).Which.Allowed.Should().Be("GET");
        }

        // ------------------------------------------------------------- sync decision ----

        [Fact]
        public void Only_a_sync_function_waits_and_prefer_only_shortens_or_skips_that_wait()
        {
            var service = Service(("Functions:HttpSyncWaitMaxSeconds", "20"));
            var async = new TriggerConfig();
            var sync = new TriggerConfig { ResponseMode = "sync" };

            service.SyncWaitSeconds(async, Call()).Should().BeNull("default: today's 202");
            service.SyncWaitSeconds(async, Call(preferWait: 5)).Should().BeNull("Prefer: wait on an async function changes nothing");
            service.SyncWaitSeconds(new TriggerConfig { ResponseMode = "async" }, Call(preferWait: 5)).Should().BeNull();
            service.SyncWaitSeconds(new TriggerConfig { ResponseMode = null }, Call()).Should().BeNull();
            service.SyncWaitSeconds(sync, Call()).Should().Be(20);
            service.SyncWaitSeconds(sync, Call(preferWait: 3)).Should().Be(3);
            service.SyncWaitSeconds(sync, Call(preferWait: 500)).Should().Be(20);
            service.SyncWaitSeconds(sync, Call(preferWait: 0)).Should().BeNull();
            service.SyncWaitSeconds(sync, Call(preferAsync: true)).Should().BeNull();
        }

        [Fact]
        public async Task A_default_call_does_not_wait_and_is_not_flagged_sync()
        {
            RunIs(RunStatus.Succeeded);

            var result = await Service().InvokeHttpAsync(Tenant, "fn-1", Call());

            result.Status.Should().Be(FunctionQueueKeys.Wire.Queued);
            result.RespondSynchronously.Should().BeFalse();
            _runs.Verify(r => r.GetByIdAsync(Tenant, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Prefer_wait_on_an_async_function_still_returns_at_once()
        {
            RunIs(RunStatus.Succeeded);

            var result = await Service().InvokeHttpAsync(Tenant, "fn-1", Call(preferWait: 10));

            result.Status.Should().Be(FunctionQueueKeys.Wire.Queued);
            result.RespondSynchronously.Should().BeFalse();
            _runs.Verify(r => r.GetByIdAsync(Tenant, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>Pub/sub available, with the runner's or Worker's notification fired by the test.</summary>
        private Func<Action<RedisChannel, RedisValue>?> WithPubSub()
        {
            Action<RedisChannel, RedisValue>? notify = null;
            var subscriber = new Mock<ISubscriber>();
            subscriber
                .Setup(s => s.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
                .Callback((RedisChannel _, Action<RedisChannel, RedisValue> handler, CommandFlags _) => notify = handler)
                .Returns(Task.CompletedTask);
            var multiplexer = new Mock<IConnectionMultiplexer>();
            multiplexer.Setup(m => m.GetSubscriber(It.IsAny<object?>())).Returns(subscriber.Object);
            _redis.Fake.On("get_Multiplexer", _ => multiplexer.Object);
            return () => notify;
        }

        private static async Task<Action<RedisChannel, RedisValue>> SubscribedAsync(Func<Action<RedisChannel, RedisValue>?> notify)
        {
            for (var i = 0; i < 200 && notify() is null; i++) await Task.Delay(10);
            return notify() ?? throw new InvalidOperationException("the wait never subscribed");
        }

        [Fact]
        public async Task A_sync_success_is_answered_from_the_runners_result_without_waiting_for_the_record()
        {
            _version.Trigger.ResponseMode = "sync";
            RunIs(RunStatus.Running); // the Worker has not written the record yet
            var notify = WithPubSub();
            _redis.Fake.On("HashGetAsync", _ => (RedisValue)FunctionQueueKeys.Wire.Succeeded);
            _redis.Fake.On("StringGetAsync", _ => (RedisValue)"{\"from\":\"runner\"}");
            var clock = System.Diagnostics.Stopwatch.StartNew();

            var call = Service().InvokeHttpAsync(Tenant, "fn-1", Call(preferWait: 20));
            (await SubscribedAsync(notify))(RedisChannel.Literal("x"), FunctionQueueKeys.Wire.Succeeded);
            var result = await call;

            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
            result.Status.Should().Be(FunctionQueueKeys.Wire.Succeeded);
            result.Result.Should().Be("{\"from\":\"runner\"}");
            result.RespondSynchronously.Should().BeTrue();
        }

        [Fact]
        public async Task A_function_with_output_actions_still_waits_for_the_record()
        {
            _version.Trigger.ResponseMode = "sync";
            _version.OutputActions = [new OutputAction()];
            RunIs(RunStatus.Running);
            var notify = WithPubSub();
            _redis.Fake.On("HashGetAsync", _ => (RedisValue)FunctionQueueKeys.Wire.Succeeded);
            _redis.Fake.On("StringGetAsync", _ => (RedisValue)"{\"from\":\"runner\"}");

            var call = Service().InvokeHttpAsync(Tenant, "fn-1", Call(preferWait: 1));
            (await SubscribedAsync(notify))(RedisChannel.Literal("x"), FunctionQueueKeys.Wire.Succeeded);
            var result = await call;

            result.Status.Should().Be(FunctionQueueKeys.Wire.Queued, "the record never finished, so the caller gets the 202");
        }

        [Fact]
        public async Task A_failure_is_never_answered_from_the_runner_it_waits_for_the_retry_decision()
        {
            _version.Trigger.ResponseMode = "sync";
            RunIs(RunStatus.Running);
            var notify = WithPubSub();
            _redis.Fake.On("HashGetAsync", _ => (RedisValue)FunctionQueueKeys.Wire.Failed);

            var call = Service().InvokeHttpAsync(Tenant, "fn-1", Call(preferWait: 1));
            (await SubscribedAsync(notify))(RedisChannel.Literal("x"), FunctionQueueKeys.Wire.Failed);
            var result = await call;

            result.Status.Should().Be(FunctionQueueKeys.Wire.Queued);
            _redis.Fake.Calls("StringGetAsync").Should().BeEmpty("a failure's result is never read from the runner");
        }

        [Fact]
        public async Task A_sync_trigger_waits_and_returns_the_finished_run_flagged_for_mapping()
        {
            _version.Trigger.ResponseMode = "sync";
            RunIs(RunStatus.Succeeded);

            var result = await Service().InvokeHttpAsync(Tenant, "fn-1", Call());

            result.Status.Should().Be("SUCCEEDED");
            result.Result.Should().Be("{\"ok\":true}");
            result.RespondSynchronously.Should().BeTrue();
        }

        [Theory]
        [InlineData(RunStatus.Running)]
        [InlineData(RunStatus.Starting)]
        [InlineData(RunStatus.Claimed)]
        public async Task A_run_not_finished_in_the_window_is_reported_exactly_as_the_async_202(RunStatus observed)
        {
            // The async path says QUEUED; the sync fallback must say the same, not what it last saw.
            _version.Trigger.ResponseMode = "sync";
            RunIs(observed);
            var clock = System.Diagnostics.Stopwatch.StartNew();

            var sync = await Service().InvokeHttpAsync(Tenant, "fn-1", Call(preferWait: 1));

            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "the caller's own bound holds");
            sync.RunId.Should().Be(_created!.ItemId);
            sync.Status.Should().Be(FunctionQueueKeys.Wire.Queued);
            sync.RespondSynchronously.Should().BeFalse();
            System.Text.Json.JsonSerializer.Serialize(sync).Should().Be(System.Text.Json.JsonSerializer.Serialize(
                new global::Functions.DomainService.Dtos.Responses.InvokeResultDto { RunId = _created.ItemId, Status = FunctionQueueKeys.Wire.Queued }));
        }

        [Fact]
        public async Task A_failure_that_will_be_retried_is_not_an_answer_but_a_202()
        {
            _version.Trigger.ResponseMode = "sync";
            _runs.Setup(r => r.GetByIdAsync(Tenant, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new FunctionRunEntity
                {
                    ItemId = _created!.ItemId, VersionId = "v-1", Status = RunStatus.Failed,
                    ErrorCode = RunErrorCode.SandboxStartFailed, Attempt = 1, MaxAttempts = 2,
                });

            var result = await Service().InvokeHttpAsync(Tenant, "fn-1", Call(preferWait: 2));

            result.Status.Should().Be(FunctionQueueKeys.Wire.Queued);
            result.RespondSynchronously.Should().BeFalse();
        }

        [Theory]
        [InlineData(RunErrorCode.SandboxStartFailed, 2, 2)]  // retryable, but no attempts left
        [InlineData(RunErrorCode.UserRuntimeError, 1, 2)]    // never retried
        public async Task A_final_failure_is_answered(RunErrorCode code, int attempt, int maxAttempts)
        {
            _version.Trigger.ResponseMode = "sync";
            _runs.Setup(r => r.GetByIdAsync(Tenant, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new FunctionRunEntity
                {
                    ItemId = _created!.ItemId, VersionId = "v-1", Status = RunStatus.Failed,
                    ErrorCode = code, Attempt = attempt, MaxAttempts = maxAttempts,
                });

            var result = await Service().InvokeHttpAsync(Tenant, "fn-1", Call(preferWait: 2));

            result.Status.Should().Be("FAILED");
            result.RespondSynchronously.Should().BeTrue();
        }

        [Fact]
        public void The_sync_wait_uses_the_same_retry_predicate_as_the_result_consumer()
        {
            var retried = new FunctionRunEntity { VersionId = "v", Status = RunStatus.Failed, ErrorCode = RunErrorCode.SandboxStartFailed, Attempt = 1, MaxAttempts = 2 };
            FunctionWireMapping.WillBeRetried(retried).Should().BeTrue();
            FunctionWireMapping.WillBeRetried(new FunctionRunEntity { VersionId = "v", Status = RunStatus.Failed, ErrorCode = RunErrorCode.Abandoned, Attempt = 1, MaxAttempts = 2 })
                .Should().BeFalse("platform-determined outcomes are not retried");
            FunctionWireMapping.WillBeRetried(new FunctionRunEntity { Status = RunStatus.Failed, ErrorCode = RunErrorCode.SandboxStartFailed, Attempt = 1, MaxAttempts = 2 })
                .Should().BeFalse("a test run has no version to retry");
        }

        [Fact]
        public async Task When_every_sync_slot_is_taken_a_call_is_answered_async_at_once()
        {
            _version.Trigger.ResponseMode = "sync";
            RunIs(RunStatus.Running);
            var service = Service(("Functions:MaxConcurrentSyncWaits", "1"));

            // The first call holds the only slot for its whole (1 s) window.
            var holder = service.InvokeHttpAsync(Tenant, "fn-1", Call(preferWait: 1));
            await Task.Delay(100);
            var clock = System.Diagnostics.Stopwatch.StartNew();

            var second = await service.InvokeHttpAsync(Tenant, "fn-1", Call());

            clock.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500), "a full house never waits");
            second.Status.Should().Be(FunctionQueueKeys.Wire.Queued);
            second.RespondSynchronously.Should().BeFalse();
            await holder;

            // The slot is released afterwards: the next sync call waits again.
            RunIs(RunStatus.Succeeded);
            (await service.InvokeHttpAsync(Tenant, "fn-1", Call())).RespondSynchronously.Should().BeTrue();
        }

        // ------------------------------------------------- reuse only for HTTP calls ----

        [Fact]
        public async Task A_workflow_step_never_carries_the_reuse_flag()
        {
            _version.Trigger.ReuseSandbox = true;
            RunIs(RunStatus.Succeeded);

            await Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, null, "wf-1");

            RunEntry().Should().NotContain(e => e.Name == FunctionQueueKeys.RunReuseField);
            _created!.ReuseRequested.Should().BeFalse();
        }

        [Theory]
        [InlineData(InvokedByType.Http, true)]
        [InlineData(InvokedByType.Workflow, false)]
        public async Task A_replay_reuses_only_when_the_original_was_an_http_call(InvokedByType original, bool reuse)
        {
            _version.Trigger.ReuseSandbox = true;

            await Service().ReplayAsync(Tenant, new FunctionRunEntity { ItemId = "orig", FunctionId = "fn-1", InvokedBy = original, Input = "{}" });

            RunEntry().Any(e => e.Name == FunctionQueueKeys.RunReuseField).Should().Be(reuse);
            _created!.ReuseRequested.Should().Be(reuse);
        }

        [Fact]
        public async Task An_http_run_records_that_it_asked_for_reuse_so_a_retry_can_too()
        {
            _version.Trigger.ReuseSandbox = true;

            await Service().InvokeHttpAsync(Tenant, "fn-1", Call());

            _created!.ReuseRequested.Should().BeTrue();
        }

        [Fact]
        public async Task A_caller_leaving_mid_wait_does_not_cancel_or_withdraw_the_run()
        {
            _version.Trigger.ResponseMode = "sync";
            RunIs(RunStatus.Running);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

            var act = () => Service().InvokeHttpAsync(Tenant, "fn-1", Call(), cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            RunEntry().Should().NotBeEmpty("the run was queued before the wait began");
            _redis.Fake.Calls("KeyDeleteAsync").Should().BeEmpty("its payload is not withdrawn");
            _redis.Fake.Calls("StringSetAsync").Select(c => ((RedisKey)c[0]!).ToString())
                .Should().NotContain(k => k.Contains("function:cancel:"), "nothing asks the runner to stop it");
            _runs.Verify(r => r.FailIfNotTerminalAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RunErrorCode>(),
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
