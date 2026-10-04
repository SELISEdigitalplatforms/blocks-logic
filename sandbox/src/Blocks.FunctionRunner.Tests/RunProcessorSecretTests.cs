using System.Text.Json;
using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Protocol;
using Blocks.FunctionRunner.Runs;
using Blocks.FunctionRunner.Sandbox;
using Blocks.FunctionRunner.SecretStore;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>Serialise Redis-backed RunProcessor secret tests so admission slots do not race.</summary>
    [CollectionDefinition("FunctionRunner.Redis.Serial", DisableParallelization = true)]
    public sealed class FunctionRunnerRedisSerialDefinition;

    /// <summary>
    /// The runner resolves a run's <c>{{secret.&lt;id&gt;}}</c> references itself, right before
    /// the sandbox starts. End to end through <see cref="RunProcessor"/> against a real Redis
    /// (skipping cleanly without one; set TEST_REDIS), with the sandbox and the secret store
    /// faked: what the sandbox is handed, what is refused before it is ever started, and that a
    /// value never reaches Redis, the result entry or a log line.
    /// </summary>
    [Collection("FunctionRunner.Redis.Serial")]
    public sealed class RunProcessorSecretTests : IAsyncLifetime
    {
        // Unique per instance so parallel xUnit workers do not share RedisKeys.TenantSlots
        // and flake ProcessAsync with Disposition.Deferred under Sonar's full suite run.
        private readonly string _tenant = $"tenant_sec_{Guid.NewGuid():N}";
        private const string StripeValue = "sk_live_RESOLVED_4242";
        private const string TokenValue = "tok_RESOLVED_9191";

        private ConnectionMultiplexer? _redis;
        private IDatabase? _db;
        private readonly string _runsDir = Path.Combine(Path.GetTempPath(), $"fn-runs-{Guid.NewGuid():N}");
        private readonly string _runId = $"run_sec_{Guid.NewGuid():N}";
        private readonly string _functionId = $"fn_sec_{Guid.NewGuid():N}";
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
                    RedisKeys.TenantSlots(_tenant), RedisKeys.Result(_runId), RedisKeys.Logs(_runId),
                ]);
            }
            if (_redis is not null) await _redis.DisposeAsync();
            if (Directory.Exists(_runsDir)) Directory.Delete(_runsDir, recursive: true);
        }

        private bool Unavailable => _db is null;

        private string RunDir => Path.Combine(_runsDir, _runId);

        private sealed class RecordingSandbox(Func<SandboxResult>? behave = null) : ISandbox
        {
            public int Calls { get; private set; }

            public string? EnvelopeContentSeen { get; private set; }

            public Task<SandboxResult> RunAsync(
                string runId, string image, string envelopeHostPath, RunLimits limits, CancellationToken cancellationToken)
            {
                Calls++;
                EnvelopeContentSeen = File.ReadAllText(envelopeHostPath);
                if (behave is not null) return Task.FromResult(behave());
                return Task.FromResult(new SandboxResult
                {
                    Output = new SandboxOutput(),
                    ExitCode = 0,
                    OomKilled = false,
                    TimedOut = false,
                    DurationMs = 5,
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

        private RunProcessor Processor(ISandbox sandbox, IRunSecretResolver resolver)
        {
            var options = Microsoft.Extensions.Options.Options.Create(new RunnerOptions
            {
                RunnerId = "test-runner",
                RunsDir = _runsDir,
                MaxActiveSandboxes = 32,
                MaxSandboxesPerTenant = 64,
                ReservedHostMemoryMb = 0,
            });
            var budget = new HostBudget(options, new RoomyHost(), new SandboxFootprint(), NullLogger<HostBudget>.Instance);

            return new RunProcessor(_db!, sandbox, new ResolvesAnything(), budget, resolver, new FakeRunAccessTokenResolver(), options, _logs.For<RunProcessor>())
            {
                EnvelopeGroupHandoff = (_, _) => { },
            };
        }

        private static readonly string[] DevRole = ["dev"];

        private string Envelope(object env, string? contextTenant = null, object? input = null) =>
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["run"] = new { id = "r", attempt = 1 },
                ["context"] = new { tenantId = contextTenant ?? _tenant, userId = "user_1", organizationId = "org_1", roles = DevRole },
                ["env"] = env,
                ["maskedEnv"] = Array.Empty<string>(),
                ["input"] = input ?? new { },
                ["limits"] = new { timeoutMs = 5000 },
            });

        private static readonly object ReferencingEnv = new
        {
            STRIPE_KEY = "{{secret.sec_stripe}}",
            AUTH = "Bearer {{secret.sec_token}}",
            BOTH = "{{secret.sec_stripe}}|{{secret.sec_token}}|{{secret.sec_stripe}}",
            PLAIN = "https://api.example.com",
        };

        private static FakeRunSecretResolver Resolves() => new(new Dictionary<string, string>
        {
            ["sec_stripe"] = StripeValue,
            ["sec_token"] = TokenValue,
        });

        private async Task<RunJob> QueueAsync(
            string envelope,
            int protocol = RedisKeys.RunProtocolVersion,
            string? tenant = null,
            bool useInstanceTenant = true)
        {
            if (useInstanceTenant && tenant is null) tenant = _tenant;
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
                TenantId = tenant,
                Image = "img@sha256:abc",
                Attempt = 1,
                Protocol = protocol,
            };
        }

        /// <summary>
        /// Admission (host / tenant / function slots) can return Deferred under a shared CI Redis.
        /// Retry the same queued run until the processor is past those gates.
        /// </summary>
        private async Task<RunProcessor.Disposition> ProcessUntilAdmittedAsync(
            ISandbox sandbox, IRunSecretResolver resolver, string envelope)
        {
            var job = await QueueAsync(envelope);
            var disposition = RunProcessor.Disposition.Deferred;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                disposition = await Processor(sandbox, resolver).ProcessAsync(job, CancellationToken.None);
                if (disposition != RunProcessor.Disposition.Deferred) return disposition;
                await Task.Delay(25);
            }
            return disposition;
        }

        private async Task<NameValueEntry[]> ResultEntryAsync()
        {
            // The results stream is shared across the whole FunctionRunner suite. Reading
            // from "-" without a bound misses the newest entries once hundreds of tests have
            // already appended; search newest-first instead.
            for (var attempt = 0; attempt < 25; attempt++)
            {
                var entries = await _db!.StreamRangeAsync(
                    RedisKeys.ResultsStream, "-", "+", count: 500, messageOrder: Order.Descending);
                var match = entries
                    .Select(e => e.Values)
                    .FirstOrDefault(v => v.Any(f => f.Name == "runId" && f.Value == _runId));
                if (match is not null) return match;
                await Task.Delay(40);
            }
            throw new InvalidOperationException($"no results-stream entry for run {_runId}");
        }

        private static string? Field(NameValueEntry[] entry, string name) =>
            entry.FirstOrDefault(f => f.Name == name).Value;

        /// <summary>Everything this run left in Redis: the run record and its result entry.</summary>
        private async Task<string> EverythingInRedisAsync()
        {
            var record = await _db!.HashGetAllAsync(RedisKeys.Run(_runId));
            var result = await ResultEntryAsync();
            return string.Join("\n",
                record.Select(e => $"{e.Name}={e.Value}").Concat(result.Select(e => $"{e.Name}={e.Value}")));
        }

        private static void NoValueAnywhere(string text)
        {
            text.Should().NotContain(StripeValue).And.NotContain(TokenValue);
        }

        // ---------- the sandbox receives values ----------

        [SkippableFact]
        public async Task The_sandbox_receives_every_reference_resolved_and_the_mask_lists_completed()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new RecordingSandbox();
            var resolver = Resolves();

            var disposition = await Processor(sandbox, resolver).ProcessAsync(
                await QueueAsync(Envelope(ReferencingEnv)), CancellationToken.None);

            disposition.Should().Be(RunProcessor.Disposition.Complete);
            sandbox.Calls.Should().Be(1);

            using var seen = JsonDocument.Parse(sandbox.EnvelopeContentSeen!);
            var env = seen.RootElement.GetProperty("env");
            env.GetProperty("STRIPE_KEY").GetString().Should().Be(StripeValue);
            env.GetProperty("AUTH").GetString().Should().Be($"Bearer {TokenValue}");
            env.GetProperty("BOTH").GetString().Should().Be($"{StripeValue}|{TokenValue}|{StripeValue}");
            env.GetProperty("PLAIN").GetString().Should().Be("https://api.example.com");

            seen.RootElement.GetProperty("maskedEnv").EnumerateArray().Select(e => e.GetString())
                .Should().BeEquivalentTo(["STRIPE_KEY", "AUTH", "BOTH"]);
            seen.RootElement.GetProperty("maskedValues").EnumerateArray().Select(e => e.GetString())
                .Should().BeEquivalentTo([StripeValue, TokenValue], "the bootstrap masks the bare values too");

            resolver.Calls.Should().ContainSingle("every reference is resolved in one lookup");
            resolver.Calls[0].TenantId.Should().Be(_tenant);
            resolver.Calls[0].Ids.Should().BeEquivalentTo(["sec_stripe", "sec_token"], "each id once");
            resolver.Calls[0].Caller.UserId.Should().Be("user_1");

            Directory.Exists(RunDir).Should().BeFalse("the resolved envelope file is gone once the run ends");
        }

        [SkippableFact]
        public async Task A_value_never_reaches_Redis_the_result_entry_or_a_log_line()
        {
            Skip.If(Unavailable, "no Redis available");

            await Processor(new RecordingSandbox(), Resolves()).ProcessAsync(
                await QueueAsync(Envelope(ReferencingEnv)), CancellationToken.None);

            var redis = await EverythingInRedisAsync();
            NoValueAnywhere(redis);
            redis.Should().Contain("{{secret.sec_stripe}}", "the run record keeps the references a retry resolves again");
            NoValueAnywhere(_logs.All);
        }

        [SkippableFact]
        public async Task A_reference_in_the_callers_input_is_not_resolved()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new RecordingSandbox();
            var resolver = Resolves();

            await Processor(sandbox, resolver).ProcessAsync(
                await QueueAsync(Envelope(new { PLAIN = "p" }, input: new { body = "{{secret.sec_stripe}}" })),
                CancellationToken.None);

            resolver.Calls.Should().BeEmpty();
            sandbox.EnvelopeContentSeen.Should().Contain("{{secret.sec_stripe}}").And.NotContain(StripeValue);
        }

        [SkippableFact]
        public async Task An_error_message_that_reaches_the_runner_unmasked_is_masked_before_it_is_stored()
        {
            // The bootstrap masks everything it writes; this is the runner's own backstop for the
            // one string it forwards verbatim.
            Skip.If(Unavailable, "no Redis available");
            var output = new SandboxOutput
            {
                Ok = false,
                ErrorCode = ErrorCodes.UserRuntimeError,
                ErrorMessage = $"upstream said 401 for Bearer {TokenValue} and {StripeValue}",
            };
            var sandbox = new RecordingSandbox(() => new SandboxResult
            {
                Output = output, ExitCode = SandboxExit.UserError, OomKilled = false, TimedOut = false, DurationMs = 5,
            });

            await Processor(sandbox, Resolves()).ProcessAsync(await QueueAsync(Envelope(ReferencingEnv)), CancellationToken.None);

            var message = Field(await ResultEntryAsync(), "errorMessage")!;
            message.Should().Contain("[redacted]");
            NoValueAnywhere(message);
        }

        [SkippableFact]
        public async Task A_host_failure_echoing_a_value_is_masked_before_it_is_stored()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new RecordingSandbox(() => new SandboxResult
            {
                Output = new SandboxOutput(), ExitCode = -1, OomKilled = false, TimedOut = false, DurationMs = 1,
                HostFailure = $"the sandbox could not be created: bad env {StripeValue}",
            });

            await Processor(sandbox, Resolves()).ProcessAsync(await QueueAsync(Envelope(ReferencingEnv)), CancellationToken.None);

            var result = await ResultEntryAsync();
            Field(result, "errorCode").Should().Be(ErrorCodes.SandboxStartFailed);
            NoValueAnywhere(Field(result, "errorMessage")!);
        }

        [Theory]
        [InlineData("tok_abc and tok_abc_longer", "[redacted] and [redacted]")]
        [InlineData("nothing here", "nothing here")]
        [InlineData("", "")]
        public void Redaction_masks_longest_first_and_leaves_other_text_alone(string text, string expected)
        {
            RunProcessor.Redact(text, ["tok_abc", "tok_abc_longer", "ab"]).Should().Be(expected);
        }

        [Fact]
        public void Redaction_ignores_values_too_short_to_be_secrets()
        {
            RunProcessor.Redact("a table of absolute values", ["ab"]).Should().Be("a table of absolute values");
        }

        // ---------- refused before the sandbox starts ----------

        [SkippableFact]
        public async Task A_missing_secret_fails_the_run_naming_the_variable_and_never_starts_the_sandbox()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new RecordingSandbox();
            var resolver = new FakeRunSecretResolver(new Dictionary<string, string> { ["sec_stripe"] = StripeValue });
            resolver.Reasons["sec_token"] = SecretUnresolvedReasons.Deleted;

            var disposition = await Processor(sandbox, resolver).ProcessAsync(
                await QueueAsync(Envelope(ReferencingEnv)), CancellationToken.None);

            disposition.Should().Be(RunProcessor.Disposition.Complete);
            sandbox.Calls.Should().Be(0, "a sandbox must never start with a half-resolved environment");
            Directory.Exists(RunDir).Should().BeFalse();

            var result = await ResultEntryAsync();
            Field(result, "status").Should().Be(RunStatuses.Failed);
            Field(result, "errorCode").Should().Be(ErrorCodes.SecretUnresolved);
            var message = Field(result, "errorMessage")!;
            message.Should().Contain("AUTH").And.Contain("BOTH").And.Contain("sec_token").And.Contain("deleted");
            message.Should().NotContain("STRIPE_KEY", "that variable resolved");
            NoValueAnywhere(message);
            NoValueAnywhere(await EverythingInRedisAsync());
            NoValueAnywhere(_logs.All);
        }

        [SkippableFact]
        public async Task A_store_outage_is_reported_as_retryable_and_never_starts_the_sandbox()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new RecordingSandbox();
            var resolver = new FakeRunSecretResolver(@throw: new SecretStoreUnavailableException(
                "the key vault could not be read", new InvalidOperationException($"inner detail {StripeValue}")));

            var disposition = await ProcessUntilAdmittedAsync(sandbox, resolver, Envelope(ReferencingEnv));

            disposition.Should().Be(RunProcessor.Disposition.Complete);
            sandbox.Calls.Should().Be(0);
            Directory.Exists(RunDir).Should().BeFalse();
            var result = await ResultEntryAsync();
            Field(result, "status").Should().Be(RunStatuses.Failed);
            Field(result, "errorCode").Should().Be(ErrorCodes.SecretStoreUnavailable);
            Field(result, "errorMessage").Should().Contain("key vault");
            // The inner exception is never logged or reported — nothing vouches for its text.
            _logs.All.Should().NotContain("inner detail");
            NoValueAnywhere(Field(result, "errorMessage")!);
        }

        [SkippableFact]
        public async Task An_entry_whose_tenant_disagrees_with_the_envelope_resolves_nothing()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new RecordingSandbox();
            var resolver = Resolves();

            await Processor(sandbox, resolver).ProcessAsync(
                await QueueAsync(Envelope(ReferencingEnv, contextTenant: "someone_else")), CancellationToken.None);

            resolver.Calls.Should().BeEmpty("one tenant's secrets must never be read into another tenant's run");
            sandbox.Calls.Should().Be(0);
            Field(await ResultEntryAsync(), "errorCode").Should().Be(ErrorCodes.SecretUnresolved);
        }

        [SkippableFact]
        public async Task An_entry_with_no_tenant_resolves_nothing()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new RecordingSandbox();
            var resolver = Resolves();

            await Processor(sandbox, resolver).ProcessAsync(
                await QueueAsync(Envelope(ReferencingEnv), tenant: null, useInstanceTenant: false), CancellationToken.None);

            resolver.Calls.Should().BeEmpty();
            sandbox.Calls.Should().Be(0);
            Field(await ResultEntryAsync(), "errorCode").Should().Be(ErrorCodes.SecretUnresolved);
        }

        [SkippableFact]
        public async Task A_refused_envelope_is_refused_before_any_secret_is_read()
        {
            Skip.If(Unavailable, "no Redis available");
            var resolver = Resolves();
            var bad = "{\"run\":{\"id\":\"r\"},\"context\":{\"tenantId\":\"" + _tenant + "\",\"accessToken\":\"x\"},\"env\":{\"A\":\"{{secret.sec_stripe}}\"}}";

            await Processor(new RecordingSandbox(), resolver).ProcessAsync(await QueueAsync(bad), CancellationToken.None);

            resolver.Calls.Should().BeEmpty();
            Field(await ResultEntryAsync(), "errorCode").Should().Be(ErrorCodes.RuntimeStartFailed);
        }

        // ---------- compatibility ----------

        [SkippableFact]
        public async Task A_version_1_entry_runs_as_it_always_did_without_resolution()
        {
            // An older control plane already put the plaintext in env; nothing to resolve.
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new RecordingSandbox();
            var resolver = Resolves();
            var legacy = Envelope(new { STRIPE_KEY = "sk_live_legacy_plaintext" });

            await Processor(sandbox, resolver).ProcessAsync(await QueueAsync(legacy, protocol: 1), CancellationToken.None);

            resolver.Calls.Should().BeEmpty();
            sandbox.Calls.Should().Be(1);
            sandbox.EnvelopeContentSeen.Should().Be(legacy);
        }

        [SkippableFact]
        public async Task A_run_with_no_references_never_asks_the_store()
        {
            Skip.If(Unavailable, "no Redis available");
            var sandbox = new RecordingSandbox();
            var resolver = Resolves();

            await Processor(sandbox, resolver).ProcessAsync(
                await QueueAsync(Envelope(new { PLAIN = "p" })), CancellationToken.None);

            resolver.Calls.Should().BeEmpty();
            sandbox.Calls.Should().Be(1);
        }
    }
}
