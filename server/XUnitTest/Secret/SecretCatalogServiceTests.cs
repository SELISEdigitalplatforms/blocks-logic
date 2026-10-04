using Blocks.Secrets;
using Common.InternalService.Secret;
using Common.InternalService.Secret.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Secret
{
    /// <summary>
    /// Covers <see cref="SecretCatalogService"/>: the shared read-only list behind
    /// <c>GET /api/Secret/GetAll</c>. Maps a Blocks Secrets <c>SecretResult</c> to the trimmed picker row
    /// (<c>id</c> / <c>name</c> / <c>tags</c> only), narrows to the platform types, passes the name / tag /
    /// search filters through, never surfaces a value or a type, and degrades a <see cref="SecretException"/>
    /// to an empty list rather than throwing.
    /// </summary>
    public class SecretCatalogServiceTests
    {
        private readonly FakeSecretService _secrets = new();
        private readonly SecretCatalogService _catalog;

        public SecretCatalogServiceTests()
        {
            _catalog = new SecretCatalogService(_secrets, Mock.Of<ILogger<SecretCatalogService>>());
        }

        [Fact]
        public async Task GetAllAsync_MapsSecretRows_ToIdNameTagsOnly()
        {
            _secrets.Rows.Add(new SecretResult
            {
                SecretId = "id-1", Name = "stripe-api-key", Type = SecretTypes.Service, Tags = new[] { "payments" },
            });
            _secrets.Rows.Add(new SecretResult
            {
                SecretId = "id-2", Name = "sendgrid-api-key", Type = SecretTypes.Both, Tags = Array.Empty<string>(),
            });

            var result = await _catalog.GetAllAsync(new GetSecretsRequest());

            result.Data.Should().HaveCount(2);
            result.TotalCount.Should().Be(2);
            result.Data[0].Id.Should().Be("id-1");
            result.Data[0].Name.Should().Be("stripe-api-key");
            result.Data[0].Tags.Should().Equal("payments");
            result.Data[1].Tags.Should().BeEmpty();
        }

        [Fact]
        public async Task GetAllAsync_DropsNonPlatformTypes()
        {
            _secrets.Rows.Add(new SecretResult { SecretId = "id-1", Name = "svc", Type = SecretTypes.Service });
            _secrets.Rows.Add(new SecretResult { SecretId = "id-2", Name = "both", Type = SecretTypes.Both });
            _secrets.Rows.Add(new SecretResult { SecretId = "id-3", Name = "api-only", Type = SecretTypes.Api });

            var result = await _catalog.GetAllAsync(new GetSecretsRequest());

            result.Data.Select(r => r.Id).Should().Equal("id-1", "id-2");
            result.TotalCount.Should().Be(2);
        }

        [Fact]
        public async Task GetAllAsync_PassesTrimmedSearchAndTag_ToTheFilter()
        {
            await _catalog.GetAllAsync(new GetSecretsRequest { Search = "  stripe  ", Tag = "  payments  " });

            _secrets.LastFilter!.Search.Should().Be("stripe");
            _secrets.LastFilter.Tags.Should().Equal("payments");
        }

        [Fact]
        public async Task GetAllAsync_BlankFilters_BecomeNull()
        {
            await _catalog.GetAllAsync(new GetSecretsRequest { Search = "   ", Tag = "  " });

            _secrets.LastFilter!.Search.Should().BeNull();
            _secrets.LastFilter.Tags.Should().BeNull();
        }

        [Fact]
        public async Task GetAllAsync_Name_MatchesExactlyAndIgnoresCase()
        {
            _secrets.Rows.Add(new SecretResult { SecretId = "id-1", Name = "stripe-key", Type = SecretTypes.Service });
            _secrets.Rows.Add(new SecretResult { SecretId = "id-2", Name = "stripe-key-old", Type = SecretTypes.Service });

            var result = await _catalog.GetAllAsync(new GetSecretsRequest { Name = " STRIPE-KEY " });

            result.Data.Should().ContainSingle().Which.Id.Should().Be("id-1");
        }

        [Fact]
        public async Task GetAllAsync_SecretException_DegradesToEmptyList()
        {
            _secrets.Throw = new SecretVaultException("kv down", "find", null, null);

            var result = await _catalog.GetAllAsync(new GetSecretsRequest());

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

            // ---- unused by the catalog ----
            public Task<IReadOnlyList<SecretTagEntry>> GetTagsAsync(CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

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
