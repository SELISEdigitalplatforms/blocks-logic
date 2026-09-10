using Blocks.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Proxy.DomainService.Services;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// Covers <see cref="ProxyVariableResolver"/>: name &rarr; id lookup + cache, one batched id &rarr; value
    /// read, de-duplication, the empty-input short-circuit, and the failure contract (an unknown name or any
    /// <see cref="SecretException"/> surfaces as <see cref="ProxyVariableResolutionException"/> carrying the
    /// offending names, never a value).
    /// </summary>
    public class ProxyVariableResolverTests
    {
        private const string Tenant = "tenant-1";

        private readonly FakeSecretService _secrets = new();
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        private readonly ProxyVariableResolver _resolver;

        public ProxyVariableResolverTests()
        {
            _resolver = new ProxyVariableResolver(
                _secrets,
                _cache,
                Options.Create(new ProxyVariableResolverOptions()),
                Mock.Of<ILogger<ProxyVariableResolver>>());
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
        public async Task ResolveAsync_SecondCall_UsesCachedIdAndValue()
        {
            _secrets.Add("cached", "id-1", "v1");

            await _resolver.ResolveAsync(new[] { "cached" }, Tenant);
            await _resolver.ResolveAsync(new[] { "cached" }, Tenant);

            _secrets.FindCalls.Should().Be(1);      // name -> id cached (5 min TTL)
            _secrets.GetValuesCalls.Should().Be(1); // id -> value cached (60 s TTL)
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
                var data = _nameToId
                    .Where(kv => kv.Key == filter.Search)
                    .Select(kv => new SecretResult { SecretId = kv.Value, Name = kv.Key, Type = SecretTypes.Service })
                    .ToList();
                return Task.FromResult(new SecretListResult { Data = data, TotalCount = data.Count });
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
