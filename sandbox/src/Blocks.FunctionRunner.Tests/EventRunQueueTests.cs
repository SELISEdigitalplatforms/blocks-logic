using System.Text.Json;
using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Health;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Protocol;
using Blocks.FunctionRunner.Redis;
using Blocks.FunctionRunner.Runs;
using Blocks.FunctionRunner.Sandbox;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The runner side of function-event runs against a real Redis (skipping cleanly without one; set TEST_REDIS): the two
    /// streams are read by the 1:5 rule, an event run that waited past its limit is not started and fails as
    /// <c>EVENT_EXPIRED</c>, and an event run inside its limit runs like any other.
    /// </summary>
    [Collection("redis-key-prefix")]
    public sealed class EventRunQueueTests : IAsyncLifetime
    {
        private ConnectionMultiplexer? _redis;
        private IDatabase? _db;
        private readonly string _runsDir = Path.Combine(Path.GetTempPath(), $"fn-runs-{Guid.NewGuid():N}");
        private readonly List<string> _runIds = [];

        public async Task InitializeAsync()
        {
            var host = Environment.GetEnvironmentVariable("TEST_REDIS") ?? "127.0.0.1:6379";
            try
            {
                var config = ConfigurationOptions.Parse(host);
                config.ConnectTimeout = 1500;
                config.AbortOnConnectFail = false;
                _redis = await ConnectionMultiplexer.ConnectAsync(config);
                if (!_redis.IsConnected) { _redis = null; return; }
                _db = _redis.GetDatabase();
                await _db.PingAsync();
                await _db.KeyDeleteAsync([RedisKeys.RunsStream, RedisKeys.EventRunsStream, RedisKeys.ResultsStream]);
            }
            catch (RedisException)
            {
                _redis = null;
                _db = null;
            }
        }

        public async Task DisposeAsync()
        {
            if (_db is not null)
            {
                await _db.KeyDeleteAsync([RedisKeys.RunsStream, RedisKeys.EventRunsStream, RedisKeys.ResultsStream]);
                foreach (var id in _runIds)
                {
                    await _db.KeyDeleteAsync([RedisKeys.Run(id), RedisKeys.Lease(id), RedisKeys.Result(id), RedisKeys.Logs(id)]);
                }
            }
            if (_redis is not null) await _redis.DisposeAsync();
            if (Directory.Exists(_runsDir)) Directory.Delete(_runsDir, recursive: true);
        }

        private bool Unavailable => _db is null;

        private sealed class CountingSandbox : ISandbox
        {
            private int _calls;
            private readonly List<string> _order = [];

            public int Calls => _calls;

            /// <summary>The runs that reached the sandbox, in the order they started.</summary>
            public IReadOnlyList<string> Order { get { lock (_order) return [.. _order]; } }

            public Task<SandboxResult> RunAsync(
                string runId, string image, string envelopeHostPath, RunLimits limits, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _calls);
                lock (_order) _order.Add(runId);
                return Task.FromResult(new SandboxResult
                {
                    Output = new SandboxOutput { Ok = true, ResultJson = "1" }, ExitCode = 0, OomKilled = false, TimedOut = false, DurationMs = 1,
                });
            }
        }

        private sealed class ResolvesAnything : IImageResolver
        {
            public Task<string?> EnsureAsync(
                string reference, CancellationToken token, string? artifactUrl = null, string? artifactSha256 = null) =>
                Task.FromResult<string?>(reference);
        }


        private (RunConsumerService Service, CountingSandbox Sandbox) Service(int maxParallelRuns, int eventMaxWaitSeconds = 300)
        {
            var options = Microsoft.Extensions.Options.Options.Create(new RunnerOptions
            {
                RunnerId = $"test-ev-{Guid.NewGuid():N}",
                RunsDir = _runsDir,
                MaxParallelRuns = maxParallelRuns,
                EventRunMaxWaitSeconds = eventMaxWaitSeconds,
                // Instant fake runs: the start-rate gate must not defer them, or the claim order is not what runs.
            });
            var sandbox = new CountingSandbox();
            var budget = new HostRoom(options, new RoomyHost(), NullLogger<HostRoom>.Instance);
            var processor = new RunProcessor(_db!, sandbox, new ResolvesAnything(), budget,
                new FakeRunSecretResolver(), new FakeRunAccessTokenResolver(), options, NullLogger<RunProcessor>.Instance)
            {
                EnvelopeGroupHandoff = (_, _) => { },
            };
            var heartbeat = new HeartbeatService(_db!, budget,
                new StartupGuard(null!, _db!, options, NullLogger<StartupGuard>.Instance),
                options, NullLogger<HeartbeatService>.Instance);
            return (new RunConsumerService(_db!, processor, heartbeat, budget, options, NullLogger<RunConsumerService>.Instance), sandbox);
        }

        /// <summary>Queues one run on a stream; <paramref name="firstQueuedAgo"/> back-dates when it first waited.</summary>
        private async Task<string> QueueAsync(string stream, string prefix, TimeSpan? firstQueuedAgo = null)
        {
            var id = $"{prefix}_{Guid.NewGuid():N}";
            _runIds.Add(id);
            var envelope = JsonSerializer.Serialize(new
            {
                run = new { id, attempt = 1 },
                context = new { tenantId = $"t_{id}" },
                env = new { },
                input = new { },
                limits = new { timeoutMs = 5000 },
            });
            await _db!.HashSetAsync(RedisKeys.Run(id), [new("envelope", envelope), new("status", RunStatuses.Queued)]);
            await _db.KeyExpireAsync(RedisKeys.Run(id), TimeSpan.FromMinutes(5));
            var fields = new List<NameValueEntry>
            {
                new("runId", id), new("functionId", $"fn_{id}"), new("tenantId", $"t_{id}"),
                new("image", "img@sha256:abc"), new("attempt", 1), new("protocol", RedisKeys.RunProtocolVersion),
            };
            if (firstQueuedAgo is { } ago)
            {
                fields.Add(new(RedisKeys.RunFirstQueuedField,
                    DateTimeOffset.UtcNow.Subtract(ago).ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
            await _db.StreamAddAsync(stream, [.. fields]);
            return id;
        }

        private async Task<List<(string RunId, string Status, string? Code, string? Message)>> ResultsAsync() =>
            [.. (await _db!.StreamRangeAsync(RedisKeys.ResultsStream, "-", "+"))
                .Where(e => e.Values.Any(v => v.Name == "runId" && _runIds.Contains(v.Value.ToString())))
                .Select(e =>
                {
                    string? Get(string n) => e.Values.Where(v => v.Name == n).Select(v => (string?)v.Value.ToString()).FirstOrDefault();
                    return (Get("runId")!, Get("status")!, Get("errorCode"), Get("errorMessage"));
                })];

        private async Task WaitForResultsAsync(int count)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while ((await ResultsAsync()).Count < count && watch.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(25);
        }

        // ------------------------------------------------------------- the 1:5 reading ----

        [SkippableFact]
        public async Task Reading_with_both_queues_full_takes_five_runs_then_one_event_run()
        {
            Skip.If(Unavailable, "no Redis available");
            var runs = new GroupConsumer(_db!, NullLogger.Instance, RedisKeys.RunsStream, RedisKeys.RunnerGroup, "a");
            var events = new GroupConsumer(_db!, NullLogger.Instance, RedisKeys.EventRunsStream, RedisKeys.RunnerGroup, "a");
            await runs.EnsureGroupAsync();
            await events.EnsureGroupAsync();
            for (var i = 0; i < 12; i++) await _db!.StreamAddAsync(RedisKeys.RunsStream, [new NameValueEntry("runId", $"h{i}")]);
            for (var i = 0; i < 3; i++) await _db!.StreamAddAsync(RedisKeys.EventRunsStream, [new NameValueEntry("runId", $"e{i}")]);
            var selector = new RunSourceSelector();

            var order = new List<string>();
            for (var turn = 0; turn < 6; turn++)
            {
                var batch = await RunConsumerService.ReadNextAsync(runs, events, selector, count: 16, CancellationToken.None);
                order.AddRange(batch.Select(b => b.Entry.Get("runId")![0].ToString()));
            }

            // 5 H, then E, 5 H, then E, then 2 H (runs empty) and the last E on the free slots.
            string.Concat(order).Should().Be("hhhhh" + "e" + "hhhhh" + "e" + "hh" + "e");
        }

        [SkippableFact]
        public async Task When_the_runs_stream_is_empty_events_use_every_free_slot()
        {
            Skip.If(Unavailable, "no Redis available");
            var runs = new GroupConsumer(_db!, NullLogger.Instance, RedisKeys.RunsStream, RedisKeys.RunnerGroup, "a");
            var events = new GroupConsumer(_db!, NullLogger.Instance, RedisKeys.EventRunsStream, RedisKeys.RunnerGroup, "a");
            await runs.EnsureGroupAsync();
            await events.EnsureGroupAsync();
            for (var i = 0; i < 9; i++) await _db!.StreamAddAsync(RedisKeys.EventRunsStream, [new NameValueEntry("runId", $"e{i}")]);

            var batch = await RunConsumerService.ReadNextAsync(runs, events, new RunSourceSelector(), count: 16, CancellationToken.None);

            batch.Should().HaveCount(9);
            batch.Should().OnlyContain(b => b.Source == events);
        }

        [SkippableFact]
        public async Task When_the_event_stream_is_empty_runs_use_every_free_slot_without_asking_for_events_every_turn()
        {
            Skip.If(Unavailable, "no Redis available");
            var runs = new GroupConsumer(_db!, NullLogger.Instance, RedisKeys.RunsStream, RedisKeys.RunnerGroup, "a");
            var events = new GroupConsumer(_db!, NullLogger.Instance, RedisKeys.EventRunsStream, RedisKeys.RunnerGroup, "a");
            await runs.EnsureGroupAsync();
            await events.EnsureGroupAsync();
            for (var i = 0; i < 30; i++) await _db!.StreamAddAsync(RedisKeys.RunsStream, [new NameValueEntry("runId", $"h{i}")]);
            var selector = new RunSourceSelector();

            var first = await RunConsumerService.ReadNextAsync(runs, events, selector, count: 16, CancellationToken.None);
            var second = await RunConsumerService.ReadNextAsync(runs, events, selector, count: 16, CancellationToken.None);   // event due, found empty
            var third = await RunConsumerService.ReadNextAsync(runs, events, selector, count: 16, CancellationToken.None);    // empty period: all runs

            first.Should().HaveCount(5);
            second.Should().HaveCount(16).And.OnlyContain(b => b.Source == runs);
            third.Should().HaveCount(9);
        }

        [SkippableFact]
        public async Task Expired_event_entries_do_not_use_the_event_turn_the_next_live_one_is_read_at_once()
        {
            Skip.If(Unavailable, "no Redis available");
            var runs = new GroupConsumer(_db!, NullLogger.Instance, RedisKeys.RunsStream, RedisKeys.RunnerGroup, "a");
            var events = new GroupConsumer(_db!, NullLogger.Instance, RedisKeys.EventRunsStream, RedisKeys.RunnerGroup, "a");
            await runs.EnsureGroupAsync();
            await events.EnsureGroupAsync();
            var old = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
            for (var i = 0; i < 20; i++) await _db!.StreamAddAsync(RedisKeys.RunsStream, [new NameValueEntry("runId", $"h{i}")]);
            for (var i = 0; i < 2; i++)
                await _db!.StreamAddAsync(RedisKeys.EventRunsStream, [new NameValueEntry("runId", $"x{i}"), new NameValueEntry(RedisKeys.RunFirstQueuedField, old)]);
            await _db!.StreamAddAsync(RedisKeys.EventRunsStream, [new NameValueEntry("runId", "e0")]);
            var selector = new RunSourceSelector();
            selector.TookRuns(5);

            var batch = await RunConsumerService.ReadNextAsync(runs, events, selector, count: 16, CancellationToken.None, eventMaxWaitSeconds: 300);

            // Both expired entries (failed by their handler), then the live one, then the runs.
            string.Concat(batch.Select(b => b.Entry.Get("runId")![0])).Should().StartWith("xxe");
            selector.Behind.Should().BeFalse("the live event run had just been queued");
        }

        [SkippableFact]
        public async Task A_long_waiting_event_run_moves_the_share_to_one_event_per_two_runs()
        {
            Skip.If(Unavailable, "no Redis available");
            var runs = new GroupConsumer(_db!, NullLogger.Instance, RedisKeys.RunsStream, RedisKeys.RunnerGroup, "a");
            var events = new GroupConsumer(_db!, NullLogger.Instance, RedisKeys.EventRunsStream, RedisKeys.RunnerGroup, "a");
            await runs.EnsureGroupAsync();
            await events.EnsureGroupAsync();
            var waited = DateTimeOffset.UtcNow.AddMinutes(-3).ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
            for (var i = 0; i < 20; i++) await _db!.StreamAddAsync(RedisKeys.RunsStream, [new NameValueEntry("runId", $"h{i}")]);
            for (var i = 0; i < 3; i++)
                await _db!.StreamAddAsync(RedisKeys.EventRunsStream, [new NameValueEntry("runId", $"e{i}"), new NameValueEntry(RedisKeys.RunFirstQueuedField, waited)]);
            var selector = new RunSourceSelector();

            var order = new List<string>();
            for (var turn = 0; turn < 4; turn++)
            {
                var batch = await RunConsumerService.ReadNextAsync(runs, events, selector, count: 16, CancellationToken.None, eventMaxWaitSeconds: 300);
                order.AddRange(batch.Select(b => b.Entry.Get("runId")![0].ToString()));
            }

            // 5 H, then the first event (waited 3 of 5 minutes → behind), then 2 H per event.
            string.Concat(order).Should().Be("hhhhh" + "ehh" + "ehh" + "ehh");
        }

        // ------------------------------------------------------------- end to end ----

        [SkippableFact]
        public async Task An_event_run_inside_its_wait_limit_runs_like_any_other_run()
        {
            Skip.If(Unavailable, "no Redis available");
            var (service, sandbox) = Service(maxParallelRuns: 0);
            using var _ = service;
            var id = await QueueAsync(RedisKeys.EventRunsStream, "run_ev", firstQueuedAgo: TimeSpan.FromSeconds(5));
            await service.StartAsync(CancellationToken.None);
            try
            {
                await WaitForResultsAsync(1);
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
            }

            var results = await ResultsAsync();
            results.Should().ContainSingle().Which.Should().Match<(string RunId, string Status, string? Code, string? Message)>(r => r.RunId == id && r.Status == RunStatuses.Succeeded);
            sandbox.Calls.Should().Be(1);
        }

        [SkippableFact]
        public async Task An_event_run_that_waited_past_the_limit_is_not_started_and_fails_as_EVENT_EXPIRED()
        {
            Skip.If(Unavailable, "no Redis available");
            var (service, sandbox) = Service(maxParallelRuns: 0, eventMaxWaitSeconds: 10);
            using var _ = service;
            var id = await QueueAsync(RedisKeys.EventRunsStream, "run_ev", firstQueuedAgo: TimeSpan.FromSeconds(60));
            await service.StartAsync(CancellationToken.None);
            try
            {
                await WaitForResultsAsync(1);
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
            }

            var result = (await ResultsAsync()).Should().ContainSingle().Subject;
            result.RunId.Should().Be(id);
            result.Status.Should().Be(RunStatuses.Failed);
            result.Code.Should().Be(ErrorCodes.EventExpired).And.Be("EVENT_EXPIRED");
            result.Message.Should().StartWith("EVENT_EXPIRED");
            sandbox.Calls.Should().Be(0, "an expired event run never reaches a sandbox");
            (await _db!.StreamLengthAsync(RedisKeys.EventRunsStream)).Should().Be(0, "the entry is acknowledged and removed");
            (await _db.StreamPendingAsync(RedisKeys.EventRunsStream, RedisKeys.RunnerGroup)).PendingMessageCount.Should().Be(0);
        }

        [SkippableFact]
        public async Task The_wait_limit_applies_to_event_runs_only_an_old_http_run_still_runs()
        {
            Skip.If(Unavailable, "no Redis available");
            var (service, sandbox) = Service(maxParallelRuns: 0, eventMaxWaitSeconds: 10);
            using var _ = service;
            var id = await QueueAsync(RedisKeys.RunsStream, "run_http", firstQueuedAgo: TimeSpan.FromSeconds(600));
            await service.StartAsync(CancellationToken.None);
            try
            {
                await WaitForResultsAsync(1);
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
            }

            (await ResultsAsync()).Should().ContainSingle().Which.Status.Should().Be(RunStatuses.Succeeded);
            sandbox.Calls.Should().Be(1);
            _runIds.Should().Contain(id);
        }

        [SkippableFact]
        public async Task With_one_slot_and_both_queues_full_the_runner_runs_five_http_runs_per_event_run()
        {
            Skip.If(Unavailable, "no Redis available");
            var kinds = new Dictionary<string, char>();
            for (var i = 0; i < 11; i++) kinds[await QueueAsync(RedisKeys.RunsStream, "run_h")] = 'h';
            for (var i = 0; i < 2; i++) kinds[await QueueAsync(RedisKeys.EventRunsStream, "run_e")] = 'e';
            var (service, sandbox) = Service(maxParallelRuns: 1);
            using var _ = service;
            await service.StartAsync(CancellationToken.None);
            try
            {
                await WaitForResultsAsync(13);
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
            }

            var order = string.Concat(sandbox.Order.Select(id => kinds[id]));
            order.Should().Be("hhhhh" + "e" + "hhhhh" + "e" + "h");
        }
    }
}
