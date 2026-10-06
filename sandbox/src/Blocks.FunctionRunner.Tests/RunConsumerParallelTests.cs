using System.Diagnostics;
using System.Text.Json;
using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Health;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Protocol;
using Blocks.FunctionRunner.Runs;
using Blocks.FunctionRunner.Sandbox;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The run loop works on several runs at once, up to the host's capacity — it used to await
    /// each run before claiming the next, so a host of capacity 10 ran one at a time. End to end
    /// through <see cref="RunConsumerService"/> and <see cref="RunProcessor"/> against a real Redis
    /// (skipping cleanly without one; set TEST_REDIS), with the sandbox faked as a slow function.
    /// </summary>
    [Collection("redis-key-prefix")]
    public sealed class RunConsumerParallelTests : IAsyncLifetime
    {
        private static readonly TimeSpan RunTime = TimeSpan.FromMilliseconds(700);

        private ConnectionMultiplexer? _redis;
        private IDatabase? _db;
        private readonly string _runsDir = Path.Combine(Path.GetTempPath(), $"fn-runs-{Guid.NewGuid():N}");
        private readonly List<string> _runIds = [.. Enumerable.Range(0, 3).Select(_ => $"run_par_{Guid.NewGuid():N}")];

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
                // A clean stream and group, so only this test's entries are there to claim.
                await _db.KeyDeleteAsync(RedisKeys.RunsStream);
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
                await _db.KeyDeleteAsync(RedisKeys.RunsStream);
                foreach (var id in _runIds)
                {
                    await _db.KeyDeleteAsync([RedisKeys.Run(id), RedisKeys.Lease(id), RedisKeys.Result(id), RedisKeys.Logs(id)]);
                }
            }
            if (_redis is not null) await _redis.DisposeAsync();
            if (Directory.Exists(_runsDir)) Directory.Delete(_runsDir, recursive: true);
        }

        private bool Unavailable => _db is null;

        private sealed class SlowSandbox : ISandbox
        {
            public async Task<SandboxResult> RunAsync(
                string runId, string image, string envelopeHostPath, RunLimits limits, CancellationToken cancellationToken)
            {
                await Task.Delay(RunTime, cancellationToken);
                return new SandboxResult
                {
                    Output = new SandboxOutput { Ok = true, ResultJson = "1" }, ExitCode = 0, OomKilled = false, TimedOut = false,
                    DurationMs = (long)RunTime.TotalMilliseconds,
                };
            }
        }

        private sealed class ResolvesAnything : IImageResolver
        {
            public Task<string?> EnsureAsync(
                string reference, CancellationToken token, string? artifactUrl = null, string? artifactSha256 = null) =>
                Task.FromResult<string?>(reference);
        }

        private sealed class RoomyHost : IHostSignals
        {
            public double Cores => 8;

            public long TotalMemoryBytes => 64L * 1024 * 1024 * 1024;

            public HostSignalSample Sample() => new(0, 0, 0, 64L * 1024 * 1024 * 1024);
        }

        private RunConsumerService Service(int maxParallelRuns, int deferralBudgetSeconds = 600)
        {
            var options = Microsoft.Extensions.Options.Options.Create(new RunnerOptions
            {
                RunnerId = $"test-par-{Guid.NewGuid():N}",
                RunsDir = _runsDir,
                MaxActiveSandboxes = 8,
                MaxParallelRuns = maxParallelRuns,
                DeferralBudgetSeconds = deferralBudgetSeconds,
            });
            var budget = new HostBudget(options, new RoomyHost(), new SandboxFootprint(), NullLogger<HostBudget>.Instance);
            var processor = new RunProcessor(_db!, new SlowSandbox(), new ResolvesAnything(), budget,
                new FakeRunSecretResolver(), new FakeRunAccessTokenResolver(), options, NullLogger<RunProcessor>.Instance)
            {
                EnvelopeGroupHandoff = (_, _) => { },
            };
            // Never started: no heartbeat means no "unhealthy" verdict, which is all the loop asks.
            var heartbeat = new HeartbeatService(_db!, budget,
                new StartupGuard(null!, _db!, options, NullLogger<StartupGuard>.Instance),
                options, NullLogger<HeartbeatService>.Instance);
            return new RunConsumerService(_db!, processor, heartbeat, budget, options, NullLogger<RunConsumerService>.Instance);
        }

        private async Task QueueAsync()
        {
            for (var i = 0; i < _runIds.Count; i++)
            {
                var id = _runIds[i];
                // A tenant and a function each, so neither share is what limits the test.
                var envelope = JsonSerializer.Serialize(new
                {
                    run = new { id, attempt = 1 },
                    context = new { tenantId = $"t_par_{i}" },
                    env = new { },
                    input = new { },
                    limits = new { timeoutMs = 5000 },
                });
                await _db!.HashSetAsync(RedisKeys.Run(id), [new("envelope", envelope), new("status", RunStatuses.Queued)]);
                await _db.KeyExpireAsync(RedisKeys.Run(id), TimeSpan.FromMinutes(5));
                await _db.StreamAddAsync(RedisKeys.RunsStream,
                [
                    new("runId", id), new("functionId", $"fn_par_{i}"), new("tenantId", $"t_par_{i}"),
                    new("image", "img@sha256:abc"), new("attempt", 1), new("protocol", RedisKeys.RunProtocolVersion),
                ]);
            }
        }

        private async Task<TimeSpan> RunAllAsync(int maxParallelRuns)
        {
            await QueueAsync();
            using var service = Service(maxParallelRuns);
            var watch = Stopwatch.StartNew();
            await service.StartAsync(CancellationToken.None);
            try
            {
                while (watch.Elapsed < TimeSpan.FromSeconds(10))
                {
                    var done = (await _db!.StreamRangeAsync(RedisKeys.ResultsStream, "-", "+"))
                        .Count(e => e.Values.Any(v => v.Name == "runId" && _runIds.Contains(v.Value.ToString())));
                    if (done == _runIds.Count) return watch.Elapsed;
                    await Task.Delay(25);
                }
                throw new TimeoutException("the runs did not all finish");
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
            }
        }

        [SkippableFact]
        public async Task Several_queued_runs_are_worked_on_at_once()
        {
            Skip.If(Unavailable, "no Redis available");

            var elapsed = await RunAllAsync(maxParallelRuns: 0);

            // One after another is at least 3 × 700 ms; side by side is about one run.
            elapsed.Should().BeLessThan(RunTime * 2);
        }

        [SkippableFact]
        public async Task MaxParallelRuns_1_falls_back_to_one_at_a_time()
        {
            Skip.If(Unavailable, "no Redis available");

            var elapsed = await RunAllAsync(maxParallelRuns: 1);

            elapsed.Should().BeGreaterThanOrEqualTo(RunTime * 3 - TimeSpan.FromMilliseconds(50));
        }

        [SkippableFact]
        public async Task Stopping_the_runner_lets_a_started_run_finish_instead_of_killing_it()
        {
            Skip.If(Unavailable, "no Redis available");
            await QueueAsync();
            using var service = Service(maxParallelRuns: 0);
            await service.StartAsync(CancellationToken.None);
            // Wait until the runs are inside their sandboxes, then stop the host.
            await Task.Delay(RunTime / 2);
            var watch = Stopwatch.StartNew();
            await service.StopAsync(CancellationToken.None);

            var done = (await _db!.StreamRangeAsync(RedisKeys.ResultsStream, "-", "+"))
                .Where(e => e.Values.Any(v => v.Name == "runId" && _runIds.Contains(v.Value.ToString())))
                .Select(e => e.Values.First(v => v.Name == "status").Value.ToString())
                .ToList();
            done.Should().HaveCount(_runIds.Count, "every started run was allowed to finish");
            var detail = string.Join(" | ", (await _db!.StreamRangeAsync(RedisKeys.ResultsStream, "-", "+"))
                .Where(e => e.Values.Any(v => v.Name == "runId" && _runIds.Contains(v.Value.ToString())))
                .Select(e => string.Join(",", e.Values.Where(v => v.Name == "errorCode" || v.Name == "errorMessage").Select(v => v.Value))));
            done.Should().OnlyContain(s => s == RunStatuses.Succeeded, detail);
            watch.Elapsed.Should().BeLessThan(RunTime * 2);
        }

        private async Task<List<FunctionConcurrency>> FillFunctionSlotsAsync(string functionId)
        {
            var held = new List<FunctionConcurrency>();
            for (var i = 0; i < Ceilings.MaxFunctionConcurrency; i++)
            {
                held.Add((await FunctionConcurrency.TryEnterAsync(_db!, functionId, $"run_busy_{i}", Ceilings.MaxFunctionConcurrency))!);
            }
            return held;
        }

        private async Task<string?> StatusOfAsync(string runId) =>
            (await _db!.StreamRangeAsync(RedisKeys.ResultsStream, "-", "+"))
                .Where(e => e.Values.Any(v => v.Name == "runId" && v.Value == runId))
                .Select(e => (string?)e.Values.First(v => v.Name == "status").Value.ToString())
                .LastOrDefault();

        [SkippableFact]
        public async Task A_deferred_run_goes_back_on_the_queue_at_once_and_runs_when_a_slot_frees()
        {
            Skip.If(Unavailable, "no Redis available");
            var held = await FillFunctionSlotsAsync("fn_par_0");
            await QueueAsync();
            using var service = Service(maxParallelRuns: 0);
            await service.StartAsync(CancellationToken.None);
            try
            {
                await Task.Delay(1500);
                (await StatusOfAsync(_runIds[0])).Should().BeNull("its function is at its limit");
                var requeued = (await _db!.StreamRangeAsync(RedisKeys.RunsStream, "-", "+"))
                    .Any(e => e.Values.Any(v => v.Name == "runId" && v.Value == _runIds[0])
                           && e.Values.Any(v => v.Name == RedisKeys.RunDeferralsField));
                requeued.Should().BeTrue("a deferral puts the run back on the queue, it is not left pending for the 90 s sweep");

                foreach (var slot in held) await slot.DisposeAsync();
                var watch = Stopwatch.StartNew();
                while ((await StatusOfAsync(_runIds[0])) is null && watch.Elapsed < TimeSpan.FromSeconds(8)) await Task.Delay(50);
                (await StatusOfAsync(_runIds[0])).Should().Be(RunStatuses.Succeeded);
                watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "it starts within the back-off, not after 90 s");
            }
            finally
            {
                foreach (var slot in held) await slot.DisposeAsync();
                await service.StopAsync(CancellationToken.None);
                await _db!.KeyDeleteAsync(RedisKeys.Concurrency("fn_par_0"));
            }
        }

        [SkippableFact]
        public async Task A_run_deferred_past_its_budget_is_given_up_with_the_reason()
        {
            Skip.If(Unavailable, "no Redis available");
            var held = await FillFunctionSlotsAsync("fn_par_0");
            await QueueAsync();
            using var service = Service(maxParallelRuns: 0, deferralBudgetSeconds: 1);
            await service.StartAsync(CancellationToken.None);
            try
            {
                var watch = Stopwatch.StartNew();
                StreamEntry? dead = null;
                while (dead is null && watch.Elapsed < TimeSpan.FromSeconds(10))
                {
                    await Task.Delay(100);
                    dead = (await _db!.StreamRangeAsync(RedisKeys.DeadStream, "-", "+"))
                        .Cast<StreamEntry?>()
                        .FirstOrDefault(e => e!.Value.Values.Any(v => v.Name == "runId" && v.Value == _runIds[0]));
                }
                dead.Should().NotBeNull();
                dead!.Value.Values.First(v => v.Name == "deadReason").Value.ToString().Should().Contain("no capacity");
            }
            finally
            {
                foreach (var slot in held) await slot.DisposeAsync();
                await service.StopAsync(CancellationToken.None);
                await _db!.KeyDeleteAsync([RedisKeys.Concurrency("fn_par_0"), RedisKeys.DeadStream]);
            }
        }
    }
}
