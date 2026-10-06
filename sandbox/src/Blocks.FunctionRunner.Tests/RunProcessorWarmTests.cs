using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Contracts;
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
    /// The branch in <see cref="RunProcessor"/>: off (or not asked for) is the single-run path,
    /// untouched; on with <c>reuse=1</c> is the warm pool, with the same envelope, the same result
    /// fields and three more. Against a real Redis like the other RunProcessor tests (set
    /// TEST_REDIS; skipped without one), with sandboxes faked.
    /// </summary>
    public sealed class RunProcessorWarmTests : IAsyncLifetime
    {
        private ConnectionMultiplexer? _redis;
        private IDatabase? _db;
        private readonly string _runsDir = Path.Combine(Path.GetTempPath(), $"fn-runs-{Guid.NewGuid():N}");
        private readonly string _functionId = $"fn_test_{Guid.NewGuid():N}";
        private readonly List<string> _runIds = [];
        // A list rather than a field of the pool's type: the pool is disposed in DisposeAsync below.
        private readonly List<WarmPool> _pools = [];

        private WarmPool Pool => _pools[^1];

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
            }
            catch (RedisException)
            {
                _redis = null;
                _db = null;
            }
        }

        public async Task DisposeAsync()
        {
            foreach (var pool in _pools) await pool.DisposeAsync();
            if (_db is not null)
            {
                foreach (var id in _runIds)
                {
                    await _db.KeyDeleteAsync([RedisKeys.Run(id), RedisKeys.Lease(id), RedisKeys.Result(id), RedisKeys.Logs(id)]);
                }
                await _db.KeyDeleteAsync([RedisKeys.Concurrency(_functionId), RedisKeys.TenantSlots("tenant_test")]);
            }
            if (_redis is not null) await _redis.DisposeAsync();
            if (Directory.Exists(_runsDir)) Directory.Delete(_runsDir, recursive: true);
        }

        private bool Unavailable => _db is null;

        private sealed class CountingSandbox : ISandbox
        {
            public int Calls { get; private set; }

            public Task<SandboxResult> RunAsync(
                string runId, string image, string envelopeHostPath, RunLimits limits, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(new SandboxResult
                {
                    Output = Succeeded(),
                    ExitCode = 0,
                    OomKilled = false,
                    TimedOut = false,
                    DurationMs = 5,
                });
            }
        }

        private static SandboxOutput Succeeded()
        {
            var output = new SandboxOutput();
            new SandboxOutputParser().Feed(output, "{\"t\":\"result\",\"ok\":true,\"value\":1}");
            return output;
        }

        private sealed class ResolvesAnything : IImageResolver
        {
            public Task<string?> EnsureAsync(
                string reference, CancellationToken token, string? artifactUrl = null, string? artifactSha256 = null) =>
                Task.FromResult<string?>(reference);
        }

        /// <summary>The footprint of the last processor built, to check what was fed to it.</summary>
        private SandboxFootprint _footprint = new();

        private (RunProcessor Processor, ScriptedFactory Factory) Processor(ISandbox sandbox, bool reuseOn, CalmHost? host = null)
        {
            var options = Microsoft.Extensions.Options.Options.Create(new RunnerOptions
            {
                RunnerId = "test-runner",
                RunsDir = _runsDir,
                MaxActiveSandboxes = 4,
                SandboxReuse = reuseOn,
                StartCostCpuMs = 0,
            });
            _footprint = new SandboxFootprint();
            var budget = new HostBudget(options, host ?? new CalmHost(), _footprint, NullLogger<HostBudget>.Instance);
            var factory = new ScriptedFactory(options.Value);
            _pools.Add(new WarmPool(factory, budget, options, NullLogger<WarmPool>.Instance));

            var processor = new RunProcessor(
                _db!, sandbox, new ResolvesAnything(), budget, new FakeRunSecretResolver(), new FakeRunAccessTokenResolver(),
                options, NullLogger<RunProcessor>.Instance, warmPool: Pool)
            {
                EnvelopeGroupHandoff = (_, _) => { },
            };
            return (processor, factory);
        }

        private async Task<RunJob> QueueAsync(bool reuse, string image = "img@sha256:abc")
        {
            var runId = $"run_test_{Guid.NewGuid():N}";
            _runIds.Add(runId);
            var envelope = "{\"run\":{\"id\":\"" + runId + "\"},\"env\":{\"STRIPE_KEY\":\"sk_live_x\"},\"input\":{\"a\":1}}";
            await _db!.HashSetAsync(RedisKeys.Run(runId),
            [
                new HashEntry("envelope", envelope),
                new HashEntry("status", RunStatuses.Queued),
            ]);
            await _db.KeyExpireAsync(RedisKeys.Run(runId), TimeSpan.FromMinutes(5));

            return new RunJob
            {
                RunId = runId,
                FunctionId = _functionId,
                VersionId = "ver_1",
                TenantId = "tenant_test",
                Image = image,
                Reuse = reuse,
            };
        }

        private async Task<NameValueEntry[]> ResultEntryAsync(string runId)
        {
            var entries = await _db!.StreamRangeAsync(RedisKeys.ResultsStream, "-", "+");
            return entries.Select(e => e.Values).Last(v => v.Any(f => f.Name == "runId" && f.Value == runId));
        }

        private static string? Field(NameValueEntry[] entry, string name) =>
            entry.Any(f => f.Name == name) ? entry.First(f => f.Name == name).Value.ToString() : null;

        [SkippableFact]
        public async Task With_reuse_off_a_reuse_run_takes_the_single_path_with_the_old_fields_only()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new CountingSandbox();
            var (processor, factory) = Processor(sandbox, reuseOn: false);
            var job = await QueueAsync(reuse: true);

            (await processor.ProcessAsync(job, default)).Should().Be(RunProcessor.Disposition.Complete);

            sandbox.Calls.Should().Be(1);
            factory.Created.Should().BeEmpty();
            var result = await ResultEntryAsync(job.RunId);
            Field(result, "reused").Should().BeNull();
            Field(result, "discard").Should().BeNull();
            Field(result, "handoverMs").Should().BeNull();
        }

        [SkippableFact]
        public async Task With_reuse_on_a_run_that_did_not_ask_takes_the_single_path()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new CountingSandbox();
            var (processor, factory) = Processor(sandbox, reuseOn: true);

            await processor.ProcessAsync(await QueueAsync(reuse: false), default);

            sandbox.Calls.Should().Be(1);
            factory.Created.Should().BeEmpty();
        }

        [SkippableFact]
        public async Task With_reuse_on_a_reuse_run_is_served_warm_and_reported_with_the_new_fields()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new CountingSandbox();
            var (processor, factory) = Processor(sandbox, reuseOn: true);

            var first = await QueueAsync(reuse: true);
            (await processor.ProcessAsync(first, default)).Should().Be(RunProcessor.Disposition.Complete);
            var second = await QueueAsync(reuse: true);
            (await processor.ProcessAsync(second, default)).Should().Be(RunProcessor.Disposition.Complete);

            sandbox.Calls.Should().Be(0, "the single-run sandbox is never started");
            factory.Created.Should().ContainSingle("the second run reused the first run's sandbox");

            // The envelope went in as one line, secrets resolved exactly as for a single run.
            var written = factory.Created[0].Written;
            written.Should().HaveCount(2);
            written[0].Should().Contain(first.RunId).And.Contain("sk_live_x").And.NotContain("\n");
            Directory.Exists(Path.Combine(_runsDir, first.RunId)).Should().BeFalse("nothing is written to disk");

            var r1 = await ResultEntryAsync(first.RunId);
            Field(r1, "status").Should().Be(RunStatuses.Succeeded);
            Field(r1, "resultKey").Should().NotBeNullOrEmpty();
            Field(r1, "reused").Should().Be("0");
            Field(r1, "discard").Should().BeEmpty();
            long.Parse(Field(r1, "handoverMs")!, System.Globalization.CultureInfo.InvariantCulture).Should().BeGreaterThanOrEqualTo(0);
            Field(r1, "attempt").Should().Be("1", "every existing field is still there");

            var r2 = await ResultEntryAsync(second.RunId);
            Field(r2, "reused").Should().Be("1");
            Field(r2, "startupMs").Should().Be("0", "a reused sandbox has no startup");
            ((string)(await _db!.StringGetAsync(RedisKeys.Result(second.RunId)))!).Should().Be("\"ok\"");
        }

        [SkippableFact]
        public async Task A_dirty_call_is_reported_with_its_discard_reason()
        {
            Skip.If(Unavailable, "no Redis available");
            var (processor, factory) = Processor(new CountingSandbox(), reuseOn: true);
            factory.Configure = c => c.OnCall = id =>
                [Lines.Started(id), Lines.Result(id, "1"), Lines.Idle(id, clean: false, ["Timeout", "fetch x1"])];
            var job = await QueueAsync(reuse: true);

            await processor.ProcessAsync(job, default);

            var result = await ResultEntryAsync(job.RunId);
            Field(result, "status").Should().Be(RunStatuses.Succeeded);
            Field(result, "discard").Should().Be("dirty:Timeout,fetch x1");
            Pool.Counts.Total.Should().Be(0);
        }

        [SkippableFact]
        public async Task An_image_without_the_reuse_runtime_falls_back_to_the_single_path_for_this_and_later_runs()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new CountingSandbox();
            var (processor, factory) = Processor(sandbox, reuseOn: true);
            factory.Configure = c =>
            {
                c.OnStart.Clear();
                c.OnStart.Add(Lines.V1NoEnvelope());
                c.Exit = (20, false);
            };

            var first = await QueueAsync(reuse: true, image: "old@sha256:v1");
            (await processor.ProcessAsync(first, default)).Should().Be(RunProcessor.Disposition.Complete);
            var second = await QueueAsync(reuse: true, image: "old@sha256:v1");
            (await processor.ProcessAsync(second, default)).Should().Be(RunProcessor.Disposition.Complete);

            sandbox.Calls.Should().Be(2, "both runs were served by a fresh sandbox, neither failed");
            factory.Created.Should().ContainSingle("the image is not tried again");
            Field(await ResultEntryAsync(first.RunId), "status").Should().Be(RunStatuses.Succeeded);
        }

        [SkippableFact]
        public async Task A_function_that_fails_to_load_fails_the_run_as_a_single_run_would()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new CountingSandbox();
            var (processor, factory) = Processor(sandbox, reuseOn: true);
            factory.Configure = c =>
            {
                c.OnStart.Clear();
                c.OnStart.Add(Lines.Fatal(ErrorCodes.UserRuntimeError, "the function module failed to load: SyntaxError"));
                c.Exit = (10, false);
            };
            var job = await QueueAsync(reuse: true);

            await processor.ProcessAsync(job, default);

            sandbox.Calls.Should().Be(0);
            var result = await ResultEntryAsync(job.RunId);
            Field(result, "status").Should().Be(RunStatuses.Failed);
            Field(result, "errorCode").Should().Be(ErrorCodes.UserRuntimeError);
            Field(result, "errorMessage").Should().Contain("failed to load");
            Field(result, "discard").Should().Be("crash");
        }

        // ---- review fixes (2026-10-06) ---------------------------------------------------------

        [SkippableFact]
        public async Task A_reused_sandbox_that_dies_before_the_call_starts_is_replaced_and_the_run_succeeds()
        {
            Skip.If(Unavailable, "no Redis available");
            var (processor, factory) = Processor(new CountingSandbox(), reuseOn: true);
            // The pre-warmed sandbox (the first) exits as soon as it is handed a call; the fresh one works.
            factory.ConfigureNth = (c, n) =>
            {
                if (n == 0) c.OnCall = _ => { c.End(); return []; };
            };
            await Pool.PrewarmAsync(
                new WarmKey("tenant_test", _functionId, "ver_1", "img@sha256:abc"), RunLimits.Default, 1, default);
            var job = await QueueAsync(reuse: true);

            (await processor.ProcessAsync(job, default)).Should().Be(RunProcessor.Disposition.Complete);

            factory.Created.Should().HaveCount(2);
            var result = await ResultEntryAsync(job.RunId);
            Field(result, "status").Should().Be(RunStatuses.Succeeded, "nothing of this call had run when the old sandbox died");
            Field(result, "reused").Should().Be("0", "it was served by the fresh sandbox");
        }

        [SkippableFact]
        public async Task A_single_run_refused_for_memory_evicts_an_idle_warm_sandbox()
        {
            Skip.If(Unavailable, "no Redis available");
            // Room for exactly one 128 MB sandbox above the 2 GB host reserve.
            var sandbox = new CountingSandbox();
            var (processor, _) = Processor(sandbox, reuseOn: true, new CalmHost { TotalMemoryBytes = (2048L + 128) * 1024 * 1024 });
            await Pool.PrewarmAsync(new WarmKey("tenant_test", "fn_other", "v", "img"), RunLimits.Default, 1, default);

            (await processor.ProcessAsync(await QueueAsync(reuse: false), default)).Should().Be(RunProcessor.Disposition.Complete);

            sandbox.Calls.Should().Be(1);
            Pool.Counts.Total.Should().Be(0, "the idle sandbox gave its memory to the waiting run");
        }

        [SkippableFact]
        public async Task A_warm_calls_memory_snapshot_is_not_fed_to_the_footprint()
        {
            Skip.If(Unavailable, "no Redis available");
            var (processor, _) = Processor(new CountingSandbox(), reuseOn: true);

            await processor.ProcessAsync(await QueueAsync(reuse: true), default);

            _footprint.SampleCount.Should().Be(0);
        }
    }
}
