using System.Text.Json;
using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Delegation;
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
    /// <c>ctx.blocks.accessToken</c> end to end through <see cref="RunProcessor"/> against a real
    /// Redis (skipping cleanly without one; set TEST_REDIS), with the sandbox and the redemption
    /// faked: when the runner redeems the grant beside the envelope, what the sandbox is handed,
    /// that the token never reaches Redis or a log, and that no token never fails a run.
    /// </summary>
    public sealed class RunProcessorDelegationTests : IAsyncLifetime
    {
        private const string Tenant = "tenant_delegation_test";
        private const string Token = "eyJhbGciOi.delegated_RUNNER.sig7";
        private static readonly string Grant = "dg_" + new string('b', 64);

        private ConnectionMultiplexer? _redis;
        private IDatabase? _db;
        private readonly string _runsDir = Path.Combine(Path.GetTempPath(), $"fn-runs-{Guid.NewGuid():N}");
        private readonly string _runId = $"run_dlg_{Guid.NewGuid():N}";
        private readonly string _functionId = $"fn_dlg_{Guid.NewGuid():N}";
        private readonly CapturingLoggerProvider _logs = new();

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
                    RedisKeys.Result(_runId), RedisKeys.Logs(_runId), RedisKeys.TenantSlots(Tenant),
                ]);
            }
            if (_redis is not null) await _redis.DisposeAsync();
            if (Directory.Exists(_runsDir)) Directory.Delete(_runsDir, recursive: true);
        }

        private bool Unavailable => _db is null;

        private sealed class RecordingSandbox(Func<SandboxResult>? behave = null) : ISandbox
        {
            public int Calls { get; private set; }

            public string? EnvelopeContentSeen { get; private set; }

            public Task<SandboxResult> RunAsync(
                string runId, string image, string envelopeHostPath, RunLimits limits, CancellationToken cancellationToken)
            {
                Calls++;
                EnvelopeContentSeen = File.ReadAllText(envelopeHostPath);
                return Task.FromResult(behave?.Invoke() ?? new SandboxResult
                {
                    Output = new SandboxOutput(), ExitCode = 0, OomKilled = false, TimedOut = false, DurationMs = 5,
                });
            }
        }

        private sealed class ResolvesAnything : IImageResolver
        {
            public Task<string?> EnsureAsync(string reference, CancellationToken token) =>
                Task.FromResult<string?>(reference);
        }

        private sealed class RoomyHost : IHostSignals
        {
            public double Cores => 8;

            public long TotalMemoryBytes => 64L * 1024 * 1024 * 1024;

            public HostSignalSample Sample() => new(0, 0, 0, 64L * 1024 * 1024 * 1024);
        }

        private RunProcessor Processor(ISandbox sandbox, IRunAccessTokenResolver tokens)
        {
            var options = Microsoft.Extensions.Options.Options.Create(new RunnerOptions
            {
                RunnerId = "test-runner",
                RunsDir = _runsDir,
                MaxActiveSandboxes = 4,
            });
            var budget = new HostBudget(options, new RoomyHost(), new SandboxFootprint(), NullLogger<HostBudget>.Instance);

            return new RunProcessor(_db!, sandbox, new ResolvesAnything(), budget,
                new FakeRunSecretResolver(new Dictionary<string, string> { ["sec_1"] = "sk_live_SECRET_55" }),
                tokens, options, _logs.For<RunProcessor>())
            {
                EnvelopeGroupHandoff = (_, _) => { },
            };
        }

        private static string Envelope(
            string tenant = Tenant, string? userId = "user_1", bool authenticated = true, object? blocks = null)
        {
            var doc = new Dictionary<string, object?>
            {
                ["run"] = new { id = "r", attempt = 1 },
                ["context"] = new { tenantId = tenant, userId, isAuthenticated = authenticated },
                ["env"] = new { KEY = "{{secret.sec_1}}" },
                ["maskedEnv"] = new[] { "KEY" },
                ["input"] = new { },
                ["limits"] = new { timeoutMs = 5000 },
            };
            if (blocks is not null) doc["blocks"] = blocks;
            return JsonSerializer.Serialize(doc);
        }

        private async Task<RunJob> QueueAsync(string envelope, string? grant, string? tenant = Tenant)
        {
            var fields = new List<HashEntry>
            {
                new("envelope", envelope),
                new("status", RunStatuses.Queued),
            };
            if (grant is not null) fields.Add(new HashEntry(RedisKeys.RunDelegationField, grant));
            await _db!.HashSetAsync(RedisKeys.Run(_runId), [.. fields]);
            await _db.KeyExpireAsync(RedisKeys.Run(_runId), TimeSpan.FromMinutes(5));

            return new RunJob
            {
                RunId = _runId,
                FunctionId = _functionId,
                TenantId = tenant,
                Image = "img@sha256:abc",
                Attempt = 1,
                Protocol = RedisKeys.RunProtocolVersion,
            };
        }

        private async Task<NameValueEntry[]> ResultEntryAsync()
        {
            var entries = await _db!.StreamRangeAsync(RedisKeys.ResultsStream, "-", "+");
            return entries.Select(e => e.Values).Last(v => v.Any(f => f.Name == "runId" && f.Value == _runId));
        }

        private static string? Field(NameValueEntry[] entry, string name) =>
            entry.FirstOrDefault(f => f.Name == name).Value;

        private static string? SeenToken(string envelope)
        {
            using var doc = JsonDocument.Parse(envelope);
            return doc.RootElement.TryGetProperty("blocks", out var b) && b.TryGetProperty("accessToken", out var t)
                ? t.GetString()
                : null;
        }

        // ---------- a grant becomes ctx.blocks.accessToken ----------

        [SkippableFact]
        public async Task The_sandbox_receives_the_redeemed_token_masked_beside_the_secrets()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new RecordingSandbox();
            var tokens = new FakeRunAccessTokenResolver(Token);

            var disposition = await Processor(sandbox, tokens).ProcessAsync(await QueueAsync(Envelope(), Grant), CancellationToken.None);

            disposition.Should().Be(RunProcessor.Disposition.Complete);
            tokens.Calls.Should().Equal((Tenant, Grant));
            SeenToken(sandbox.EnvelopeContentSeen!).Should().Be(Token);

            using var seen = JsonDocument.Parse(sandbox.EnvelopeContentSeen!);
            seen.RootElement.GetProperty("env").GetProperty("KEY").GetString().Should().Be("sk_live_SECRET_55");
            seen.RootElement.GetProperty("maskedValues").EnumerateArray().Select(e => e.GetString())
                .Should().BeEquivalentTo(["sk_live_SECRET_55", Token]);
        }

        [SkippableFact]
        public async Task The_token_never_reaches_Redis_or_a_log_and_the_grant_is_kept_for_a_retry()
        {
            Skip.If(Unavailable, "no Redis available");

            await Processor(new RecordingSandbox(), new FakeRunAccessTokenResolver(Token))
                .ProcessAsync(await QueueAsync(Envelope(), Grant), CancellationToken.None);

            var record = await _db!.HashGetAllAsync(RedisKeys.Run(_runId));
            var everything = string.Join("\n",
                record.Select(e => $"{e.Name}={e.Value}").Concat((await ResultEntryAsync()).Select(e => $"{e.Name}={e.Value}")));
            everything.Should().NotContain(Token);
            record.Should().Contain(e => e.Name == RedisKeys.RunDelegationField && e.Value == Grant,
                "a retry re-reads the run hash and redeems the same grant afresh");
            _logs.All.Should().NotContain(Token).And.NotContain(Grant);
        }

        [SkippableFact]
        public async Task An_error_message_echoing_the_token_is_masked_before_it_is_stored()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new RecordingSandbox(() => new SandboxResult
            {
                Output = new SandboxOutput
                {
                    Ok = false, ErrorCode = ErrorCodes.UserRuntimeError, ErrorMessage = $"401 for Bearer {Token}",
                },
                ExitCode = SandboxExit.UserError, OomKilled = false, TimedOut = false, DurationMs = 5,
            });

            await Processor(sandbox, new FakeRunAccessTokenResolver(Token))
                .ProcessAsync(await QueueAsync(Envelope(), Grant), CancellationToken.None);

            var message = Field(await ResultEntryAsync(), "errorMessage")!;
            message.Should().Contain("[redacted]").And.NotContain(Token);
        }

        // ---------- no token, and the run still runs ----------

        [SkippableFact]
        public async Task A_run_without_a_grant_never_asks_IAM_and_gets_no_token()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new RecordingSandbox();
            var tokens = new FakeRunAccessTokenResolver(Token);

            await Processor(sandbox, tokens).ProcessAsync(await QueueAsync(Envelope(), grant: null), CancellationToken.None);

            tokens.Calls.Should().BeEmpty();
            sandbox.Calls.Should().Be(1);
            SeenToken(sandbox.EnvelopeContentSeen!).Should().BeNull();
        }

        [SkippableFact]
        public async Task A_grant_IAM_will_not_redeem_still_runs_the_function_without_a_token()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new RecordingSandbox(() => new SandboxResult
            {
                Output = new SandboxOutput { Ok = true, ResultJson = "{}" },
                ExitCode = 0, OomKilled = false, TimedOut = false, DurationMs = 5,
            });

            var disposition = await Processor(sandbox, new FakeRunAccessTokenResolver(null))
                .ProcessAsync(await QueueAsync(Envelope(), Grant), CancellationToken.None);

            disposition.Should().Be(RunProcessor.Disposition.Complete);
            sandbox.Calls.Should().Be(1);
            SeenToken(sandbox.EnvelopeContentSeen!).Should().BeNull();
            Field(await ResultEntryAsync(), "status").Should().Be(RunStatuses.Succeeded);
            _logs.All.Should().Contain("starts without ctx.blocks.accessToken");
        }

        [SkippableTheory]
        [InlineData(null, true)]
        [InlineData("user_1", false)]
        public async Task A_grant_beside_an_unauthenticated_caller_is_not_redeemed(string? userId, bool authenticated)
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new RecordingSandbox();
            var tokens = new FakeRunAccessTokenResolver(Token);

            await Processor(sandbox, tokens).ProcessAsync(
                await QueueAsync(Envelope(userId: userId, authenticated: authenticated), Grant), CancellationToken.None);

            tokens.Calls.Should().BeEmpty();
            sandbox.Calls.Should().Be(1);
            SeenToken(sandbox.EnvelopeContentSeen!).Should().BeNull();
        }

        [SkippableFact]
        public async Task An_entry_whose_tenant_disagrees_with_the_envelope_redeems_nothing()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new RecordingSandbox();
            var tokens = new FakeRunAccessTokenResolver(Token);

            // Secret resolution refuses the same mismatch, so the run fails before the sandbox; the
            // point here is that the grant is never redeemed against either tenant.
            await Processor(sandbox, tokens).ProcessAsync(
                await QueueAsync(Envelope(tenant: "other_tenant"), Grant), CancellationToken.None);

            tokens.Calls.Should().BeEmpty();
            sandbox.Calls.Should().Be(0);
        }

        [SkippableFact]
        public async Task A_token_carried_by_the_queue_is_refused_and_no_grant_is_redeemed()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new RecordingSandbox();
            var tokens = new FakeRunAccessTokenResolver(Token);

            await Processor(sandbox, tokens).ProcessAsync(
                await QueueAsync(Envelope(blocks: new { accessToken = "planted" }), Grant), CancellationToken.None);

            sandbox.Calls.Should().Be(0);
            tokens.Calls.Should().BeEmpty();
            var result = await ResultEntryAsync();
            Field(result, "errorCode").Should().Be(ErrorCodes.RuntimeStartFailed);
            Field(result, "errorMessage").Should().Contain("blocks.accessToken");
        }
    }
}
