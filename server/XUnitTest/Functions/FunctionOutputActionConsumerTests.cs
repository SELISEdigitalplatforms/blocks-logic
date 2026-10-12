using System.Text.Json;
using Blocks.Genesis;
using FluentAssertions;
using Functions.DomainService.Consumers;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using static Functions.DomainService.Consumers.FunctionOutputActionConsumer;

namespace XUnitTest.Functions
{
    /// <summary>
    /// Output actions fire at most once per (run, attempt, action), whatever happens to the job
    /// carrying them: redelivered, reclaimed while its owner is still working, or reclaimed after
    /// its owner died mid-send. The verdict is written once, only while the run still awaits it.
    /// </summary>
    public class FunctionOutputActionConsumerTests
    {
        private const string Tenant = "tenant-1";
        private const string RunId = "run-1";

        private readonly Mock<IFunctionRunRepository> _runs = new();
        private readonly Mock<IFunctionVersionRepository> _versions = new();
        private readonly Mock<IFunctionRepository> _functions = new();
        private readonly Mock<IOutputActionProcessor> _processor = new();
        private readonly (IDatabase Database, FakeRedisDatabase Fake) _redis = FakeRedisDatabase.Create();
        private readonly Dictionary<string, string> _markers = new(StringComparer.Ordinal);
        private readonly List<string> _sent = [];

        private readonly FunctionVersionEntity _version = new()
        {
            ItemId = "v-1",
            FunctionId = "fn-1",
            OutputActions =
            [
                new OutputAction { Id = "a1", Url = "https://one.test" },
                new OutputAction { Id = "a2", Url = "https://two.test" },
            ],
        };

        private FunctionRunEntity _record = new()
        {
            ItemId = RunId, TenantId = Tenant, FunctionId = "fn-1", VersionId = "v-1",
            Status = RunStatus.OutputProcessing, Attempt = 1, Result = "{}",
        };

        public FunctionOutputActionConsumerTests()
        {
            _runs.Setup(r => r.GetByIdAsync(Tenant, RunId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _record);
            _runs.Setup(r => r.ApplyOutputResultAsync(
                    Tenant, RunId, It.IsAny<int>(), It.IsAny<IReadOnlyList<OutputActionResult>>(), It.IsAny<RunStatus>(),
                    It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            _versions.Setup(v => v.GetByIdAsync(Tenant, "v-1", It.IsAny<CancellationToken>())).ReturnsAsync(_version);
            _processor
                .Setup(p => p.ProcessAsync(Tenant, It.IsAny<FunctionRunEntity>(), It.IsAny<IReadOnlyList<OutputAction>>(),
                    It.IsAny<RetryPolicy>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, FunctionRunEntity _, IReadOnlyList<OutputAction> actions, RetryPolicy _, CancellationToken _) =>
                {
                    actions.Should().ContainSingle("each action is delivered on its own, under its own marker");
                    _sent.Add(actions[0].Id);
                    return new OutputActionChainResult([new OutputActionResult { ActionId = actions[0].Id, Ok = true }], true);
                });

            // SET NX / SET / SET XX semantics over a dictionary, so markers behave like Redis.
            _redis.Fake.On("StringSetAsync", a =>
            {
                var key = a[0]!.ToString();
                var when = a.OfType<When>().FirstOrDefault(When.Always);
                if (when == When.NotExists && _markers.ContainsKey(key)) return false;
                if (when == When.Exists && !_markers.ContainsKey(key)) return false;
                _markers[key] = a[1]!.ToString();
                return true;
            });
            _redis.Fake.On("StringGetAsync", a =>
                _markers.TryGetValue(a[0]!.ToString(), out var v) ? (RedisValue)v : RedisValue.Null);
        }

        private FunctionOutputActionConsumer Consumer()
        {
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(_redis.Database);
            return new FunctionOutputActionConsumer(
                cache.Object, _runs.Object, _versions.Object, _functions.Object, _processor.Object,
                new ConfigurationBuilder().Build(), NullLogger<FunctionOutputActionConsumer>.Instance);
        }

        private static ResultStreamEntry Job(string attempt = "1") => new("1-0", new Dictionary<string, string>
        {
            ["runId"] = RunId, ["tenantId"] = Tenant, ["functionId"] = "fn-1", ["attempt"] = attempt,
        });

        private static string MarkerKey(int index, string id) =>
            FunctionWorkerQueueKeys.OutputActionMarker(RunId, 1, $"{index}-{id}");

        private void Marker(int index, string id, ActionMarker marker) =>
            _markers[MarkerKey(index, id)] = JsonSerializer.Serialize(marker, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        private void VerifyVerdict(RunStatus status, Func<IReadOnlyList<OutputActionResult>, bool>? results = null) =>
            _runs.Verify(r => r.ApplyOutputResultAsync(
                Tenant, RunId, 1, It.Is<IReadOnlyList<OutputActionResult>>(x => results == null || results(x)), status,
                It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

        [Fact]
        public async Task Every_action_is_sent_once_in_order_and_the_verdict_written()
        {
            var disposition = await Consumer().ProcessAsync(Job(), null, CancellationToken.None);

            disposition.Should().Be(OutputDisposition.Done);
            _sent.Should().Equal("a1", "a2");
            VerifyVerdict(RunStatus.Succeeded, r => r.Count == 2);
            _markers[MarkerKey(0, "a1")].Should().Contain("\"state\":\"done\"");
            _redis.Fake.Calls("PublishAsync").Should().ContainSingle();
        }

        [Fact]
        public async Task A_redelivered_job_sends_nothing_again()
        {
            await Consumer().ProcessAsync(Job(), null, CancellationToken.None);
            // The first delivery's verdict write was lost (say the Worker died just before it):
            // the run is still OUTPUT_PROCESSING when the job comes round again.
            await Consumer().ProcessAsync(Job(), null, CancellationToken.None);

            _sent.Should().Equal("a1", "a2");
        }

        [Fact]
        public async Task A_recorded_outcome_is_reused_and_the_chain_resumes_after_it()
        {
            Marker(0, "a1", new ActionMarker { State = ActionMarker.Done, Ok = true, Results = [new OutputActionResult { ActionId = "a1", Ok = true, StatusCode = 201 }] });

            await Consumer().ProcessAsync(Job(), null, CancellationToken.None);

            _sent.Should().Equal("a2");
            VerifyVerdict(RunStatus.Succeeded, r => r.Count == 2 && r[0].StatusCode == 201);
        }

        [Fact]
        public async Task A_recorded_failure_stops_the_chain_without_resending()
        {
            Marker(0, "a1", new ActionMarker { State = ActionMarker.Done, Ok = false, Results = [new OutputActionResult { ActionId = "a1", Ok = false }] });

            await Consumer().ProcessAsync(Job(), null, CancellationToken.None);

            _sent.Should().BeEmpty();
            VerifyVerdict(RunStatus.OutputFailed);
        }

        [Fact]
        public async Task An_action_another_live_worker_is_sending_is_left_to_it()
        {
            Marker(0, "a1", new ActionMarker { State = ActionMarker.InFlight, Owner = "other", HeartbeatAt = DateTimeOffset.UtcNow });

            var disposition = await Consumer().ProcessAsync(Job(), null, CancellationToken.None);

            disposition.Should().Be(OutputDisposition.InFlightElsewhere);
            _sent.Should().BeEmpty();
            _runs.Verify(r => r.ApplyOutputResultAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<OutputActionResult>>(),
                It.IsAny<RunStatus>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task An_action_whose_sender_died_mid_send_is_failed_not_resent()
        {
            Marker(0, "a1", new ActionMarker { State = ActionMarker.InFlight, Owner = "dead", HeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-10) });

            var disposition = await Consumer().ProcessAsync(Job(), null, CancellationToken.None);

            disposition.Should().Be(OutputDisposition.Done);
            _sent.Should().BeEmpty();
            VerifyVerdict(RunStatus.OutputFailed, r => r.Count == 1 && r[0].Error!.Contains("outcome unknown", StringComparison.Ordinal));
        }

        [Fact]
        public async Task An_unreadable_marker_is_never_taken_as_permission_to_send()
        {
            _markers[MarkerKey(0, "a1")] = "not json";

            await Consumer().ProcessAsync(Job(), null, CancellationToken.None);

            _sent.Should().BeEmpty();
            VerifyVerdict(RunStatus.OutputFailed);
        }

        [Fact]
        public async Task The_first_failure_stops_the_chain()
        {
            _processor
                .Setup(p => p.ProcessAsync(Tenant, It.IsAny<FunctionRunEntity>(), It.IsAny<IReadOnlyList<OutputAction>>(),
                    It.IsAny<RetryPolicy>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, FunctionRunEntity _, IReadOnlyList<OutputAction> actions, RetryPolicy _, CancellationToken _) =>
                {
                    _sent.Add(actions[0].Id);
                    return new OutputActionChainResult([new OutputActionResult { ActionId = actions[0].Id, Ok = false }], false);
                });

            await Consumer().ProcessAsync(Job(), null, CancellationToken.None);

            _sent.Should().Equal("a1");
            VerifyVerdict(RunStatus.OutputFailed, r => r.Count == 1);
        }

        [Fact]
        public async Task Disabled_actions_take_no_marker_and_are_not_sent()
        {
            _version.OutputActions[0].Enabled = false;

            await Consumer().ProcessAsync(Job(), null, CancellationToken.None);

            _sent.Should().Equal("a2");
            _markers.Should().NotContainKey(MarkerKey(0, "a1"));
        }

        [Fact]
        public async Task Two_actions_sharing_an_id_do_not_suppress_each_other()
        {
            _version.OutputActions[1].Id = "a1";

            await Consumer().ProcessAsync(Job(), null, CancellationToken.None);

            _sent.Should().Equal("a1", "a1");
        }

        [Theory]
        [InlineData(RunStatus.Succeeded)]
        [InlineData(RunStatus.OutputFailed)]
        [InlineData(RunStatus.Queued)]
        public async Task A_run_no_longer_awaiting_output_is_left_alone(RunStatus status)
        {
            _record.Status = status;

            var disposition = await Consumer().ProcessAsync(Job(), null, CancellationToken.None);

            disposition.Should().Be(OutputDisposition.Done);
            _sent.Should().BeEmpty();
        }

        [Fact]
        public async Task A_job_for_a_superseded_attempt_is_left_alone()
        {
            _record.Attempt = 2;

            await Consumer().ProcessAsync(Job("1"), null, CancellationToken.None);

            _sent.Should().BeEmpty();
        }

        [Theory]
        [InlineData("")]
        [InlineData("0")]
        [InlineData("x")]
        public async Task A_malformed_job_is_dropped_without_sending(string attempt)
        {
            var disposition = await Consumer().ProcessAsync(Job(attempt), null, CancellationToken.None);

            disposition.Should().Be(OutputDisposition.Done);
            _sent.Should().BeEmpty();
        }

        [Fact]
        public async Task A_lost_verdict_race_publishes_nothing()
        {
            _runs.Setup(r => r.ApplyOutputResultAsync(
                    Tenant, RunId, It.IsAny<int>(), It.IsAny<IReadOnlyList<OutputActionResult>>(), It.IsAny<RunStatus>(),
                    It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            await Consumer().ProcessAsync(Job(), null, CancellationToken.None);

            _redis.Fake.Calls("PublishAsync").Should().BeEmpty();
        }

        [Fact]
        public async Task A_long_delivery_keeps_its_marker_and_stream_entry_alive()
        {
            var consumer = Consumer();
            consumer.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
            var kept = 0;
            _processor
                .Setup(p => p.ProcessAsync(Tenant, It.IsAny<FunctionRunEntity>(), It.IsAny<IReadOnlyList<OutputAction>>(),
                    It.IsAny<RetryPolicy>(), It.IsAny<CancellationToken>()))
                .Returns(async (string _, FunctionRunEntity _, IReadOnlyList<OutputAction> actions, RetryPolicy _, CancellationToken _) =>
                {
                    await Task.Delay(300);
                    return new OutputActionChainResult([new OutputActionResult { ActionId = actions[0].Id, Ok = true }], true);
                });

            await consumer.ProcessAsync(Job(), () => { Interlocked.Increment(ref kept); return Task.CompletedTask; }, CancellationToken.None);

            kept.Should().BeGreaterThan(1);
            // The heartbeat never outlives the send: the last write to the marker is the outcome.
            _markers[MarkerKey(1, "a2")].Should().Contain("\"state\":\"done\"");
        }
    }
}
