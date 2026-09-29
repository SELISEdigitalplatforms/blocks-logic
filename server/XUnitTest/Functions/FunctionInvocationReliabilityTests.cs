using System.Diagnostics;
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
    /// The invoke path's failure modes: a run whose enqueue fails must be closed and reported as
    /// 503 rather than left QUEUED for ever, a caller disconnecting must never strand a half-queued
    /// run, and the synchronous wait must be bounded, woken by the completion notification,
    /// cheap on the database while it waits, and cancellable.
    /// </summary>
    public class FunctionInvocationReliabilityTests
    {
        private const string Tenant = "tenant-1";

        private readonly Mock<IFunctionRepository> _functions = new();
        private readonly Mock<IFunctionVersionRepository> _versions = new();
        private readonly Mock<IFunctionRunRepository> _runs = new();
        private readonly Mock<IFunctionRunStatsRepository> _stats = new();
        private readonly Mock<IFunctionAdmissionService> _admission = new();
        private readonly Mock<IFunctionAuthorizationService> _authorization = new();
        private readonly Mock<IFunctionBuildService> _builds = new();
        private readonly Mock<ISubscriber> _subscriber = new();
        private readonly (IDatabase Database, FakeRedisDatabase Fake) _redis = FakeRedisDatabase.Create();

        private readonly FunctionEntity _function = new()
        {
            ItemId = "fn-1",
            Status = FunctionStatus.Live,
            ActiveVersionId = "v-1",
        };

        private readonly FunctionVersionEntity _version = new()
        {
            ItemId = "v-1",
            FunctionId = "fn-1",
            Number = 1,
            ImageDigest = "sha256:abc",
        };

        private FunctionRunEntity? _created;
        private Action<RedisChannel, RedisValue>? _notify;

        public FunctionInvocationReliabilityTests()
        {
            _version.Limits.TimeoutSeconds = 60;
            _function.Limits.TimeoutSeconds = 60;

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
                .Setup(b => b.EnsureImageAsync(Tenant, It.IsAny<FunctionEntity>(), It.IsAny<CancellationToken>(), It.IsAny<int?>(), It.IsAny<bool>()))
                .ReturnsAsync(new FunctionBuildEntity { ItemId = "b-1", Status = BuildStatus.Succeeded, ImageDigest = "sha256:test" });

            var multiplexer = new Mock<IConnectionMultiplexer>();
            multiplexer.Setup(m => m.GetSubscriber(It.IsAny<object?>())).Returns(_subscriber.Object);
            _redis.Fake.On("get_Multiplexer", _ => multiplexer.Object);
            _subscriber
                .Setup(s => s.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
                .Callback((RedisChannel _, Action<RedisChannel, RedisValue> handler, CommandFlags _) => _notify = handler)
                .Returns(Task.CompletedTask);
        }

        private FunctionInvocationService Service(params (string Key, string Value)[] settings)
        {
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(_redis.Database);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
                .Build();

            return new FunctionInvocationService(
                _functions.Object, _versions.Object, _runs.Object, _stats.Object, _admission.Object,
                _authorization.Object, _builds.Object, new Mock<IEndpointAccessAuthorizer>().Object,
                new HttpContextAccessor(), cache.Object, configuration, NullLogger<FunctionInvocationService>.Instance);
        }

        private void RunIs(Func<FunctionRunEntity?> current) =>
            _runs.Setup(r => r.GetByIdAsync(Tenant, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => current());

        private static FunctionRunEntity Run(RunStatus status) => new() { ItemId = "run", Status = status, Result = "{\"ok\":true}" };

        // ---- secret references stay references in the queue -----------------------

        /// <summary>
        /// Everything the invoke path wrote to Redis for one run — the run-record hash and the
        /// stream entry — flattened to text, so an assertion can cover all of it at once.
        /// </summary>
        private string EverythingEnqueued()
        {
            var parts = new List<string>();
            foreach (var call in _redis.Fake.Calls("HashSetAsync"))
            {
                if (call[1] is HashEntry[] entries) parts.AddRange(entries.Select(e => $"{e.Name}={e.Value}"));
            }
            foreach (var call in _redis.Fake.Calls("StreamAddAsync"))
            {
                if (call[1] is NameValueEntry[] entries) parts.AddRange(entries.Select(e => $"{e.Name}={e.Value}"));
            }
            return string.Join("\n", parts);
        }

        private async Task InvokeUntilQueuedAsync()
        {
            // The wait is abandoned as soon as the record exists; the enqueue still completes,
            // which is the part under test.
            using var cts = new CancellationTokenSource();
            _runs
                .Setup(r => r.CreateAsync(Tenant, It.IsAny<FunctionRunEntity>(), It.IsAny<CancellationToken>()))
                .Callback((string _, FunctionRunEntity run, CancellationToken _) =>
                {
                    _created = run;
                    cts.Cancel();
                })
                .Returns(Task.CompletedTask);

            var act = () => Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, null, "wf-1", cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task The_queued_payload_carries_secret_references_and_never_a_resolved_value()
        {
            _version.Variables =
            [
                new VariableBinding { Key = "STRIPE_API_KEY", Value = "{{secret.sec_stripe}}" },
                new VariableBinding { Key = "AUTH", Value = "Bearer {{secret.sec_token}}" },
                new VariableBinding { Key = "API_BASE", Value = "https://api.example.com" },
            ];

            await InvokeUntilQueuedAsync();

            var enqueued = EverythingEnqueued();
            enqueued.Should().Contain("{{secret.sec_stripe}}").And.Contain("Bearer {{secret.sec_token}}");

            using var envelope = System.Text.Json.JsonDocument.Parse(
                _redis.Fake.Calls("HashSetAsync").SelectMany(c => (HashEntry[])c[1]!)
                    .Single(e => e.Name == "envelope").Value.ToString());
            envelope.RootElement.GetProperty("env").GetProperty("STRIPE_API_KEY").GetString()
                .Should().Be("{{secret.sec_stripe}}");
            envelope.RootElement.GetProperty("maskedEnv").EnumerateArray().Select(e => e.GetString())
                .Should().BeEquivalentTo(["STRIPE_API_KEY", "AUTH"]);
            envelope.RootElement.TryGetProperty("maskedValues", out _).Should().BeFalse();
        }

        [Fact]
        public async Task The_run_entry_announces_the_protocol_an_old_runner_refuses()
        {
            // An old runner dead-letters anything but protocol 1, which is the guard against it
            // executing a function with "{{secret.x}}" where its key should be.
            await InvokeUntilQueuedAsync();

            var entry = _redis.Fake.Calls("StreamAddAsync").Single()[1] as NameValueEntry[];
            entry!.Single(e => e.Name == "protocol").Value.ToString()
                .Should().Be(FunctionQueueKeys.RunProtocolVersion.ToString());
            FunctionQueueKeys.RunProtocolVersion.Should().BeGreaterThan(FunctionQueueKeys.ProtocolVersion);
        }

        [Fact]
        public async Task A_reference_to_a_secret_that_may_not_exist_is_queued_not_refused()
        {
            // No secret store is consulted at invoke any more; the runner fails such a run as
            // SecretUnresolved, naming the variable, before its sandbox starts.
            _version.Variables = [new VariableBinding { Key = "TOKEN", Value = "{{secret.deleted_long_ago}}" }];

            await InvokeUntilQueuedAsync();

            _created.Should().NotBeNull();
            _redis.Fake.Calls("StreamAddAsync").Should().ContainSingle();
        }

        // ---- enqueue compensation -------------------------------------------------

        [Fact]
        public async Task A_failed_enqueue_closes_the_run_and_answers_unavailable()
        {
            _redis.Fake.On("StreamAddAsync", _ => throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "down"));

            var act = () => Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, null, "wf-1");

            var thrown = await act.Should().ThrowAsync<FunctionUnavailableException>();
            thrown.Which.RunId.Should().Be(_created!.ItemId);

            _runs.Verify(r => r.FailIfNotTerminalAsync(
                Tenant, _created.ItemId, RunErrorCode.EnqueueFailed,
                It.Is<string>(m => m.Contains("nothing was executed", StringComparison.Ordinal)),
                It.IsAny<DateTime>(), CancellationToken.None), Times.Once);

            // The payload is withdrawn, so an entry that did land cannot run it.
            _redis.Fake.Calls("KeyDeleteAsync")
                .Should().ContainSingle(a => a[0]!.ToString() == FunctionQueueKeys.Run(_created.ItemId));

            // Counted like every other run that has a record.
            _stats.Verify(s => s.RecordRunStartedAsync(Tenant, "fn-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);

            // No waiting on a run that was never queued.
            _runs.Verify(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task A_payload_write_failure_is_compensated_the_same_way()
        {
            _redis.Fake.On("HashSetAsync", _ => throw new RedisTimeoutException("timeout", CommandStatus.Unknown));

            var act = () => Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, null, "wf-1");

            await act.Should().ThrowAsync<FunctionUnavailableException>();
            _runs.Verify(r => r.FailIfNotTerminalAsync(
                Tenant, It.IsAny<string>(), RunErrorCode.EnqueueFailed, It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
            _redis.Fake.Calls("StreamAddAsync").Should().BeEmpty();
        }

        [Fact]
        public async Task The_caller_still_gets_503_when_closing_the_run_fails_too()
        {
            // Mongo down as well: the sweeper closes the record later, but the caller must still
            // be told "unavailable", not handed the Mongo exception as a 500.
            _redis.Fake.On("StreamAddAsync", _ => throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "down"));
            _redis.Fake.On("KeyDeleteAsync", _ => throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "down"));
            _runs.Setup(r => r.FailIfNotTerminalAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RunErrorCode>(), It.IsAny<string>(),
                    It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("mongo"));

            var act = () => Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, null, "wf-1");

            await act.Should().ThrowAsync<FunctionUnavailableException>();
        }

        [Fact]
        public async Task A_caller_disconnecting_after_the_record_exists_cannot_strand_it()
        {
            using var cts = new CancellationTokenSource();
            _runs
                .Setup(r => r.CreateAsync(Tenant, It.IsAny<FunctionRunEntity>(), It.IsAny<CancellationToken>()))
                .Callback((string _, FunctionRunEntity run, CancellationToken _) =>
                {
                    _created = run;
                    cts.Cancel();
                })
                .Returns(Task.CompletedTask);

            var act = () => Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, null, "wf-1", cts.Token);

            // The wait is abandoned — the caller has gone — but the run was fully queued first.
            await act.Should().ThrowAsync<OperationCanceledException>();
            _redis.Fake.Calls("StreamAddAsync").Should().ContainSingle();
            _runs.Verify(r => r.CreateAsync(Tenant, It.IsAny<FunctionRunEntity>(), CancellationToken.None), Times.Once);
        }

        [Fact]
        public async Task A_caller_already_gone_before_the_record_creates_nothing()
        {
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            var act = () => Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, null, "wf-1", cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            _created.Should().BeNull();
            _redis.Fake.Calls("StreamAddAsync").Should().BeEmpty();
        }

        // ---- synchronous wait ---------------------------------------------------------

        [Fact]
        public async Task A_terminal_run_is_returned_with_its_result()
        {
            RunIs(() => Run(RunStatus.Succeeded));

            var result = await Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, null, "wf-1");

            result.Status.Should().Be(FunctionQueueKeys.Wire.Succeeded);
            result.Result.Should().Be("{\"ok\":true}");
        }

        [Fact]
        public async Task The_http_facing_wait_is_capped_low_and_returns_the_run_id_to_poll()
        {
            RunIs(() => Run(RunStatus.Queued));

            var stopwatch = Stopwatch.StartNew();
            var result = await Service(("Functions:HttpSyncWaitMaxSeconds", "1"), ("Functions:SyncWaitMaxSeconds", "180"))
                .TestAsync(Tenant, "fn-1", new TestFunctionRequestDto { FunctionId = "fn-1", InputJson = "{}" });
            stopwatch.Stop();

            result.Status.Should().Be(FunctionQueueKeys.Wire.Queued);
            result.RunId.Should().Be(_created!.ItemId);
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(4));
        }

        [Fact]
        public void The_default_http_cap_sits_under_a_60_second_ingress_timeout()
        {
            FunctionInvocationService.DefaultHttpSyncWaitMaxSeconds.Should().BeLessThanOrEqualTo(30);
        }

        [Fact]
        public async Task A_workflow_wait_is_not_held_to_the_http_cap()
        {
            // The workflow step runs in the Worker with no ingress in front of it. Queued for
            // ~1.5 s, then done: an HTTP cap of 1 s would have given up.
            var sw = Stopwatch.StartNew();
            RunIs(() => sw.Elapsed > TimeSpan.FromMilliseconds(1500) ? Run(RunStatus.Succeeded) : Run(RunStatus.Queued));

            var result = await Service(("Functions:HttpSyncWaitMaxSeconds", "1"), ("Functions:SyncWaitMaxSeconds", "10"))
                .InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, 5, "wf-1");

            result.Status.Should().Be(FunctionQueueKeys.Wire.Succeeded);
        }

        [Fact]
        public async Task The_completion_notification_wakes_the_wait_before_the_next_poll()
        {
            var done = false;
            RunIs(() => done ? Run(RunStatus.Succeeded) : Run(RunStatus.Queued));

            var sw = Stopwatch.StartNew();
            var waiting = Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, 30, "wf-1");
            await Task.Delay(150);
            done = true;
            _notify.Should().NotBeNull("the wait subscribes before its first read");
            _notify!(RedisChannel.Literal("x"), "SUCCEEDED");

            var result = await waiting;
            sw.Stop();

            result.Status.Should().Be(FunctionQueueKeys.Wire.Succeeded);
            // Subscribed, the timer's first re-read is a full second out; the notification got
            // there well before it.
            sw.Elapsed.Should().BeLessThan(FunctionInvocationService.SubscribedPollInitial);
            _subscriber.Verify(s => s.UnsubscribeAsync(
                It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>?>(), It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public async Task Without_pub_sub_the_wait_still_completes_by_polling()
        {
            _subscriber
                .Setup(s => s.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.SocketFailure, "no pubsub"));
            var sw = Stopwatch.StartNew();
            RunIs(() => sw.Elapsed > TimeSpan.FromMilliseconds(500) ? Run(RunStatus.Failed) : Run(RunStatus.Queued));

            var result = await Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, 5, "wf-1");

            result.Status.Should().Be(FunctionQueueKeys.Wire.Failed);
        }

        [Fact]
        public async Task Polling_backs_off_instead_of_reading_five_times_a_second()
        {
            _subscriber
                .Setup(s => s.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.SocketFailure, "no pubsub"));
            var reads = 0;
            _runs.Setup(r => r.GetByIdAsync(Tenant, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback(() => Interlocked.Increment(ref reads))
                .ReturnsAsync(Run(RunStatus.Queued));

            await Service(("Functions:HttpSyncWaitMaxSeconds", "3"))
                .TestAsync(Tenant, "fn-1", new TestFunctionRequestDto { FunctionId = "fn-1", InputJson = "{}" });

            // A fixed 200 ms poll would read ~15 times in 3 s; 200→400→800→1000… reads ~6.
            reads.Should().BeInRange(3, 8);
        }

        [Fact]
        public async Task Cancelling_the_request_ends_the_wait_and_unsubscribes()
        {
            RunIs(() => Run(RunStatus.Queued));
            using var cts = new CancellationTokenSource();

            var waiting = Service().InvokeFromWorkflowAsync(Tenant, "fn-1", "{}", null, 30, "wf-1", cts.Token);
            await Task.Delay(100);
            var sw = Stopwatch.StartNew();
            await cts.CancelAsync();

            await FluentActions.Awaiting(() => waiting).Should().ThrowAsync<OperationCanceledException>();
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
            _subscriber.Verify(s => s.UnsubscribeAsync(
                It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>?>(), It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public async Task A_run_the_runner_has_started_is_reported_running_when_the_wait_lapses()
        {
            RunIs(() => Run(RunStatus.Queued));
            _redis.Fake.On("HashGetAsync", _ => (RedisValue)FunctionQueueKeys.Wire.Running);

            var result = await Service(("Functions:HttpSyncWaitMaxSeconds", "1"))
                .TestAsync(Tenant, "fn-1", new TestFunctionRequestDto { FunctionId = "fn-1", InputJson = "{}", WaitTimeoutSeconds = 1 });

            result.Status.Should().Be(FunctionQueueKeys.Wire.Running);
        }
    }
}
