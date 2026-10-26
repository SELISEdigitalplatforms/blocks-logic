using System.Text.Json;
using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Maintenance;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Runs;
using Blocks.FunctionRunner.Sandbox;
using Blocks.FunctionRunner.SecretStore;
using Blocks.FunctionRunner.Builds;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// <c>{{secretName.&lt;name&gt;}}</c> — an imported reference not yet bound to an id (2026-10-18). The runner looks
    /// the name up exactly at run start, every run (never cached), and reads its value in the same single batch as the
    /// ids. One match runs; none or several refuse the run as <c>SECRET_UNRESOLVED</c>, naming the variable, without
    /// reading a single value. No Redis needed: <see cref="RunProcessor.ResolveSecretsAsync"/> touches only the resolver.
    /// </summary>
    public sealed class SecretNameReferenceTests
    {
        private const string Tenant = "tenant_names";
        private const string StripeValue = "sk_live_NAMED_4242";
        private const string TokenValue = "tok_ID_9191";

        /// <summary>The fake resolver plus a name catalog, recording every name lookup.</summary>
        private sealed class NamedResolver(
            Dictionary<string, string> values, Dictionary<string, string[]> idsByName, Exception? nameLookupFails = null)
            : IRunSecretResolver
        {
            private readonly FakeRunSecretResolver _values = new(values);

            public Dictionary<string, string[]> IdsByName { get; } = idsByName;

            public List<string[]> NameCalls { get; } = [];

            public List<(string TenantId, EnvSecretReferences.Caller Caller, string[] Ids)> ValueCalls => _values.Calls;

            public Task<SecretLookup> ResolveAsync(
                string tenantId, EnvSecretReferences.Caller caller, IReadOnlyCollection<string> secretIds, CancellationToken cancellationToken)
                => _values.ResolveAsync(tenantId, caller, secretIds, cancellationToken);

            public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> FindIdsByNameAsync(
                string tenantId, EnvSecretReferences.Caller caller, IReadOnlyCollection<string> names, CancellationToken cancellationToken)
            {
                NameCalls.Add(names.ToArray());
                if (nameLookupFails is not null) throw nameLookupFails;
                IReadOnlyDictionary<string, IReadOnlyList<string>> found = names.ToDictionary(
                    n => n, n => (IReadOnlyList<string>)(IdsByName.TryGetValue(n, out var ids) ? ids : []), StringComparer.Ordinal);
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

        private static string Envelope(object env) =>
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["run"] = new { id = "r", attempt = 1 },
                ["context"] = new { tenantId = Tenant, userId = "user_1", organizationId = "org_1", roles = DevRole },
                ["env"] = env,
                ["maskedEnv"] = Array.Empty<string>(),
                ["input"] = new { },
                ["limits"] = new { timeoutMs = 5000 },
            });

        private static readonly RunJob Job = new() { RunId = "run_n", FunctionId = "fn_n", TenantId = Tenant, Image = "img", Attempt = 1 };

        private static readonly object MixedEnv = new
        {
            STRIPE_KEY = "{{secretName.stripe-key}}",
            AUTH = "Bearer {{secret.sec_token}}",
            PLAIN = "x",
        };

        // ------------------------------------------------------------ the parser ----

        [Fact]
        public void Collect_finds_names_and_ids_per_key()
        {
            var plan = EnvSecretReferences.Collect(Envelope(MixedEnv));

            plan.IsEmpty.Should().BeFalse();
            plan.Names.Should().BeEquivalentTo(["stripe-key"]);
            plan.Ids.Should().BeEquivalentTo(["sec_token"]);
            plan.NamesByKey.Keys.Should().BeEquivalentTo(["STRIPE_KEY"]);
        }

        [Fact]
        public void A_names_only_envelope_is_not_empty()
        {
            EnvSecretReferences.Collect(Envelope(new { A = "{{secretName.a}}" })).IsEmpty.Should().BeFalse();
        }

        [Fact]
        public void Apply_puts_the_bound_value_in_and_masks_it_in_one_pass()
        {
            var envelope = Envelope(new { A = "{{secretName.a}}-{{secret.b}}" });
            var plan = EnvSecretReferences.Collect(envelope);
            // A value that itself looks like a reference is delivered as text, never expanded again.
            var values = new Dictionary<string, string> { ["id_a"] = "{{secret.b}}", ["b"] = "vb_value" };

            var applied = EnvSecretReferences.Apply(envelope, plan, values, new Dictionary<string, string> { ["a"] = "id_a" });

            using var doc = JsonDocument.Parse(applied);
            doc.RootElement.GetProperty("env").GetProperty("A").GetString().Should().Be("{{secret.b}}-vb_value");
            doc.RootElement.GetProperty("maskedEnv").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(["A"]);
            doc.RootElement.GetProperty("maskedValues").EnumerateArray().Select(e => e.GetString())
                .Should().BeEquivalentTo(["{{secret.b}}", "vb_value"]);
        }

        // ------------------------------------------------------- run-start binding ----

        [Fact]
        public async Task One_matching_name_is_read_in_the_same_batch_as_the_ids()
        {
            var resolver = new NamedResolver(
                new() { ["sec_stripe"] = StripeValue, ["sec_token"] = TokenValue },
                new() { ["stripe-key"] = ["sec_stripe"] });

            var prepared = await Processor(resolver).ResolveSecretsAsync(Job, Envelope(MixedEnv), CancellationToken.None);

            prepared.Failure.Should().BeNull();
            using var doc = JsonDocument.Parse(prepared.Envelope!);
            doc.RootElement.GetProperty("env").GetProperty("STRIPE_KEY").GetString().Should().Be(StripeValue);
            doc.RootElement.GetProperty("env").GetProperty("AUTH").GetString().Should().Be($"Bearer {TokenValue}");
            prepared.Values.Should().BeEquivalentTo([StripeValue, TokenValue], "both values are masked in logs");

            resolver.NameCalls.Should().ContainSingle().Which.Should().BeEquivalentTo(["stripe-key"]);
            resolver.ValueCalls.Should().ContainSingle("all values are read in one request");
            resolver.ValueCalls[0].Ids.Should().BeEquivalentTo(["sec_token", "sec_stripe"]);
            resolver.ValueCalls[0].Caller.UserId.Should().Be("user_1");
        }

        [Fact]
        public async Task No_secret_with_the_name_fails_the_run_naming_the_variable_and_reads_no_value()
        {
            var resolver = new NamedResolver(new() { ["sec_token"] = TokenValue }, new());

            var prepared = await Processor(resolver).ResolveSecretsAsync(Job, Envelope(MixedEnv), CancellationToken.None);

            prepared.Envelope.Should().BeNull();
            prepared.Failure!.Value.Code.Should().Be(ErrorCodes.SecretUnresolved);
            prepared.Failure!.Value.Message.Should().Contain("STRIPE_KEY").And.Contain("stripe-key")
                .And.Contain(SecretUnresolvedReasons.NoSuchName);
            resolver.ValueCalls.Should().BeEmpty("a run refused for a name never reads a value");
        }

        [Fact]
        public async Task Two_secrets_with_the_name_fail_clearly()
        {
            var resolver = new NamedResolver(
                new() { ["s1"] = StripeValue, ["s2"] = "other_value", ["sec_token"] = TokenValue },
                new() { ["stripe-key"] = ["s1", "s2"] });

            var prepared = await Processor(resolver).ResolveSecretsAsync(Job, Envelope(MixedEnv), CancellationToken.None);

            prepared.Failure!.Value.Code.Should().Be(ErrorCodes.SecretUnresolved);
            prepared.Failure!.Value.Message.Should().Contain("more than one secret has that name").And.Contain("stripe-key");
            prepared.Failure!.Value.Message.Should().NotContain(StripeValue);
            resolver.ValueCalls.Should().BeEmpty();
        }

        [Fact]
        public async Task Nothing_is_cached_so_adding_the_secret_makes_the_next_run_work()
        {
            var resolver = new NamedResolver(new() { ["sec_new"] = StripeValue }, new());
            var processor = Processor(resolver);
            var envelope = Envelope(new { STRIPE_KEY = "{{secretName.stripe-key}}" });

            var first = await processor.ResolveSecretsAsync(Job, envelope, CancellationToken.None);
            first.Failure.Should().NotBeNull();

            // The only step the user takes after an import: create the secret with that name.
            resolver.IdsByName["stripe-key"] = ["sec_new"];
            var second = await processor.ResolveSecretsAsync(Job, envelope, CancellationToken.None);

            second.Failure.Should().BeNull();
            resolver.NameCalls.Should().HaveCount(2, "the name is looked up again on every run; a miss is never kept");
        }

        [Fact]
        public async Task A_name_lookup_the_store_cannot_answer_is_retryable()
        {
            var resolver = new NamedResolver(new(), new(), new SecretStoreUnavailableException("the key vault could not be read"));

            var prepared = await Processor(resolver).ResolveSecretsAsync(Job, Envelope(MixedEnv), CancellationToken.None);

            prepared.Failure!.Value.Code.Should().Be(ErrorCodes.SecretStoreUnavailable);
        }

        [Fact]
        public async Task A_resolver_that_knows_no_names_refuses_the_run_instead_of_handing_over_the_text()
        {
            var resolver = new FakeRunSecretResolver(new() { ["sec_token"] = TokenValue });

            var prepared = await Processor(resolver).ResolveSecretsAsync(Job, Envelope(MixedEnv), CancellationToken.None);

            prepared.Failure!.Value.Code.Should().Be(ErrorCodes.SecretUnresolved);
            resolver.Calls.Should().BeEmpty();
        }

        // ------------------------------------------------- image names with a tenant ----

        [Fact]
        public void The_tenant_image_form_is_accepted_by_registry_cleanup_and_digest_selection()
        {
            const string registry = "127.0.0.1:5000";
            var newForm = $"{registry}/fn/tenant-a/d238adf2-e71b-412b-b352-4d269b413797@sha256:7954c865a2c4";

            RegistryClient.TryParse(newForm, registry, out var name, out var digest).Should().BeTrue();
            name.Should().Be("fn/tenant-a/d238adf2-e71b-412b-b352-4d269b413797");
            digest.Should().Be("sha256:7954c865a2c4");

            ImageGc.RepositoryOf($"{registry}/fn/tenant-a/abc:568a5ad80f90-e33a6815d061").Should().Be($"{registry}/fn/tenant-a/abc");
            BuildProcessor.SelectPushedDigest(
                    [$"{registry}/fn/abc@sha256:{new string('a', 64)}", $"{registry}/fn/tenant-a/abc@sha256:{new string('b', 64)}"],
                    $"{registry}/fn/tenant-a/abc")
                .Should().Be($"{registry}/fn/tenant-a/abc@sha256:{new string('b', 64)}",
                    "the same function id in another tenant (or the old form) is never mistaken for this build");
        }

        [Fact]
        public void The_old_image_form_is_still_accepted()
        {
            const string registry = "127.0.0.1:5000";
            RegistryClient.TryParse($"{registry}/fn/abc@sha256:aa", registry, out var name, out _).Should().BeTrue();
            name.Should().Be("fn/abc");
            ImageGc.RepositoryOf($"{registry}/fn/abc:tag").Should().Be($"{registry}/fn/abc");
        }
    }
}
