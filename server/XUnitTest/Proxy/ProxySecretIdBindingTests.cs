using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Repositories;
using Proxy.DomainService.Services;
using XUnitTest.TestHelpers;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// PX-9 (2026-10-07): the token keeps the secret's name, the proxy row keeps name + id (bound once on save),
    /// and a call reads by id — no search per call. Unknown names are refused on save.
    /// </summary>
    public class ProxySecretIdBindingTests : IDisposable
    {
        private const string Tenant = "t1";
        private readonly Mock<IProxyRepository> _proxies = new();
        private readonly Mock<IProxyVersionRepository> _versions = new();
        private readonly Mock<IProxyVariableResolver> _resolver = new();
        private readonly ProxyService _service;

        public ProxySecretIdBindingTests()
        {
            TestBlocksContext.Set(Tenant, "user-1");
            _proxies.Setup(r => r.SaveConfigAsync(It.IsAny<ProxyDetailEntity>(), It.IsAny<int>())).ReturnsAsync(true);
            _service = new ProxyService(
                _proxies.Object, _versions.Object, Mock.Of<IProxyExecutionRepository>(),
                TimeProvider.System, Mock.Of<ILogger<ProxyService>>(), _resolver.Object);
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        private void IdsAre(Dictionary<string, string> ids) =>
            _resolver.Setup(r => r.LookupIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), Tenant, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ids);

        private static ProxyCreateRequestDto Create(string headerValue) => new()
        {
            Name = "OpenAI",
            Upstream = "https://api.openai.com/v1/chat",
            Methods = new List<string> { "POST" },
            Headers = new List<ProxyKeyValueInputDto> { new() { Key = "Authorization", Value = headerValue } },
            Routes = new List<ProxyRouteConfigInputDto>
            {
                new()
                {
                    Method = "POST", Path = "",
                    Query = new List<ProxyKeyValueInputDto> { new() { Key = "org", Value = "{{$VAR.org-id}}" } },
                },
            },
            Enabled = true,
        };

        [Fact]
        public async Task Create_StoresTheIdOfEveryVariable_IncludingInsideRoutes()
        {
            IdsAre(new() { ["openai-key"] = "sec-1", ["org-id"] = "sec-2" });
            ProxyDetailEntity? inserted = null;
            _proxies.Setup(r => r.InsertAsync(It.IsAny<ProxyDetailEntity>()))
                .Callback<ProxyDetailEntity>(p => inserted = p).Returns(Task.CompletedTask);

            var result = await _service.CreateAsync(Tenant, Create("Bearer {{$VAR.openai-key}}"));

            result.HttpStatus.Should().Be(201);
            inserted!.SecretIds.Should().Equal(new Dictionary<string, string> { ["openai-key"] = "sec-1", ["org-id"] = "sec-2" });
        }

        [Fact]
        public async Task Create_WithAnUnknownSecretName_IsRefusedAndNamesIt()
        {
            IdsAre(new() { ["org-id"] = "sec-2" });

            var result = await _service.CreateAsync(Tenant, Create("Bearer {{$VAR.typo-key}}"));

            result.HttpStatus.Should().Be(400);
            result.Errors!["variables"].Should().Contain("typo-key");
            _proxies.Verify(r => r.InsertAsync(It.IsAny<ProxyDetailEntity>()), Times.Never);
        }

        [Fact]
        public async Task Create_WhenTheSecretStoreIsDown_StillSaves_AndCallsFallBackToTheName()
        {
            _resolver.Setup(r => r.LookupIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), Tenant, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("vault"));
            ProxyDetailEntity? inserted = null;
            _proxies.Setup(r => r.InsertAsync(It.IsAny<ProxyDetailEntity>()))
                .Callback<ProxyDetailEntity>(p => inserted = p).Returns(Task.CompletedTask);

            var result = await _service.CreateAsync(Tenant, Create("Bearer {{$VAR.openai-key}}"));

            result.HttpStatus.Should().Be(201);
            inserted!.SecretIds.Should().BeEmpty();
        }

        [Fact]
        public async Task Update_WithNoConfigChange_StillFillsTheIdsOfAnOldProxy_WithoutAVersion()
        {
            var existing = new ProxyDetailEntity
            {
                ItemId = "p1", TenantId = Tenant, Name = "OpenAI", Slug = "openai",
                Upstream = "https://api.openai.com/v1/chat",
                Methods = new List<HttpMethodType> { HttpMethodType.Post },
                Headers = new List<ProxyKeyValue> { new() { Key = "Authorization", Value = "Bearer {{$VAR.openai-key}}" } },
                CurrentVersion = 3,
            };
            _proxies.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(existing);
            IdsAre(new() { ["openai-key"] = "sec-1" });

            var result = await _service.UpdateAsync(Tenant, new ProxyUpdateRequestDto
            {
                ItemId = "p1",
                Name = "OpenAI",
                Upstream = "https://api.openai.com/v1/chat",
                Methods = new List<string> { "POST" },
                Headers = new List<ProxyKeyValueInputDto> { new() { Key = "Authorization", Value = "Bearer {{$VAR.openai-key}}" } },
            });

            result.HttpStatus.Should().Be(200);
            existing.SecretIds.Should().ContainKey("openai-key").WhoseValue.Should().Be("sec-1");
            existing.CurrentVersion.Should().Be(3);
            _proxies.Verify(r => r.SaveConfigAsync(existing, It.IsAny<int>()), Times.Once);
            _versions.Verify(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>()), Times.Never);
        }
    }
}
