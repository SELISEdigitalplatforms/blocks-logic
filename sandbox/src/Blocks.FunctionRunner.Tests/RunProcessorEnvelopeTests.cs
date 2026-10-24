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
    /// The envelope on the runner's disk holds the tenant's resolved secret variables, so it
    /// must be gone once the run is over — however it ended. Runs <see cref="RunProcessor"/>
    /// end to end against a real Redis (skipping cleanly without one; set TEST_REDIS) with the
    /// sandbox and image resolver faked, because the lease and concurrency Lua are part of the
    /// path and a fake Redis would only re-implement them.
    /// </summary>
    // Shares the results stream that EventRunQueueTests deletes; must not run alongside it.
    [Collection("redis-key-prefix")]
    public sealed class RunProcessorEnvelopeTests : IAsyncLifetime
    {
        private ConnectionMultiplexer? _redis;
        private IDatabase? _db;
        private readonly string _runsDir = Path.Combine(Path.GetTempPath(), $"fn-runs-{Guid.NewGuid():N}");
        private readonly string _runId = $"run_test_{Guid.NewGuid():N}";
        private readonly string _functionId = $"fn_test_{Guid.NewGuid():N}";

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
            if (_db is not null)
            {
                await _db.KeyDeleteAsync(
                [
                    RedisKeys.Run(_runId), RedisKeys.Lease(_runId), RedisKeys.Concurrency(_functionId),
                    RedisKeys.Result(_runId), RedisKeys.Logs(_runId), RedisKeys.StreamOut(_runId), RedisKeys.Events(_runId),
                ]);
            }
            if (_redis is not null) await _redis.DisposeAsync();
            if (Directory.Exists(_runsDir)) Directory.Delete(_runsDir, recursive: true);
        }

        private bool Unavailable => _db is null;

        private string RunDir => Path.Combine(_runsDir, _runId);

        /// <summary>A sandbox that records what it was handed, then does what the test says.</summary>
        private sealed class FakeSandbox(Func<string, SandboxResult> behave) : ISandbox
        {
            public int Calls { get; private set; }

            public string? EnvelopeContentSeen { get; private set; }

            public UnixFileMode? EnvelopeModeSeen { get; private set; }

            public Task<SandboxResult> RunAsync(
                string runId, string image, string envelopeHostPath, RunLimits limits, CancellationToken cancellationToken)
            {
                Calls++;
                EnvelopeContentSeen = File.ReadAllText(envelopeHostPath);
                EnvelopeModeSeen = File.GetUnixFileMode(envelopeHostPath);
                return Task.FromResult(behave(envelopeHostPath));
            }
        }

        /// <summary>A sandbox that streams these pieces through <c>onChunk</c> while it runs, then finishes.</summary>
        private sealed class StreamingSandbox(params string[] pieces) : ISandbox
        {
            public Task<SandboxResult> RunAsync(
                string runId, string image, string envelopeHostPath, RunLimits limits, CancellationToken cancellationToken) =>
                RunAsync(runId, image, envelopeHostPath, limits, null, cancellationToken);

            public Task<SandboxResult> RunAsync(
                string runId, string image, string envelopeHostPath, RunLimits limits, Action<string>? onChunk,
                CancellationToken cancellationToken)
            {
                foreach (var piece in pieces) onChunk?.Invoke(piece);
                var answered = Finished();
                return Task.FromResult(answered with
                {
                    Output = new SandboxOutputParser().Parse("{\"t\":\"result\",\"ok\":true,\"result\":\"Hello\"}\n"),
                });
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

        private RunProcessor Processor(ISandbox sandbox, Action<string, int>? handoff = null, string[]? traceHosts = null)
        {
            var options = Microsoft.Extensions.Options.Options.Create(new RunnerOptions
            {
                RunnerId = "test-runner",
                RunsDir = _runsDir,
                MaxActiveSandboxes = 4,
                TraceHosts = traceHosts ?? [],
            });
            var budget = new HostBudget(options, new RoomyHost(), new SandboxFootprint(), NullLogger<HostBudget>.Instance);

            return new RunProcessor(_db!, sandbox, new ResolvesAnything(), budget, new FakeRunSecretResolver(), new FakeRunAccessTokenResolver(), options, NullLogger<RunProcessor>.Instance)
            {
                // Root can give a file to any group; anyone else needs the provisioned
                // membership. The real syscall is proven in EnvelopeScreeningTests.
                EnvelopeGroupHandoff = handoff ?? ((_, _) => { }),
            };
        }

        private async Task<RunJob> QueueAsync(string envelope = """{"run":{"id":"r"},"env":{"STRIPE_KEY":"sk_live_x"},"input":{}}""")
        {
            await _db!.HashSetAsync(RedisKeys.Run(_runId),
            [
                new HashEntry("envelope", envelope),
                new HashEntry("status", RunStatuses.Queued),
            ]);
            await _db.KeyExpireAsync(RedisKeys.Run(_runId), TimeSpan.FromMinutes(5));

            return new RunJob
            {
                RunId = _runId,
                FunctionId = _functionId,
                TenantId = "tenant_test",
                Image = "img@sha256:abc",
                Attempt = 3,
                Deliveries = 2,
            };
        }

        private static SandboxResult Finished(bool timedOut = false, bool oom = false, int exitCode = 0) => new()
        {
            Output = new SandboxOutput(),
            ExitCode = exitCode,
            OomKilled = oom,
            TimedOut = timedOut,
            DurationMs = 5,
        };

        private async Task<NameValueEntry[]> ResultEntryAsync()
        {
            var entries = await _db!.StreamRangeAsync(RedisKeys.ResultsStream, "-", "+");
            return entries
                .Select(e => e.Values)
                .Last(v => v.Any(f => f.Name == "runId" && f.Value == _runId));
        }

        private static string? Field(NameValueEntry[] entry, string name) =>
            entry.FirstOrDefault(f => f.Name == name).Value;

        [SkippableFact]
        public async Task A_successful_run_sees_a_group_only_envelope_and_leaves_none_behind()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new FakeSandbox(_ => Finished());

            var disposition = await Processor(sandbox).ProcessAsync(await QueueAsync(), CancellationToken.None);

            disposition.Should().Be(RunProcessor.Disposition.Complete);
            sandbox.EnvelopeContentSeen.Should().Contain("sk_live_x");
            sandbox.EnvelopeModeSeen.Should().Be(UnixFileMode.UserRead | UnixFileMode.GroupRead);
            Directory.Exists(RunDir).Should().BeFalse();
        }

        [SkippableFact]
        public async Task The_result_reports_the_control_planes_attempt_not_the_delivery_count()
        {
            Skip.If(Unavailable, "no Redis available");

            await Processor(new FakeSandbox(_ => Finished())).ProcessAsync(await QueueAsync(), CancellationToken.None);

            Field(await ResultEntryAsync(), "attempt").Should().Be("3");
        }

        [SkippableFact]
        public async Task A_timed_out_run_leaves_no_envelope_behind()
        {
            Skip.If(Unavailable, "no Redis available");

            await Processor(new FakeSandbox(_ => Finished(timedOut: true, exitCode: 137)))
                .ProcessAsync(await QueueAsync(), CancellationToken.None);

            Directory.Exists(RunDir).Should().BeFalse();
            Field(await ResultEntryAsync(), "status").Should().Be(RunStatuses.TimedOut);
        }

        [SkippableFact]
        public async Task A_failed_run_leaves_no_envelope_behind()
        {
            Skip.If(Unavailable, "no Redis available");

            await Processor(new FakeSandbox(_ => Finished(oom: true, exitCode: 137)))
                .ProcessAsync(await QueueAsync(), CancellationToken.None);

            Directory.Exists(RunDir).Should().BeFalse();
        }

        [SkippableFact]
        public async Task A_sandbox_that_could_not_be_created_leaves_no_envelope_behind()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new FakeSandbox(_ => new SandboxResult
            {
                Output = new SandboxOutput(),
                ExitCode = -1,
                OomKilled = false,
                TimedOut = false,
                DurationMs = 1,
                HostFailure = "the sandbox could not be created: no such image",
            });

            await Processor(sandbox).ProcessAsync(await QueueAsync(), CancellationToken.None);

            Directory.Exists(RunDir).Should().BeFalse();
            Field(await ResultEntryAsync(), "errorCode").Should().Be(ErrorCodes.SandboxStartFailed);
        }

        [SkippableFact]
        public async Task A_sandbox_that_throws_still_leaves_no_envelope_behind()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new FakeSandbox(_ => throw new InvalidOperationException("the Engine socket went away"));

            var act = async () => await Processor(sandbox).ProcessAsync(await QueueAsync(), CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>();
            sandbox.Calls.Should().Be(1, "the envelope was written and handed over before the crash");
            Directory.Exists(RunDir).Should().BeFalse();
        }

        [SkippableFact]
        public async Task A_host_that_cannot_hand_the_envelope_to_the_sandbox_fails_the_run_without_starting_it()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new FakeSandbox(_ => Finished());

            var disposition = await Processor(sandbox,
                    (_, _) => throw new ExecutionEnvelope.HandoffException("Operation not permitted (errno 1)"))
                .ProcessAsync(await QueueAsync(), CancellationToken.None);

            disposition.Should().Be(RunProcessor.Disposition.Complete);
            sandbox.Calls.Should().Be(0, "a sandbox that cannot read its envelope must not be started");
            Directory.Exists(RunDir).Should().BeFalse();

            var result = await ResultEntryAsync();
            Field(result, "status").Should().Be(RunStatuses.Failed);
            Field(result, "errorCode").Should().Be(ErrorCodes.SandboxStartFailed);
            Field(result, "errorMessage").Should().NotContain("errno",
                "host internals are for the runner's log, not the tenant's run record");
        }

        [SkippableFact]
        public async Task A_refused_envelope_never_reaches_the_disk_or_the_sandbox()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new FakeSandbox(_ => Finished());

            await Processor(sandbox).ProcessAsync(
                await QueueAsync("""{"run":{"id":"r"},"context":{"accessToken":"x"}}"""), CancellationToken.None);

            sandbox.Calls.Should().Be(0);
            Directory.Exists(RunDir).Should().BeFalse();
            Field(await ResultEntryAsync(), "errorCode").Should().Be(ErrorCodes.RuntimeStartFailed);
        }
    

        [SkippableFact]
        public async Task A_single_runs_streamed_answer_reaches_the_run_stream_then_its_end()
        {
            Skip.If(Unavailable, "no Redis available");

            var disposition = await Processor(new StreamingSandbox("Hel", "lo"))
                .ProcessAsync(await QueueAsync(), CancellationToken.None);

            disposition.Should().Be(RunProcessor.Disposition.Complete);
            var entries = await _db!.StreamRangeAsync(RedisKeys.StreamOut(_runId), "-", "+");
            entries.Take(2).Select(e => (string?)e[RedisKeys.StreamDataField]).Should().Equal("Hel", "lo");
            entries.Should().HaveCount(3);
            ((string?)entries[2][RedisKeys.StreamEndField]).Should().Be(RunStatuses.Succeeded);
            (await _db.KeyTimeToLiveAsync(RedisKeys.StreamOut(_runId))).Should().NotBeNull("it expires");
        }

        [SkippableFact]
        public async Task A_single_run_that_streams_nothing_leaves_no_stream()
        {
            Skip.If(Unavailable, "no Redis available");

            await Processor(new StreamingSandbox()).ProcessAsync(await QueueAsync(), CancellationToken.None);

            (await _db!.KeyExistsAsync(RedisKeys.StreamOut(_runId))).Should().BeFalse();
        }

        // ---- function events ---------------------------------------------------------

        private const string EventsEnvelope =
            """{"run":{"id":"r"},"events":{"allowed":["SendMail","FunctionEvent"]},"input":{}}""";

        private static string EventsLine(params string[] events) =>
            $$"""{"t":"result","ok":true,"value":1,"events":[{{string.Join(",", events)}}]}""" + "\n";

        private string SendMail(int n) =>
            $$$"""{"id":"{{{_runId}}}-3-{{{n}}}","type":"SendMail","payload":{"to":"a@b.c"}}""";

        private static FakeSandbox Replying(string stdout, bool failed = false) => new(_ => Finished() with
        {
            Output = new SandboxOutputParser().Parse(stdout),
            ExitCode = failed ? 10 : 0,
        });

        [SkippableFact]
        public async Task A_successful_runs_events_are_stored_under_the_events_key_and_counted_on_the_entry()
        {
            Skip.If(Unavailable, "no Redis available");
            var events = new[] { SendMail(1), SendMail(2) };

            await Processor(Replying(EventsLine(events))).ProcessAsync(await QueueAsync(EventsEnvelope), CancellationToken.None);

            var stored = (string?)await _db!.StringGetAsync(RedisKeys.Events(_runId));
            stored.Should().Be($"[{string.Join(",", events)}]");
            (await _db.KeyTimeToLiveAsync(RedisKeys.Events(_runId))).Should().NotBeNull("it expires like the result");
            var entry = await ResultEntryAsync();
            Field(entry, RedisKeys.ResultEventsKeyField).Should().Be(RedisKeys.Events(_runId));
            Field(entry, RedisKeys.ResultEventCountField).Should().Be("2");
            Field(entry, RedisKeys.ResultEventsRejectedField).Should().BeNull();
            Field(entry, "status").Should().Be(RunStatuses.Succeeded);
        }

        [SkippableFact]
        public async Task A_run_without_events_writes_no_events_key_or_fields()
        {
            Skip.If(Unavailable, "no Redis available");

            await Processor(Replying("""{"t":"result","ok":true,"value":1}""" + "\n"))
                .ProcessAsync(await QueueAsync(EventsEnvelope), CancellationToken.None);

            (await _db!.KeyExistsAsync(RedisKeys.Events(_runId))).Should().BeFalse();
            var entry = await ResultEntryAsync();
            Field(entry, RedisKeys.ResultEventsKeyField).Should().BeNull();
            Field(entry, RedisKeys.ResultEventCountField).Should().BeNull();
        }

        [SkippableFact]
        public async Task Events_of_a_type_the_envelope_does_not_allow_are_dropped_and_the_result_stands()
        {
            Skip.If(Unavailable, "no Redis available");
            const string onlyEvents = """{"run":{"id":"r"},"events":{"allowed":["FunctionEvent"]},"input":{}}""";

            await Processor(Replying(EventsLine(SendMail(1)))).ProcessAsync(await QueueAsync(onlyEvents), CancellationToken.None);

            (await _db!.KeyExistsAsync(RedisKeys.Events(_runId))).Should().BeFalse();
            var entry = await ResultEntryAsync();
            Field(entry, "status").Should().Be(RunStatuses.Succeeded);
            Field(entry, RedisKeys.ResultEventsRejectedField).Should().StartWith("EVENTS_REJECTED: ");
            Field(entry, RedisKeys.ResultEventsKeyField).Should().BeNull();
            (await _db.KeyExistsAsync(RedisKeys.Result(_runId))).Should().BeTrue();
        }

        [SkippableFact]
        public async Task A_forged_id_of_another_run_drops_the_events()
        {
            Skip.If(Unavailable, "no Redis available");
            const string forged = """{"id":"someone-else-1-1","type":"SendMail","payload":{}}""";

            await Processor(Replying(EventsLine(forged))).ProcessAsync(await QueueAsync(EventsEnvelope), CancellationToken.None);

            (await _db!.KeyExistsAsync(RedisKeys.Events(_runId))).Should().BeFalse();
            Field(await ResultEntryAsync(), RedisKeys.ResultEventsRejectedField).Should().Contain("does not belong to this run");
        }

        [SkippableFact]
        public async Task A_failed_run_stores_no_events_even_if_its_line_carried_them()
        {
            Skip.If(Unavailable, "no Redis available");
            var failed = $$"""{"t":"result","ok":false,"code":"USER_RUNTIME_ERROR","message":"x","events":[{{SendMail(1)}}]}""" + "\n";

            await Processor(Replying(failed, failed: true)).ProcessAsync(await QueueAsync(EventsEnvelope), CancellationToken.None);

            (await _db!.KeyExistsAsync(RedisKeys.Events(_runId))).Should().BeFalse();
            Field(await ResultEntryAsync(), RedisKeys.ResultEventsKeyField).Should().BeNull();
        }

        [SkippableFact]
        public async Task The_sandbox_gets_a_runner_made_trace_and_the_events_object_as_the_control_plane_wrote_it()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new FakeSandbox(_ => Finished());
            const string queued =
                """{"run":{"id":"r"},"events":{"allowed":["SendMail"]},"trace":{"traceparent":"00-forged","hosts":["evil.example"]},"input":{}}""";

            using var span = new System.Diagnostics.Activity("Function::Run").Start();
            await Processor(sandbox, traceHosts: ["api.blocks.test"]).ProcessAsync(await QueueAsync(queued), CancellationToken.None);

            using var doc = System.Text.Json.JsonDocument.Parse(sandbox.EnvelopeContentSeen!);
            var trace = doc.RootElement.GetProperty("trace");
            trace.GetProperty("traceparent").GetString().Should().Be(span.Id);
            trace.GetProperty("hosts").EnumerateArray().Select(h => h.GetString()).Should().Equal("api.blocks.test");
            doc.RootElement.GetProperty("events").GetRawText().Should().Be("""{"allowed":["SendMail"]}""");
        }
    }
}
