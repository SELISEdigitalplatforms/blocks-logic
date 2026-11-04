using System.Text.Json;
using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Runs;
using Blocks.FunctionRunner.Sandbox;
using Blocks.FunctionRunner.SecretStore;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// <c>optionalEnv</c>: keys the function can run without (client-credentials keys, for example). A reference in
    /// one whose secret is simply not there leaves the key out of <c>ctx.env</c>; any other fault still fails the run,
    /// and a required key still fails exactly as before. No Redis needed.
    /// </summary>
    public sealed class OptionalEnvTests
    {
        private const string Tenant = "tenant_optional";
        private const string UrlValue = "mongodb://resolved";
        private const string IdValue = "client_RESOLVED_1";

        private sealed class NamedResolver(Dictionary<string, string> values, Dictionary<string, string[]> idsByName)
            : IRunSecretResolver
        {
            public FakeRunSecretResolver Values { get; } = new(values);

            public Task<SecretLookup> ResolveAsync(
                string tenantId, EnvSecretReferences.Caller caller, IReadOnlyCollection<string> secretIds, CancellationToken cancellationToken)
                => Values.ResolveAsync(tenantId, caller, secretIds, cancellationToken);

            public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> FindIdsByNameAsync(
                string tenantId, EnvSecretReferences.Caller caller, IReadOnlyCollection<string> names, CancellationToken cancellationToken)
            {
                IReadOnlyDictionary<string, IReadOnlyList<string>> found = names.ToDictionary(
                    n => n, n => (IReadOnlyList<string>)(idsByName.TryGetValue(n, out var ids) ? ids : []), StringComparer.Ordinal);
                return Task.FromResult(found);
            }
        }

        private sealed class NeverRuns : ISandbox
        {
            public Task<SandboxResult> RunAsync(
                string runId, string image, string envelopeHostPath, RunLimits limits, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("not used");
        }

        private sealed class ResolvesAnything : IImageResolver
        {
            public Task<string?> EnsureAsync(
                string reference, CancellationToken token, string? artifactUrl = null, string? artifactSha256 = null) =>
                Task.FromResult<string?>(reference);
        }

        private static RunProcessor Processor(IRunSecretResolver resolver)
        {
            var options = Microsoft.Extensions.Options.Options.Create(new RunnerOptions
            {
                RunnerId = "test-runner",
                RunsDir = Path.GetTempPath(),
            });
            var budget = new HostRoom(options, new RoomyHost(), NullLogger<HostRoom>.Instance);
            return new RunProcessor(null!, new NeverRuns(), new ResolvesAnything(), budget, resolver,
                new FakeRunAccessTokenResolver(), options, NullLogger<RunProcessor>.Instance);
        }

        private static readonly string[] DevRole = ["dev"];

        private static string Envelope(object env, string[] optional) =>
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["run"] = new { id = "r", attempt = 1 },
                ["context"] = new { tenantId = Tenant, userId = "user_1", organizationId = "org_1", roles = DevRole },
                ["env"] = env,
                ["maskedEnv"] = Array.Empty<string>(),
                ["optionalEnv"] = optional,
                ["input"] = new { },
                ["limits"] = new { timeoutMs = 5000 },
            });

        private static readonly RunJob Job = new() { RunId = "run_o", FunctionId = "fn_o", TenantId = Tenant, Image = "img", Attempt = 1 };

        private static readonly object Env = new
        {
            DB_URL = "{{secret.sec_url}}",
            CLIENT_ID = "{{secret.sec_id}}",
            CLIENT_SECRET = "{{secret.sec_gone}}",
            PLAIN = "x",
        };

        private static readonly string[] ClientKeys = ["CLIENT_ID", "CLIENT_SECRET"];

        private static JsonElement EnvOf(string envelope) => JsonDocument.Parse(envelope).RootElement.GetProperty("env");

        [Fact]
        public void Collect_reads_the_optional_keys()
        {
            EnvSecretReferences.Collect(Envelope(Env, ClientKeys)).OptionalKeys.Should().BeEquivalentTo(ClientKeys);
        }

        [Fact]
        public async Task A_gone_secret_in_an_optional_key_leaves_only_that_key_out()
        {
            var resolver = new NamedResolver(new() { ["sec_url"] = UrlValue, ["sec_id"] = IdValue }, new());

            var prepared = await Processor(resolver).ResolveSecretsAsync(Job, Envelope(Env, ClientKeys), CancellationToken.None);

            prepared.Failure.Should().BeNull();
            var env = EnvOf(prepared.Envelope!);
            env.TryGetProperty("CLIENT_SECRET", out _).Should().BeFalse("the function sees it as undefined");
            env.GetProperty("CLIENT_ID").GetString().Should().Be(IdValue, "an optional key that resolves is kept");
            env.GetProperty("DB_URL").GetString().Should().Be(UrlValue);
            env.GetProperty("PLAIN").GetString().Should().Be("x");
            prepared.Values.Should().BeEquivalentTo([UrlValue, IdValue]);
            prepared.Envelope.Should().NotContain("sec_gone", "no reference text is left for the sandbox");
        }

        [Theory]
        [InlineData(SecretUnresolvedReasons.Deleted)]
        [InlineData(SecretUnresolvedReasons.NoValue)]
        public async Task Every_absence_reason_leaves_the_key_out(string reason)
        {
            var resolver = new NamedResolver(new() { ["sec_url"] = UrlValue, ["sec_id"] = IdValue }, new());
            resolver.Values.Reasons["sec_gone"] = reason;

            var prepared = await Processor(resolver).ResolveSecretsAsync(Job, Envelope(Env, ClientKeys), CancellationToken.None);

            prepared.Failure.Should().BeNull();
            EnvOf(prepared.Envelope!).TryGetProperty("CLIENT_SECRET", out _).Should().BeFalse();
        }

        [Theory]
        [InlineData(SecretUnresolvedReasons.Locked)]
        [InlineData(SecretUnresolvedReasons.AccessDenied)]
        [InlineData(SecretUnresolvedReasons.Invalid)]
        public async Task A_fault_that_is_not_an_absence_still_fails_an_optional_key(string reason)
        {
            var resolver = new NamedResolver(new() { ["sec_url"] = UrlValue, ["sec_id"] = IdValue }, new());
            resolver.Values.Reasons["sec_gone"] = reason;

            var prepared = await Processor(resolver).ResolveSecretsAsync(Job, Envelope(Env, ClientKeys), CancellationToken.None);

            prepared.Envelope.Should().BeNull();
            prepared.Failure!.Value.Code.Should().Be(ErrorCodes.SecretUnresolved);
            prepared.Failure!.Value.Message.Should().Contain("CLIENT_SECRET").And.Contain(reason);
        }

        [Fact]
        public async Task A_required_key_still_fails_the_run()
        {
            var resolver = new NamedResolver(new() { ["sec_id"] = IdValue }, new());

            var prepared = await Processor(resolver).ResolveSecretsAsync(Job, Envelope(Env, ClientKeys), CancellationToken.None);

            prepared.Failure!.Value.Code.Should().Be(ErrorCodes.SecretUnresolved);
            prepared.Failure!.Value.Message.Should().Contain("DB_URL").And.NotContain("CLIENT_SECRET");
        }

        [Fact]
        public async Task An_old_envelope_without_optionalEnv_fails_as_before()
        {
            var resolver = new NamedResolver(new() { ["sec_url"] = UrlValue, ["sec_id"] = IdValue }, new());
            var envelope = Envelope(Env, []).Replace(",\"optionalEnv\":[]", string.Empty, StringComparison.Ordinal);

            var prepared = await Processor(resolver).ResolveSecretsAsync(Job, envelope, CancellationToken.None);

            prepared.Failure!.Value.Message.Should().Contain("CLIENT_SECRET");
        }

        [Fact]
        public async Task An_unknown_name_in_an_optional_key_is_left_out_and_the_rest_is_still_read()
        {
            var resolver = new NamedResolver(new() { ["sec_url"] = UrlValue }, new());
            var envelope = Envelope(new { DB_URL = "{{secret.sec_url}}", CLIENT_ID = "{{secretName.client-id}}" }, ["CLIENT_ID"]);

            var prepared = await Processor(resolver).ResolveSecretsAsync(Job, envelope, CancellationToken.None);

            prepared.Failure.Should().BeNull();
            var env = EnvOf(prepared.Envelope!);
            env.TryGetProperty("CLIENT_ID", out _).Should().BeFalse();
            env.GetProperty("DB_URL").GetString().Should().Be(UrlValue);
        }

        [Fact]
        public async Task A_duplicate_name_still_fails_an_optional_key()
        {
            var resolver = new NamedResolver(
                new() { ["s1"] = IdValue, ["s2"] = "other" }, new() { ["client-id"] = ["s1", "s2"] });
            var envelope = Envelope(new { CLIENT_ID = "{{secretName.client-id}}" }, ["CLIENT_ID"]);

            var prepared = await Processor(resolver).ResolveSecretsAsync(Job, envelope, CancellationToken.None);

            prepared.Failure!.Value.Message.Should().Contain(SecretUnresolvedReasons.DuplicateName);
            resolver.Values.Calls.Should().BeEmpty("a run refused for a name never reads a value");
        }

        [Fact]
        public async Task Only_optional_keys_all_gone_runs_with_no_secrets()
        {
            var resolver = new NamedResolver(new(), new());
            var envelope = Envelope(new { CLIENT_SECRET = "{{secret.sec_gone}}", PLAIN = "x" }, ["CLIENT_SECRET"]);

            var prepared = await Processor(resolver).ResolveSecretsAsync(Job, envelope, CancellationToken.None);

            prepared.Failure.Should().BeNull();
            prepared.Values.Should().BeEmpty();
            EnvOf(prepared.Envelope!).TryGetProperty("CLIENT_SECRET", out _).Should().BeFalse();
        }
    }
}
