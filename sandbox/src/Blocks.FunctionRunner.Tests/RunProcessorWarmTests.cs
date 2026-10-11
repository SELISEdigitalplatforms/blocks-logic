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
                    await _db.KeyDeleteAsync([RedisKeys.Run(id), RedisKeys.Lease(id), RedisKeys.Result(id), RedisKeys.Logs(id), RedisKeys.StreamOut(id)]);
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
            public int Calls;

            public Task<string?> EnsureAsync(
                string reference, CancellationToken token, string? artifactUrl = null, string? artifactSha256 = null)
            {
                Interlocked.Increment(ref Calls);
                return Task.FromResult<string?>(reference);
            }
        }

        private readonly ResolvesAnything _resolver = new();

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
                _db!, sandbox, _resolver, budget, new FakeRunSecretResolver(), new FakeRunAccessTokenResolver(),
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

            // The live sandbox proved the image is here, so the second call never asked Docker.
            _resolver.Calls.Should().Be(1);
            // Every step time of the call rides on the result entry, for the run's Timing group.
            Field(r2, RedisKeys.ResultTimingsField).Should().Contain("handover.admission=").And.Contain("handover.total=");
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

        // ---- FN-2: the answer does not wait for ctx.waitUntil (2026-10-07) ----------------------

        [SkippableFact]
        public async Task A_warm_caller_is_answered_at_the_result_line_before_waitUntil_work_ends()
        {
            Skip.If(Unavailable, "no Redis available");
            var (processor, factory) = Processor(new CountingSandbox(), reuseOn: true);
            factory.Configure = c => c.OnCall = id => [Lines.Started(id), Lines.Result(id, "42")];
            var job = await QueueAsync(reuse: true);
            var notified = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sub = _redis!.GetSubscriber();
            await sub.SubscribeAsync(RedisChannel.Literal(RedisKeys.SyncChannel(job.RunId)), (_, v) => notified.TrySetResult(v!));

            var run = processor.ProcessAsync(job, default);

            (await notified.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(RunStatuses.Succeeded);
            // What the Api's fast path reads is there; the Worker's entry is not, the run still goes on.
            ((string?)await _db!.HashGetAsync(RedisKeys.Run(job.RunId), "status")).Should().Be(RunStatuses.Succeeded);
            ((string?)await _db.StringGetAsync(RedisKeys.Result(job.RunId))).Should().Be("42");
            (await _db.StreamRangeAsync(RedisKeys.ResultsStream, "-", "+"))
                .Should().NotContain(e => e.Values.Any(f => f.Name == "runId" && f.Value == job.RunId));
            run.IsCompleted.Should().BeFalse();

            // The waitUntil work logs, then the runtime reports the call over.
            factory.Created[0].Push(Lines.Log(job.RunId, "analytics sent"));
            factory.Created[0].Push(Lines.Idle(job.RunId, clean: true));
            (await run).Should().Be(RunProcessor.Disposition.Complete);

            var entry = await ResultEntryAsync(job.RunId);
            Field(entry, "status").Should().Be(RunStatuses.Succeeded);
            Field(entry, "discard").Should().BeEmpty();
            var logs = await _db.ListRangeAsync(RedisKeys.Logs(job.RunId));
            logs.Select(l => (string)l!).Should().Contain(l => l.Contains("analytics sent"), "late logs still reach the record");
            factory.Created[0].Pauses.Should().Be(1, "kept and paused, after the answer");
        }

        [SkippableFact]
        public async Task An_answered_call_keeps_its_status_when_the_sandbox_dies_afterwards()
        {
            Skip.If(Unavailable, "no Redis available");
            var (processor, factory) = Processor(new CountingSandbox(), reuseOn: true);
            factory.Configure = c =>
            {
                c.Exit = (137, true);
                c.OnCall = id =>
                {
                    // Answered, then OOM-killed inside its waitUntil work.
                    c.Push(Lines.Started(id));
                    c.Push(Lines.Result(id, "1"));
                    c.End();
                    return [];
                };
            };
            var job = await QueueAsync(reuse: true);

            (await processor.ProcessAsync(job, default)).Should().Be(RunProcessor.Disposition.Complete);

            var entry = await ResultEntryAsync(job.RunId);
            Field(entry, "status").Should().Be(RunStatuses.Succeeded, "the caller already has this answer");
            Field(entry, "errorCode").Should().BeEmpty();
            Field(entry, "discard").Should().Be("memory", "the kill still ends the sandbox and shows on the run");
            ((string?)await _db!.HashGetAsync(RedisKeys.Run(job.RunId), "status")).Should().Be(RunStatuses.Succeeded);
            Pool.Counts.Total.Should().Be(0);
        }

        [SkippableFact]
        public async Task A_failed_call_is_recorded_early_without_waking_the_caller()
        {
            Skip.If(Unavailable, "no Redis available");
            var (processor, factory) = Processor(new CountingSandbox(), reuseOn: true);
            factory.Configure = c => c.OnCall = id =>
                [Lines.Started(id), Lines.Failure(id, ErrorCodes.UserRuntimeError, "boom"), Lines.Idle(id, clean: true)];
            var job = await QueueAsync(reuse: true);
            var messages = new List<string>();
            var sub = _redis!.GetSubscriber();
            await sub.SubscribeAsync(RedisChannel.Literal(RedisKeys.SyncChannel(job.RunId)), (_, v) => { lock (messages) messages.Add(v!); });

            await processor.ProcessAsync(job, default);
            await Task.Delay(200);

            var entry = await ResultEntryAsync(job.RunId);
            Field(entry, "status").Should().Be(RunStatuses.Failed);
            Field(entry, "errorCode").Should().Be(ErrorCodes.UserRuntimeError);
            Field(entry, "errorMessage").Should().Be("boom");
            lock (messages) messages.Should().Equal([RunStatuses.Failed], "only the final notification, after the entry");
        }

        // ---- F-5: streamed answers (2026-10-07) --------------------------------------------------

        private async Task<StreamEntry[]> StreamAsync(string runId)
        {
            _streamKeys.Add(RedisKeys.StreamOut(runId));
            return await _db!.StreamRangeAsync(RedisKeys.StreamOut(runId), "-", "+");
        }

        private readonly List<string> _streamKeys = [];

        [SkippableFact]
        public async Task A_streamed_answer_reaches_the_run_stream_piece_by_piece_then_its_end()
        {
            Skip.If(Unavailable, "no Redis available");
            var (processor, factory) = Processor(new CountingSandbox(), reuseOn: true);
            factory.Configure = c => c.OnCall = id =>
                [Lines.Started(id), Lines.Chunk(id, "Hel"), Lines.Chunk(id, "lo")];
            var job = await QueueAsync(reuse: true);
            var nudges = 0;
            await _redis!.GetSubscriber().SubscribeAsync(
                RedisChannel.Literal(RedisKeys.StreamChannel(job.RunId)), (_, _) => Interlocked.Increment(ref nudges));

            var run = processor.ProcessAsync(job, default);

            // Live: both pieces are there while the call is still going.
            for (var i = 0; i < 100 && (await StreamAsync(job.RunId)).Length < 2; i++) await Task.Delay(20);
            var live = await StreamAsync(job.RunId);
            live.Select(e => (string?)e[RedisKeys.StreamDataField]).Should().Equal("Hel", "lo");
            run.IsCompleted.Should().BeFalse();
            (await _db!.KeyTimeToLiveAsync(RedisKeys.StreamOut(job.RunId))).Should().NotBeNull("it expires");

            factory.Created[0].Push(Lines.Result(job.RunId, "\"Hello\""));
            factory.Created[0].Push(Lines.Idle(job.RunId, clean: true));
            await run;

            var all = await StreamAsync(job.RunId);
            all.Should().HaveCount(3);
            ((string?)all[2][RedisKeys.StreamEndField]).Should().Be(RunStatuses.Succeeded);
            for (var i = 0; i < 50 && Volatile.Read(ref nudges) < 3; i++) await Task.Delay(20);
            Volatile.Read(ref nudges).Should().BeGreaterThanOrEqualTo(3, "a nudge per piece and one for the end");
            ((string?)await _db.StringGetAsync(RedisKeys.Result(job.RunId))).Should().Be("\"Hello\"");
        }

        [SkippableFact]
        public async Task A_stream_cut_off_by_a_crash_still_ends_with_the_runs_status()
        {
            Skip.If(Unavailable, "no Redis available");
            var (processor, factory) = Processor(new CountingSandbox(), reuseOn: true);
            factory.Configure = c => c.OnCall = id =>
            {
                c.Push(Lines.Started(id));
                c.Push(Lines.Chunk(id, "partial"));
                c.End();
                return [];
            };
            var job = await QueueAsync(reuse: true);

            await processor.ProcessAsync(job, default);

            var all = await StreamAsync(job.RunId);
            all.Select(e => (string?)e[RedisKeys.StreamDataField]).First().Should().Be("partial");
            var end = all.Last();
            ((string?)end[RedisKeys.StreamEndField]).Should().NotBeNullOrEmpty().And.NotBe(RunStatuses.Succeeded);
            all.Count(e => !e[RedisKeys.StreamEndField].IsNull).Should().Be(1, "exactly one end");
        }

        [SkippableFact]
        public async Task A_call_that_does_not_stream_writes_no_stream()
        {
            Skip.If(Unavailable, "no Redis available");
            var (processor, _) = Processor(new CountingSandbox(), reuseOn: true);
            var job = await QueueAsync(reuse: true);

            await processor.ProcessAsync(job, default);

            (await _db!.KeyExistsAsync(RedisKeys.StreamOut(job.RunId))).Should().BeFalse();
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
