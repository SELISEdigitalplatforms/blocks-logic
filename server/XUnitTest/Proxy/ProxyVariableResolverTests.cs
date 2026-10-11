using Blocks.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Proxy.DomainService.Services;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// Covers <see cref="ProxyVariableResolver"/>: name &rarr; id lookup + cache, one uncached batched id &rarr; value
    /// read, de-duplication, the empty-input short-circuit, and the failure contract (an unknown name or any
    /// <see cref="SecretException"/> surfaces as <see cref="ProxyVariableResolutionException"/> carrying the
    /// offending names, never a value).
    /// </summary>
    public class ProxyVariableResolverTests
    {
        private const string Tenant = "tenant-1";

        private readonly FakeSecretService _secrets = new();
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        private readonly ServiceProvider _services;
        private readonly ProxyVariableResolver _resolver;

        public ProxyVariableResolverTests()
        {
            // ProxyVariableResolver takes IServiceScopeFactory (it's a singleton resolving the scoped
            // ISecretService per call) — a tiny DI container stands in for the app's, same as production
            // wires ISecretService. Held in a field so the provider outlives the constructor.
            _services = new ServiceCollection()
                .AddSingleton<ISecretService>(_secrets)
                .BuildServiceProvider();

            _resolver = new ProxyVariableResolver(
                _services.GetRequiredService<IServiceScopeFactory>(),
                _cache,
                Options.Create(new ProxyVariableResolverOptions()),
                Mock.Of<ILogger<ProxyVariableResolver>>());
        }

        // ---------- PX-9: exact lookup across pages; stored ids skip the search ----------

        [Fact]
        public async Task ResolveAsync_ExactNameBehindFiftyLookalikes_IsStillFound()
        {
            _secrets.Add("KEY", "id-real", "the-value");
            for (var i = 0; i < 60; i++) _secrets.Add($"KEY_{i}", $"id-{i}", "other"); // newer, so they fill page 1

            var result = await _resolver.ResolveAsync(new[] { "KEY" }, Tenant);

            result["KEY"].Should().Be("the-value");
            _secrets.FindCalls.Should().Be(2, "the exact match is on page 2");
        }

        [Fact]
        public async Task ResolveAsync_WithStoredIds_ReadsByIdOnce_AndNeverSearches()
        {
            _secrets.Add("api-token", "id-1", "sk_live_1");

            var result = await _resolver.ResolveAsync(
                new[] { "api-token" }, Tenant, new Dictionary<string, string> { ["api-token"] = "id-1" });

            result["api-token"].Should().Be("sk_live_1");
            _secrets.FindCalls.Should().Be(0);
            _secrets.GetValuesCalls.Should().Be(1);
        }

        [Fact]
        public async Task ResolveAsync_NameMissingFromStoredIds_FallsBackToTheLookup()
        {
            _secrets.Add("a", "id-a", "va");
            _secrets.Add("b", "id-b", "vb");

            var result = await _resolver.ResolveAsync(
                new[] { "a", "b" }, Tenant, new Dictionary<string, string> { ["a"] = "id-a" });

            result.Should().HaveCount(2);
            _secrets.FindCalls.Should().Be(1, "only 'b' had no stored id");
        }

        [Fact]
        public async Task LookupIdsAsync_ReturnsExactIds_AndLeavesOutUnknownNames()
        {
            _secrets.Add("token", "id-t", "v");
            _secrets.Add("token-old", "id-o", "v2");

            var ids = await _resolver.LookupIdsAsync(new[] { "token", "nope" }, Tenant);

            ids.Should().Equal(new Dictionary<string, string> { ["token"] = "id-t" });
        }

        [Fact]
        public async Task ResolveAsync_EmptyNames_ShortCircuits_NoSecretServiceCall()
        {
            var result = await _resolver.ResolveAsync(Array.Empty<string>(), Tenant);

            result.Should().BeEmpty();
            _secrets.FindCalls.Should().Be(0);
            _secrets.GetValuesCalls.Should().Be(0);
        }

        [Fact]
        public async Task ResolveAsync_ResolvesNames_ToTheirKeyVaultValues()
        {
            _secrets.Add("api-token", "id-1", "sk_live_1");
            _secrets.Add("account", "id-2", "acct_9");

            var result = await _resolver.ResolveAsync(new[] { "api-token", "account" }, Tenant);

            result["api-token"].Should().Be("sk_live_1");
            result["account"].Should().Be("acct_9");
            _secrets.GetValuesCalls.Should().Be(1); // one batched value read for both ids
        }

        [Fact]
        public async Task ResolveAsync_DeduplicatesNames_BeforeLookup()
        {
            _secrets.Add("dup", "id-1", "v");

            var result = await _resolver.ResolveAsync(new[] { "dup", "dup", "dup" }, Tenant);

            result.Should().ContainKey("dup").WhoseValue.Should().Be("v");
            _secrets.FindCalls.Should().Be(1);
            _secrets.GetValuesCalls.Should().Be(1);
        }

        [Fact]
        public async Task ResolveAsync_SecondCall_CachesIdOnly_ReadsValueAgain()
        {
            _secrets.Add("cached", "id-1", "v1");

            await _resolver.ResolveAsync(new[] { "cached" }, Tenant);
            await _resolver.ResolveAsync(new[] { "cached" }, Tenant);

            _secrets.FindCalls.Should().Be(1);      // name -> id cached (an id is not a secret)
            _secrets.GetValuesCalls.Should().Be(2); // value never cached: every call reads the vault
        }

        [Fact]
        public async Task ResolveAsync_AfterASuccessfulRead_ADeniedCallerGetsNothing()
        {
            // PX-1: a value one caller was allowed to read must never serve the next caller.
            _secrets.Add("api-key", "id-1", "sk_live_1");
            (await _resolver.ResolveAsync(new[] { "api-key" }, Tenant))["api-key"].Should().Be("sk_live_1");

            _secrets.FailValueReadFor("id-1", new SecretAccessDeniedException("denied", "no access"));
            var act = () => _resolver.ResolveAsync(new[] { "api-key" }, Tenant);

            (await act.Should().ThrowAsync<ProxyVariableResolutionException>())
                .Which.Names.Should().Equal("api-key");
        }

        [Fact]
        public async Task ResolveAsync_RotatedValue_IsSeenOnTheNextCall()
        {
            _secrets.Add("k", "id-1", "old");
            await _resolver.ResolveAsync(new[] { "k" }, Tenant);

            _secrets.Add("k", "id-1", "new");

            (await _resolver.ResolveAsync(new[] { "k" }, Tenant))["k"].Should().Be("new");
        }

        [Fact]
        public async Task ResolveAsync_UnknownName_ThrowsWithThatName()
        {
            _secrets.Add("known", "id-1", "v");

            var act = () => _resolver.ResolveAsync(new[] { "known", "ghost" }, Tenant);

            (await act.Should().ThrowAsync<ProxyVariableResolutionException>())
                .Which.Names.Should().Contain("ghost").And.NotContain("known");
        }

        [Fact]
        public async Task ResolveAsync_AccessDenied_IsAResolutionFailure()
        {
            _secrets.Add("locked", "id-1", "v");
            _secrets.FailValueReadFor("id-1", new SecretAccessDeniedException("denied", "no access"));

            var act = () => _resolver.ResolveAsync(new[] { "locked" }, Tenant);

            (await act.Should().ThrowAsync<ProxyVariableResolutionException>())
                .Which.Names.Should().Equal("locked");
        }

        [Fact]
        public async Task ResolveAsync_VaultUnreachable_IsAResolutionFailure()
        {
            _secrets.Add("v", "id-1", "x");
            _secrets.FailValueReadFor("id-1", new SecretVaultException("kv down", "get", "id-1", null));

            var act = () => _resolver.ResolveAsync(new[] { "v" }, Tenant);

            await act.Should().ThrowAsync<ProxyVariableResolutionException>();
        }

        // --------------------------------------------------------------------

        private sealed class FakeSecretService : ISecretService
        {
            private readonly Dictionary<string, string> _nameToId = new(StringComparer.Ordinal);
            private readonly Dictionary<string, string> _idToValue = new(StringComparer.Ordinal);
            private readonly Dictionary<string, Exception> _valueReadFailures = new(StringComparer.Ordinal);

            public int FindCalls { get; private set; }

            public int GetValuesCalls { get; private set; }

            public void Add(string name, string id, string value)
            {
                _nameToId[name] = id;
                _idToValue[id] = value;
            }

            public void FailValueReadFor(string id, Exception ex) => _valueReadFailures[id] = ex;

            public Task<SecretListResult> FindAsync(SecretFilter filter, CancellationToken cancellationToken = default)
            {
                FindCalls++;
                // Like the real store: "name contains, any case", newest first, one page at a time.
                var all = _nameToId
                    .Where(kv => kv.Key.Contains(filter.Search ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    .Reverse()
                    .Select(kv => new SecretResult { SecretId = kv.Value, Name = kv.Key, Type = SecretTypes.Service })
                    .ToList();
                var size = filter.PageSize <= 0 ? 50 : filter.PageSize;
                var page = all.Skip((Math.Max(1, filter.PageNumber) - 1) * size).Take(size).ToList();
                return Task.FromResult(new SecretListResult { Data = page, TotalCount = all.Count });
            }

            public Task<IReadOnlyDictionary<string, string>> GetValuesAsync(
                IReadOnlyCollection<string> secretIds, CancellationToken cancellationToken = default)
            {
                GetValuesCalls++;
                foreach (var id in secretIds)
                {
                    if (_valueReadFailures.TryGetValue(id, out var ex))
                    {
                        throw ex;
                    }
                }

                var map = secretIds
                    .Where(_idToValue.ContainsKey)
                    .ToDictionary(id => id, id => _idToValue[id], StringComparer.Ordinal);
                return Task.FromResult<IReadOnlyDictionary<string, string>>(map);
            }

            // ---- unused by the resolver ----
            public Task<string> SetAsync(SetSecretRequest request, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task<IReadOnlyDictionary<string, string>> SetManyAsync(
                IReadOnlyCollection<SetSecretRequest> requests, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task<SecretResult> GetAsync(string secretId, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task<IReadOnlyList<SecretTagEntry>> GetTagsAsync(CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task<string> GetValueAsync(string secretId, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task UpdateAsync(string secretId, UpdateSecretRequest request, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task RotateAsync(string secretId, RotateSecretRequest request, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task LockAsync(string secretId, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task UnlockAsync(string secretId, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task DeleteAsync(string secretId, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task RestoreAsync(string secretId, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task UpdateAccessAsync(string secretId, SecretAccess access, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task<SecretAuditListResult> GetAuditLogsAsync(SecretAuditFilter filter, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();
        }
    }
}
