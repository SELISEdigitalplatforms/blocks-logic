using Blocks.Genesis;
using Blocks.Secrets;
using FluentAssertions;
using Mail.DomainService.Mails.Office365;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace XUnitTest.Mail
{
    /// <summary>
    /// Phase 2: token reuse, the refresh boundary, single-flight coalescing, cancellation and
    /// bounded eviction — all on an injectable clock, so none of it waits on wall time.
    /// </summary>
    public class Office365TokenCacheTests
    {
        private readonly TestClock _clock = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        private readonly CountingAcquirer _acquirer;
        private readonly Mock<ISecretService> _secrets = new();

        public Office365TokenCacheTests()
        {
            _acquirer = new CountingAcquirer(_clock);

            _secrets
                .Setup(s => s.GetValueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("client-secret");
        }

        private CachingOffice365TokenProvider Provider(Office365TokenCacheOptions? options = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton(_secrets.Object);

            return new CachingOffice365TokenProvider(
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                _acquirer,
                _clock,
                Options.Create(options ?? new Office365TokenCacheOptions()),
                NullLogger<CachingOffice365TokenProvider>.Instance);
        }

        private static Office365TokenRequest Request(
            string tenant = "blocks-tenant",
            string reference = "secret-1",
            string scope = Office365TokenScopes.Graph) =>
            new(tenant, "contoso-tenant", "mailer-app", reference, scope);

        /// <summary>Blocks Secrets refuses an unauthenticated or untenanted context.</summary>
        private static IDisposable TenantContext()
        {
            var previous = BlocksContext.GetContext();
            BlocksContext.SetContext(
                BlocksContext.Create("blocks-tenant", [], "user", true, "", "", DateTime.MaxValue, "", [], "", "", "", "", "", "blocks-tenant"),
                true);
            return new Restore(previous);
        }

        private sealed class Restore(BlocksContext? previous) : IDisposable
        {
            public void Dispose() => BlocksContext.SetContext(previous!, previous is not null);
        }

        // ---------- H1 ----------

        [Fact]
        public async Task SecondSend_ReusesTheTokenWithoutReadingTheSecretAgain()
        {
            using var _ = TenantContext();
            var provider = Provider();

            var first = await provider.GetTokenAsync(Request());
            var second = await provider.GetTokenAsync(Request());

            second.Should().Be(first);
            _acquirer.Calls.Should().Be(1);
            _secrets.Verify(s => s.GetValueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        // ---------- H2 ----------

        [Fact]
        public async Task AtTheRefreshBoundary_TheTokenIsNotReused()
        {
            using var _ = TenantContext();
            var provider = Provider();

            // Expires at 13:00; reusable only before 12:55.
            _acquirer.Lifetime = TimeSpan.FromHours(1);
            await provider.GetTokenAsync(Request());

            _clock.Advance(TimeSpan.FromMinutes(54));
            await provider.GetTokenAsync(Request());
            _acquirer.Calls.Should().Be(1, "still inside the window");

            _clock.Advance(TimeSpan.FromMinutes(2));
            await provider.GetTokenAsync(Request());
            _acquirer.Calls.Should().Be(2, "past the boundary, so one refresh");
        }

        [Fact]
        public async Task ARefreshResolvesTheSecretAgain_SoARotationTakesEffect()
        {
            using var _ = TenantContext();
            _acquirer.Lifetime = TimeSpan.FromMinutes(10);
            var provider = Provider();

            await provider.GetTokenAsync(Request());

            _secrets
                .Setup(s => s.GetValueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("rotated-secret");

            _clock.Advance(TimeSpan.FromMinutes(6));
            await provider.GetTokenAsync(Request());

            _acquirer.LastSecret.Should().Be("rotated-secret");
        }

        // ---------- H3 ----------

        [Fact]
        public async Task ConcurrentRequestsForOneKey_ShareASingleAcquisition()
        {
            using var _ = TenantContext();
            _acquirer.Gate = new TaskCompletionSource();
            var provider = Provider();

            var callers = Enumerable.Range(0, 20).Select(_ => provider.GetTokenAsync(Request())).ToArray();

            _acquirer.Started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            _acquirer.Gate.SetResult();

            var tokens = await Task.WhenAll(callers);

            _acquirer.Calls.Should().Be(1);
            tokens.Should().OnlyContain(t => t == tokens[0]);
        }

        [Fact]
        public async Task DifferentKeys_DoNotBlockOneAnother()
        {
            using var _ = TenantContext();
            var provider = Provider();

            await provider.GetTokenAsync(Request(tenant: "tenant-a"));
            await provider.GetTokenAsync(Request(tenant: "tenant-b"));
            await provider.GetTokenAsync(Request(reference: "secret-2"));

            _acquirer.Calls.Should().Be(3, "each key is its own credential and its own token");
        }

        [Fact]
        public async Task OneApplication_HoldsAGraphTokenAndAnExchangeTokenSeparately()
        {
            using var _ = TenantContext();
            var provider = Provider();

            // The sender and the IMAP poller share one application and one cache. Either token
            // handed to the other resource is refused, so they must never collide on a key.
            var graph = await provider.GetTokenAsync(Request(scope: Office365TokenScopes.Graph));
            var exchange = await provider.GetTokenAsync(Request(scope: Office365TokenScopes.ExchangeOnline));

            _acquirer.Calls.Should().Be(2);
            exchange.Should().NotBe(graph);
            _acquirer.Scopes.Should().Equal(Office365TokenScopes.Graph, Office365TokenScopes.ExchangeOnline);

            (await provider.GetTokenAsync(Request(scope: Office365TokenScopes.Graph))).Should().Be(graph);
            _acquirer.Calls.Should().Be(2, "each scope's token is reused on its own key");
        }

        // ---------- C1 ----------

        [Fact]
        public async Task AFailedAcquisition_IsNotCached_AndALaterSendCanTryAgain()
        {
            using var _ = TenantContext();
            var provider = Provider();
            _acquirer.Throw = new InvalidOperationException("AADSTS7000215");

            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetTokenAsync(Request()));

            _acquirer.Throw = null;
            var token = await provider.GetTokenAsync(Request());

            token.Should().NotBeNullOrEmpty();
            _acquirer.Calls.Should().Be(2, "the faulted in-flight entry was removed rather than joined");
        }

        [Fact]
        public async Task OneWaiterCancelling_DoesNotCancelTheOthers()
        {
            using var _ = TenantContext();
            _acquirer.Gate = new TaskCompletionSource();
            var provider = Provider();

            using var quitter = new CancellationTokenSource();
            var abandoned = provider.GetTokenAsync(Request(), quitter.Token);
            var patient = provider.GetTokenAsync(Request());

            _acquirer.Started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            await quitter.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);

            _acquirer.Gate.SetResult();
            (await patient).Should().NotBeNullOrEmpty("the shared acquisition belonged to everyone waiting on it");
        }

        [Fact]
        public async Task AVaultFailure_Propagates_SoTheSenderCanClassifyIt()
        {
            using var _ = TenantContext();
            _secrets
                .Setup(s => s.GetValueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SecretVaultException("down", "Get", "secret-1"));

            await Assert.ThrowsAsync<SecretVaultException>(() => Provider().GetTokenAsync(Request()));
            _acquirer.Calls.Should().Be(0, "the secret is resolved immediately before acquisition, so no token was requested");
        }

        // ---------- H6 ----------

        [Fact]
        public async Task CompletedEntriesAreBounded_AndTheLeastRecentlyUsedGoFirst()
        {
            using var _ = TenantContext();
            var provider = Provider(new Office365TokenCacheOptions { MaxEntries = 2 });

            await provider.GetTokenAsync(Request(tenant: "tenant-a"));
            _clock.Advance(TimeSpan.FromSeconds(1));
            await provider.GetTokenAsync(Request(tenant: "tenant-b"));
            _clock.Advance(TimeSpan.FromSeconds(1));

            // Touching A makes B the least recently used.
            await provider.GetTokenAsync(Request(tenant: "tenant-a"));
            _clock.Advance(TimeSpan.FromSeconds(1));

            await provider.GetTokenAsync(Request(tenant: "tenant-c"));

            provider.CachedEntryCount.Should().Be(2);

            var before = _acquirer.Calls;
            await provider.GetTokenAsync(Request(tenant: "tenant-a"));
            _acquirer.Calls.Should().Be(before, "A was used most recently, so it survived");
        }

        private sealed class TestClock(DateTimeOffset start) : ISystemClock
        {
            public DateTimeOffset UtcNow { get; private set; } = start;

            public void Advance(TimeSpan by) => UtcNow += by;
        }

        private sealed class CountingAcquirer(ISystemClock clock) : IOffice365TokenAcquirer
        {
            private int _calls;

            public int Calls => Volatile.Read(ref _calls);
            public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(1);
            public Exception? Throw { get; set; }
            public TaskCompletionSource? Gate { get; set; }
            public ManualResetEventSlim Started { get; } = new(false);
            public string? LastSecret { get; private set; }
            public System.Collections.Concurrent.ConcurrentQueue<string> Scopes { get; } = new();

            public async Task<Office365AccessToken> AcquireAsync(
                Office365TokenRequest request,
                string clientSecret,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref _calls);
                LastSecret = clientSecret;
                Scopes.Enqueue(request.Scope);
                Started.Set();

                if (Gate is not null)
                {
                    await Gate.Task.ConfigureAwait(false);
                }

                if (Throw is not null)
                {
                    throw Throw;
                }

                // Expiry on the test clock, so advancing it actually crosses the refresh boundary.
                return new Office365AccessToken($"token-{Calls}", clock.UtcNow + Lifetime);
            }
        }
    }
}
