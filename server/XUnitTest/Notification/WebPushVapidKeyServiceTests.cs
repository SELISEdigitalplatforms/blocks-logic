using System.Text.Json;
using Blocks.Secrets;
using DomainService.Notification;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebPush;

namespace XUnitTest.Notification
{
    public class WebPushVapidKeyServiceTests
    {
        private readonly FakeSecretService _secrets = new();
        private readonly WebPushVapidKeyService _sut;

        public WebPushVapidKeyServiceTests()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ISecretService>(_secrets);
            var provider = services.BuildServiceProvider();
            var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            _sut = new WebPushVapidKeyService(scopeFactory, NullLogger<WebPushVapidKeyService>.Instance);
        }

        [Fact]
        public async Task GetOrCreatePublicKey_GeneratesOnce_H2_H3()
        {
            var first = await _sut.GetOrCreatePublicKeyAsync();
            var second = await _sut.GetOrCreatePublicKeyAsync();

            first.Should().NotBeNullOrWhiteSpace();
            second.Should().Be(first);
            _secrets.SetCalls.Should().Be(1);
        }

        [Fact]
        public async Task Rotate_ReplacesKeypair_H6()
        {
            var before = await _sut.GetOrCreatePublicKeyAsync();
            await _sut.RotateAsync();
            var after = await _sut.GetOrCreatePublicKeyAsync();

            after.Should().NotBe(before);
            _secrets.RotateCalls.Should().Be(1);
        }

        [Fact]
        public async Task GetVapidDetails_ReturnsStoredPrivateKey()
        {
            var publicKey = await _sut.GetOrCreatePublicKeyAsync();
            var details = await _sut.GetVapidDetailsAsync();

            details.PublicKey.Should().Be(publicKey);
            details.PrivateKey.Should().NotBeNullOrWhiteSpace();
            details.Subject.Should().Be(WebPushVapidKeyService.DefaultSubject);
        }

        private sealed class FakeSecretService : ISecretService
        {
            private readonly Dictionary<string, (SecretResult Meta, string Value)> _byId = new();
            public int SetCalls { get; private set; }
            public int RotateCalls { get; private set; }

            public Task<string> SetAsync(SetSecretRequest request, CancellationToken cancellationToken = default)
            {
                SetCalls++;
                var id = Guid.NewGuid().ToString();
                _byId[id] = (new SecretResult
                {
                    SecretId = id,
                    Name = request.Name,
                    Type = request.Type,
                }, request.Value);
                return Task.FromResult(id);
            }

            public Task<SecretListResult> FindAsync(SecretFilter filter, CancellationToken cancellationToken = default)
            {
                var data = _byId.Values
                    .Select(v => v.Meta)
                    .Where(m => string.IsNullOrEmpty(filter.Search)
                                || m.Name.Contains(filter.Search, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                return Task.FromResult(new SecretListResult { Data = data, TotalCount = data.Count });
            }

            public Task<string> GetValueAsync(string secretId, CancellationToken cancellationToken = default) =>
                Task.FromResult(_byId[secretId].Value);

            public Task RotateAsync(string secretId, RotateSecretRequest request, CancellationToken cancellationToken = default)
            {
                RotateCalls++;
                var existing = _byId[secretId];
                _byId[secretId] = (existing.Meta, request.Value);
                return Task.CompletedTask;
            }

            public Task<IReadOnlyDictionary<string, string>> SetManyAsync(IReadOnlyCollection<SetSecretRequest> requests, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<SecretResult> GetAsync(string secretId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<IReadOnlyList<SecretTagEntry>> GetTagsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<IReadOnlyDictionary<string, string>> GetValuesAsync(IReadOnlyCollection<string> secretIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task UpdateAsync(string secretId, UpdateSecretRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task LockAsync(string secretId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task UnlockAsync(string secretId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task DeleteAsync(string secretId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task RestoreAsync(string secretId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task UpdateAccessAsync(string secretId, SecretAccess access, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<SecretAuditListResult> GetAuditLogsAsync(SecretAuditFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        }
    }
}
