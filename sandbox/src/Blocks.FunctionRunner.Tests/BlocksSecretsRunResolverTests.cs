using Blocks.FunctionRunner.Runs;
using Blocks.FunctionRunner.SecretStore;
using Blocks.FunctionRunner.Utils;
using Blocks.Genesis;
using Blocks.Secrets;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The runner's adapter over <c>SeliseBlocks.Secrets.OS</c>. The SDK itself is faked — what is
    /// under test is how its answers and its exceptions become "resolved", "unresolved (why)" or
    /// "store unavailable", the context it is called in, and that no value ever reaches a log.
    /// </summary>
    public sealed class BlocksSecretsRunResolverTests
    {
        private const string Tenant = "tenant_1";
        private const string Value1 = "sk_live_VALUE_ONE_1";
        private const string Value2 = "tok_VALUE_TWO_2";

        private static readonly EnvSecretReferences.Caller Caller = new(Tenant, "user_1", "org_1", ["dev"]);

        private readonly CapturingLoggerProvider _logs = new();
        private readonly FakeSecretService _service = new();
        private readonly FakeTenants _tenants = new();

        private BlocksSecretsRunResolver Resolver(TimeSpan? timeout = null)
        {
            var services = new ServiceCollection();
            services.AddScoped<ISecretService>(_ => _service);
            var provider = services.BuildServiceProvider();
            var resolver = new BlocksSecretsRunResolver(
                provider.GetRequiredService<IServiceScopeFactory>(), _tenants, _logs.For<BlocksSecretsRunResolver>());
            return timeout is null ? resolver : new BlocksSecretsRunResolver(
                provider.GetRequiredService<IServiceScopeFactory>(), _tenants, _logs.For<BlocksSecretsRunResolver>())
            {
                Timeout = timeout.Value,
            };
        }

        private void NoValueLogged() => _logs.All.Should().NotContain(Value1).And.NotContain(Value2);

        [Fact]
        public async Task Every_id_resolves_in_one_batch_call()
        {
            _service.Values["s1"] = Value1;
            _service.Values["s2"] = Value2;

            var lookup = await Resolver().ResolveAsync(Tenant, Caller, ["s1", "s2"], CancellationToken.None);

            lookup.Values.Should().BeEquivalentTo(new Dictionary<string, string> { ["s1"] = Value1, ["s2"] = Value2 });
            lookup.Unresolved.Should().BeEmpty();
            _service.BatchCalls.Should().Be(1);
            _service.SingleCalls.Should().Be(0);
            NoValueLogged();
        }

        [Fact]
        public async Task The_store_is_asked_as_the_runs_tenant_and_caller_authenticated()
        {
            // The SDK refuses an unauthenticated context outright, and an access-listed secret is
            // only readable by the caller the owner allowed — so both matter.
            _service.Values["s1"] = Value1;

            await Resolver().ResolveAsync(Tenant, Caller, ["s1"], CancellationToken.None);

            var seen = _service.ContextSeen!;
            seen.TenantId.Should().Be(Tenant);
            seen.IsAuthenticated.Should().BeTrue();
            seen.UserId.Should().Be("user_1");
            seen.OrganizationId.Should().Be("org_1");
            seen.Roles.Should().Equal("dev");
            seen.OAuthToken.Should().BeEmpty("no credential of any kind is manufactured for the call");
        }

        [Fact]
        public async Task A_refused_batch_is_retried_id_by_id_so_every_broken_reference_is_named()
        {
            _service.Values["ok"] = Value1;
            _service.Failures["gone"] = new SecretNotFoundException("gone");
            _service.Failures["empty"] = new SecretNotFoundException("empty", "VALUE_MISSING");
            _service.Failures["locked"] = new SecretStateException("locked", "read value", "STATUS_LOCKED");
            _service.Failures["deleted"] = new SecretStateException("deleted", "read value", "STATUS_DELETED");
            _service.Failures["private"] = new SecretAccessDeniedException("NOT_IN_ACCESS_LIST");
            _service.Failures["bad id"] = new SecretValidationException("bad", "INVALID_ID");

            var lookup = await Resolver().ResolveAsync(
                Tenant, Caller, ["ok", "gone", "empty", "locked", "deleted", "private", "bad id"], CancellationToken.None);

            lookup.Values.Should().BeEquivalentTo(new Dictionary<string, string> { ["ok"] = Value1 });
            lookup.Unresolved.Should().BeEquivalentTo(new Dictionary<string, string>
            {
                ["gone"] = SecretUnresolvedReasons.NotFound,
                ["empty"] = SecretUnresolvedReasons.NoValue,
                ["locked"] = SecretUnresolvedReasons.Locked,
                ["deleted"] = SecretUnresolvedReasons.Deleted,
                ["private"] = SecretUnresolvedReasons.AccessDenied,
                ["bad id"] = SecretUnresolvedReasons.Invalid,
            });
            _logs.All.Should().Contain("gone");
            NoValueLogged();
        }

        [Fact]
        public async Task A_tenant_the_registry_does_not_know_resolves_nothing_and_asks_nothing()
        {
            _tenants.Known = false;

            var lookup = await Resolver().ResolveAsync(Tenant, Caller, ["s1"], CancellationToken.None);

            lookup.Values.Should().BeEmpty();
            lookup.Unresolved["s1"].Should().Be(SecretUnresolvedReasons.UnknownTenant);
            _service.BatchCalls.Should().Be(0);
        }

        [Fact]
        public async Task A_tenant_registry_failure_is_a_store_outage()
        {
            _tenants.Throw = new TimeoutException("mongo");

            var act = () => Resolver().ResolveAsync(Tenant, Caller, ["s1"], CancellationToken.None);

            await act.Should().ThrowAsync<SecretStoreUnavailableException>();
        }

        [Fact]
        public async Task A_key_vault_failure_is_a_store_outage_whether_batched_or_single()
        {
            _service.BatchThrows = new SecretVaultException("kv down", "Get", "s1");

            var act = () => Resolver().ResolveAsync(Tenant, Caller, ["s1"], CancellationToken.None);

            (await act.Should().ThrowAsync<SecretStoreUnavailableException>())
                .Which.Message.Should().Contain("key vault");
        }

        [Fact]
        public async Task A_key_vault_failure_during_the_id_by_id_retry_is_a_store_outage_and_leaks_nothing()
        {
            _service.Values["ok"] = Value1;
            _service.Failures["gone"] = new SecretNotFoundException("gone");
            _service.Failures["flaky"] = new SecretVaultException($"kv said {Value1}", "Get", "flaky");

            var act = () => Resolver().ResolveAsync(Tenant, Caller, ["ok", "gone", "flaky"], CancellationToken.None);

            var thrown = await act.Should().ThrowAsync<SecretStoreUnavailableException>();
            thrown.Which.Message.Should().NotContain(Value1);
            NoValueLogged();
        }

        [Fact]
        public async Task Any_other_failure_is_a_store_outage_and_its_text_is_not_logged()
        {
            _service.BatchThrows = new InvalidOperationException($"driver dump containing {Value2}");

            var act = () => Resolver().ResolveAsync(Tenant, Caller, ["s1"], CancellationToken.None);

            var thrown = await act.Should().ThrowAsync<SecretStoreUnavailableException>();
            thrown.Which.Message.Should().NotContain(Value2);
            _logs.All.Should().Contain(nameof(InvalidOperationException));
            NoValueLogged();
        }

        [Fact]
        public async Task A_store_that_does_not_answer_in_time_is_a_store_outage()
        {
            _service.Delay = TimeSpan.FromSeconds(30);

            var act = () => Resolver(TimeSpan.FromMilliseconds(100)).ResolveAsync(Tenant, Caller, ["s1"], CancellationToken.None);

            (await act.Should().ThrowAsync<SecretStoreUnavailableException>()).Which.Message.Should().Contain("did not answer");
        }

        [Fact]
        public async Task The_runner_shutting_down_is_a_cancellation_not_an_outage()
        {
            using var cts = new CancellationTokenSource();
            _service.Delay = TimeSpan.FromSeconds(30);
            cts.CancelAfter(50);

            var act = () => Resolver().ResolveAsync(Tenant, Caller, ["s1"], cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task More_ids_than_one_sdk_batch_are_split_rather_than_refused()
        {
            var ids = Enumerable.Range(0, 120).Select(i => $"s{i}").ToArray();
            foreach (var id in ids) _service.Values[id] = $"value-{id}";

            var lookup = await Resolver().ResolveAsync(Tenant, Caller, ids, CancellationToken.None);

            lookup.Values.Should().HaveCount(120);
            _service.BatchCalls.Should().Be(3);
            _service.LargestBatch.Should().BeLessThanOrEqualTo(50);
        }

        [Fact]
        public async Task Nothing_to_resolve_touches_nothing()
        {
            var lookup = await Resolver().ResolveAsync(Tenant, Caller, [], CancellationToken.None);

            lookup.Values.Should().BeEmpty();
            _service.BatchCalls.Should().Be(0);
            _tenants.Lookups.Should().Be(0);
        }

        [Fact]
        public void The_runner_wires_the_resolver_and_the_secret_store()
        {
            var services = new ServiceCollection();
            services.AddFunctionRunnerServices(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

            services.Should().Contain(d => d.ServiceType == typeof(IRunSecretResolver)
                && d.ImplementationType == typeof(BlocksSecretsRunResolver) && d.Lifetime == ServiceLifetime.Singleton);
            services.Should().Contain(d => d.ServiceType == typeof(ISecretService) && d.Lifetime == ServiceLifetime.Scoped,
                "the resolver opens a scope per lookup, so the SDK's scoped services are never captured by a singleton");
        }

        // ---------- doubles ----------

        private sealed class FakeTenants : ITenants
        {
            public bool Known { get; set; } = true;

            public Exception? Throw { get; set; }

            public int Lookups { get; private set; }

            public Tenant? GetTenantByID(string tenantId)
            {
                Lookups++;
                if (Throw is not null) throw Throw;
                return Known
                    ? new Tenant
                    {
                        TenantId = tenantId,
                        DbConnectionString = "mongodb://unused",
                        JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "", IssueDate = DateTime.UtcNow },
                        Applications = [new Applications { Domain = "app.example.com" }],
                    }
                    : null;
            }

            public Tenant? GetTenantByApplicationDomain(string appName) => throw new NotSupportedException();

            public Dictionary<string, (string, string)> GetTenantDatabaseConnectionStrings() => throw new NotSupportedException();

            public (string?, string?) GetTenantDatabaseConnectionString(string tenantId) => throw new NotSupportedException();

            public JwtTokenParameters? GetTenantTokenValidationParameter(string tenantId) => throw new NotSupportedException();

            public Task UpdateTenantVersionAsync(TenantCacheUpdateMessage cacheUpdate) => throw new NotSupportedException();
        }

        /// <summary>Behaves like the SDK where it matters: a batch throws on its first unreadable id.</summary>
        private sealed class FakeSecretService : ISecretService
        {
            public Dictionary<string, string> Values { get; } = [];

            public Dictionary<string, Exception> Failures { get; } = [];

            public Exception? BatchThrows { get; set; }

            public TimeSpan? Delay { get; set; }

            public int BatchCalls { get; private set; }

            public int SingleCalls { get; private set; }

            public int LargestBatch { get; private set; }

            public BlocksContext? ContextSeen { get; private set; }

            public async Task<IReadOnlyDictionary<string, string>> GetValuesAsync(
                IReadOnlyCollection<string> secretIds, CancellationToken cancellationToken = default)
            {
                BatchCalls++;
                LargestBatch = Math.Max(LargestBatch, secretIds.Count);
                ContextSeen = BlocksContext.GetContext();
                if (Delay is { } delay) await Task.Delay(delay, cancellationToken);
                if (BatchThrows is not null) throw BatchThrows;

                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var id in secretIds)
                {
                    if (Failures.TryGetValue(id, out var failure)) throw failure;
                    result[id] = Values.TryGetValue(id, out var v) ? v : throw new SecretNotFoundException(id);
                }
                return result;
            }

            public Task<string> GetValueAsync(string secretId, CancellationToken cancellationToken = default)
            {
                SingleCalls++;
                if (Failures.TryGetValue(secretId, out var failure)) throw failure;
                return Values.TryGetValue(secretId, out var v)
                    ? Task.FromResult(v)
                    : throw new SecretNotFoundException(secretId);
            }

            public Task<string> SetAsync(SetSecretRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task<IReadOnlyDictionary<string, string>> SetManyAsync(IReadOnlyCollection<SetSecretRequest> requests, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task<SecretResult?> GetAsync(string secretId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task<SecretListResult> FindAsync(SecretFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task<IReadOnlyList<SecretTagEntry>> GetTagsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task UpdateAsync(string secretId, UpdateSecretRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task RotateAsync(string secretId, RotateSecretRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task LockAsync(string secretId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task UnlockAsync(string secretId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task DeleteAsync(string secretId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task RestoreAsync(string secretId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task UpdateAccessAsync(string secretId, SecretAccess access, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task<SecretAuditListResult> GetAuditLogsAsync(SecretAuditFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        }
    }
}
