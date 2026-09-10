using Blocks.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Proxy.DomainService.Services;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// Covers <see cref="ProxyVariableCatalog"/>: the read-only list the console's <c>{{$VAR.name}}</c>
    /// picker calls (<c>GET /api/Proxy/Variables</c>). Maps a Blocks Secrets <c>SecretResult</c> to the
    /// trimmed picker row, passes the search term through, never surfaces a value, and degrades a
    /// <see cref="SecretException"/> to an empty list rather than throwing.
    /// </summary>
    public class ProxyVariableCatalogTests
    {
        private readonly FakeSecretService _secrets = new();
        private readonly ProxyVariableCatalog _catalog;

        public ProxyVariableCatalogTests()
        {
            _catalog = new ProxyVariableCatalog(_secrets, Mock.Of<ILogger<ProxyVariableCatalog>>());
        }

        [Fact]
        public async Task ListAsync_MapsSecretRows_ToPickerRows()
        {
            _secrets.Rows.Add(new SecretResult
            {
                SecretId = "id-1", Name = "stripe-api-key", Type = SecretTypes.Service, Tags = new[] { "payments" },
            });
            _secrets.Rows.Add(new SecretResult
            {
                SecretId = "id-2", Name = "sendgrid-api-key", Type = SecretTypes.Both, Tags = System.Array.Empty<string>(),
            });

            var result = await _catalog.ListAsync(null);

            result.Data.Should().HaveCount(2);
            result.TotalCount.Should().Be(2);
            result.Data[0].SecretId.Should().Be("id-1");
            result.Data[0].Name.Should().Be("stripe-api-key");
            result.Data[0].Type.Should().Be("service");
            result.Data[0].Tags.Should().Equal("payments");
        }

        [Fact]
        public async Task ListAsync_PassesTrimmedSearch_ToTheFilter()
        {
            await _catalog.ListAsync("  stripe  ");

            _secrets.LastFilter!.Search.Should().Be("stripe");
        }

        [Fact]
        public async Task ListAsync_BlankSearch_BecomesNullFilter()
        {
            await _catalog.ListAsync("   ");

            _secrets.LastFilter!.Search.Should().BeNull();
        }

        [Fact]
        public async Task ListAsync_SecretException_DegradesToEmptyList()
        {
            _secrets.Throw = new SecretVaultException("kv down", "find", null, null);

            var result = await _catalog.ListAsync(null);

            result.Data.Should().BeEmpty();
            result.TotalCount.Should().Be(0);
        }

        // --------------------------------------------------------------------

        private sealed class FakeSecretService : ISecretService
        {
            public List<SecretResult> Rows { get; } = new();

            public SecretFilter? LastFilter { get; private set; }

            public Exception? Throw { get; set; }

            public Task<SecretListResult> FindAsync(SecretFilter filter, CancellationToken cancellationToken = default)
            {
                LastFilter = filter;
                if (Throw is not null)
                {
                    throw Throw;
                }

                return Task.FromResult(new SecretListResult { Data = Rows, TotalCount = Rows.Count });
            }

            public Task<IReadOnlyList<SecretTagEntry>> GetTagsAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<SecretTagEntry>>(new List<SecretTagEntry>());

            // ---- unused by the catalog ----
            public Task<string> SetAsync(SetSecretRequest request, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task<IReadOnlyDictionary<string, string>> SetManyAsync(
                IReadOnlyCollection<SetSecretRequest> requests, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task<SecretResult> GetAsync(string secretId, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task<string> GetValueAsync(string secretId, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task<IReadOnlyDictionary<string, string>> GetValuesAsync(
                IReadOnlyCollection<string> secretIds, CancellationToken cancellationToken = default) =>
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
