using Blocks.FunctionRunner.Delegation;
using Blocks.FunctionRunner.Utils;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The runner's redemption of a run's delegation grant: what the Genesis provider is asked, in
    /// which ambient context, and that every way it can go wrong ends in "no token" rather than a
    /// failed run — except the run itself being cancelled.
    /// </summary>
    public sealed class DelegatedRunAccessTokenResolverTests
    {
        private const string Tenant = "tenant_delegation_test";
        private const string Token = "eyJhbGciOi.delegated_unit.sig";
        private static readonly string Grant = "dg_" + new string('a', 64);

        private readonly CapturingLoggerProvider _logs = new();

        private DelegatedRunAccessTokenResolver Resolver(FakeProvider provider, FakeTenants? tenants = null, TimeSpan? timeout = null) =>
            new(provider, tenants ?? new FakeTenants(), _logs.For<DelegatedRunAccessTokenResolver>())
            {
                Timeout = timeout ?? TimeSpan.FromSeconds(10),
            };

        [Fact]
        public async Task Redeems_the_grant_for_the_runs_tenant()
        {
            var provider = new FakeProvider(Token);

            var token = await Resolver(provider).RedeemAsync(Tenant, Grant, CancellationToken.None);

            token.Should().Be(Token);
            provider.Seen.Should().ContainSingle();
            provider.Seen[0].Should().Be((Grant, Tenant), "the provider reads both from ambient context");
        }

        [Fact]
        public async Task The_ambient_grant_and_context_do_not_leak_back_to_the_caller()
        {
            DelegatedTokenContext.Clear();

            await Resolver(new FakeProvider(Token)).RedeemAsync(Tenant, Grant, CancellationToken.None);

            DelegatedTokenContext.Current.Should().BeNull();
            BlocksContext.GetContext()?.TenantId.Should().NotBe(Tenant);
        }

        [Fact]
        public async Task Every_attempt_redeems_afresh()
        {
            var provider = new FakeProvider(Token);

            await Resolver(provider).RedeemAsync(Tenant, Grant, CancellationToken.None);
            await Resolver(provider).RedeemAsync(Tenant, Grant, CancellationToken.None);

            provider.Invalidated.Should().Equal([Grant, Grant],
                "a cached token would skip IAM's re-check of the user on a later attempt");
        }

        [Theory]
        [InlineData("")]
        [InlineData("dg_short")]
        [InlineData("not-a-grant")]
        [InlineData("DG_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        public async Task A_malformed_grant_is_never_sent(string grant)
        {
            var provider = new FakeProvider(Token);

            (await Resolver(provider).RedeemAsync(Tenant, grant, CancellationToken.None)).Should().BeNull();

            provider.Seen.Should().BeEmpty();
        }

        [Fact]
        public async Task A_blank_tenant_redeems_nothing()
        {
            var provider = new FakeProvider(Token);

            (await Resolver(provider).RedeemAsync(" ", Grant, CancellationToken.None)).Should().BeNull();

            provider.Seen.Should().BeEmpty();
        }

        [Fact]
        public async Task An_unknown_tenant_redeems_nothing()
        {
            var provider = new FakeProvider(Token);

            (await Resolver(provider, new FakeTenants { Known = false }).RedeemAsync(Tenant, Grant, CancellationToken.None))
                .Should().BeNull();

            provider.Seen.Should().BeEmpty();
        }

        [Fact]
        public async Task A_tenant_registry_failure_means_no_token_not_a_failed_run()
        {
            var tenants = new FakeTenants { Throw = new TimeoutException("mongo") };

            (await Resolver(new FakeProvider(Token), tenants).RedeemAsync(Tenant, Grant, CancellationToken.None))
                .Should().BeNull();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task A_grant_IAM_refused_means_no_token(string? answer)
        {
            // The provider answers null for every refusal: expired, revoked, deactivated user, bad signature.
            (await Resolver(new FakeProvider(answer)).RedeemAsync(Tenant, Grant, CancellationToken.None)).Should().BeNull();
            _logs.All.Should().NotContain(Grant);
        }

        [Fact]
        public async Task A_provider_that_throws_means_no_token_and_nothing_sensitive_is_logged()
        {
            var provider = new FakeProvider(Token) { Throw = new HttpRequestException($"boom {Grant} {Token}") };

            (await Resolver(provider).RedeemAsync(Tenant, Grant, CancellationToken.None)).Should().BeNull();

            _logs.All.Should().Contain(nameof(HttpRequestException)).And.NotContain(Grant).And.NotContain(Token);
            provider.Invalidated.Should().Equal(Grant);
        }

        [Fact]
        public async Task A_slow_IAM_means_no_token_after_the_timeout()
        {
            var provider = new FakeProvider(Token) { Hang = true };

            var token = await Resolver(provider, timeout: TimeSpan.FromMilliseconds(100))
                .RedeemAsync(Tenant, Grant, CancellationToken.None);

            token.Should().BeNull();
            _logs.All.Should().Contain("did not redeem");
        }

        [Fact]
        public async Task Cancelling_the_run_itself_propagates()
        {
            using var cts = new CancellationTokenSource();
            var provider = new FakeProvider(Token) { Hang = true };

            var redeem = Resolver(provider).RedeemAsync(Tenant, Grant, cts.Token);
            await cts.CancelAsync();

            await redeem.Invoking(r => r).Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task The_token_is_never_logged()
        {
            await Resolver(new FakeProvider(Token)).RedeemAsync(Tenant, Grant, CancellationToken.None);

            _logs.All.Should().NotContain(Token).And.NotContain(Grant);
        }

        [Fact]
        public void It_is_registered_as_the_runners_resolver()
        {
            var services = new ServiceCollection();
            services.AddFunctionRunnerServices(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

            services.Should().Contain(d => d.ServiceType == typeof(IRunAccessTokenResolver)
                && d.ImplementationType == typeof(DelegatedRunAccessTokenResolver) && d.Lifetime == ServiceLifetime.Singleton);
        }

        // ---------- doubles ----------

        /// <summary>Answers like Genesis's provider, recording the ambient grant and tenant it saw.</summary>
        private sealed class FakeProvider(string? answer) : IDelegatedTokenProvider
        {
            public List<(string? Grant, string? Tenant)> Seen { get; } = [];

            public List<string> Invalidated { get; } = [];

            public Exception? Throw { get; init; }

            public bool Hang { get; init; }

            public async Task<string?> GetTokenAsync(CancellationToken ct = default)
            {
                Seen.Add((DelegatedTokenContext.Current, BlocksContext.GetContext()?.TenantId));
                if (Throw is not null) throw Throw;
                if (Hang) await Task.Delay(Timeout.Infinite, ct);
                return answer;
            }

            public Task<Dictionary<string, string>> GetAuthorizationHeadersAsync(
                Dictionary<string, string>? existingHeaders = null, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public void Invalidate(string? delegationGrantId)
            {
                if (delegationGrantId is not null) Invalidated.Add(delegationGrantId);
            }
        }

        private sealed class FakeTenants : ITenants
        {
            public bool Known { get; init; } = true;

            public Exception? Throw { get; init; }

            public Tenant? GetTenantByID(string tenantId)
            {
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
    }
}
