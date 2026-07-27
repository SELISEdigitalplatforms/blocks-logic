using Blocks.Genesis;
using DomainService.MagicLink;
using DomainService.MagicLink.Models;
using DomainService.MagicLink.Service;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Integration
{
    [Collection("Mongo integration")]
    public class MagicLinkRepositoryIntegrationTests
    {
        private readonly MongoIntegrationFixture _fixture;
        private readonly MagicLinkRepository _repo;
        private readonly string _ns = Guid.NewGuid().ToString("N");

        public MagicLinkRepositoryIntegrationTests(MongoIntegrationFixture fixture)
        {
            _fixture = fixture;

            var provider = new Mock<IDbContextProvider>();
            // Two-arg overload used by GetCollection<T>(tenantId, name) for MagicLinks.
            provider.Setup(p => p.GetCollection<MagicLink>(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string _, string name) => _fixture.GetCollection<MagicLink>(_ns + "_" + name));
            // Single-arg GetDatabase(projectKey) used for config / credential / usage collections.
            provider.Setup(p => p.GetDatabase(It.IsAny<string>())).Returns(_fixture.Database);

            var config = new Mock<IConfiguration>();
            config.Setup(c => c["RootTenantId"]).Returns("root-tenant");

            _repo = new MagicLinkRepository(provider.Object, config.Object);
        }

        private static MagicLink NewLink(string projectKey, string? name = null) => new()
        {
            ItemId = Guid.NewGuid().ToString(),
            ProjectKey = projectKey,
            Name = name ?? "link",
            Uri = "https://example.com/x",
            Type = MagicLinkType.Action,
            CreatedAt = DateTime.UtcNow
        };

        [Fact]
        public async Task Create_Get_Update_RoundTrips()
        {
            var pk = _ns + "-p1";
            var link = NewLink(pk);
            var id = await _repo.CreateMagicLinkAsync(link);

            var loaded = await _repo.GetMagicLinkAsync(id, pk);
            loaded.Should().NotBeNull();

            loaded!.Name = "updated";
            (await _repo.UpdateMagicLinkAsync(loaded)).Should().BeTrue();
            (await _repo.GetMagicLinkAsync(id, pk))!.Name.Should().Be("updated");
        }

        [Fact]
        public async Task GetMagicLinksByIds_FiltersByProject()
        {
            var pk = _ns + "-p2";
            var a = NewLink(pk);
            var b = NewLink(pk);
            await _repo.CreateMagicLinkAsync(a);
            await _repo.CreateMagicLinkAsync(b);

            var result = await _repo.GetMagicLinksByIdsAsync(new List<string> { a.ItemId, b.ItemId }, pk);
            result.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetMagicLinks_WithFilters_ReturnsMatchesAndCount()
        {
            var pk = _ns + "-p3";
            var link = NewLink(pk, "Searchable");
            link.RequestMethod = "POST";
            await _repo.CreateMagicLinkAsync(link);

            var (links, total) = await _repo.GetMagicLinksAsync(new GetMagicLinksRequest
            {
                ProjectKey = pk,
                Type = MagicLinkType.Action,
                SearchText = "Search",
                RequestMethod = "post",
                Status = "Active"
            });

            total.Should().Be(1);
            links.Should().ContainSingle();
        }

        [Theory]
        [InlineData("ManuallyDisabled")]
        [InlineData("UsageLimitExceeded")]
        [InlineData("TimeExpired")]
        [InlineData("Active")]
        public async Task GetMagicLinks_StatusFilters_DoNotThrow(string status)
        {
            var pk = _ns + "-status-" + status;
            await _repo.CreateMagicLinkAsync(NewLink(pk));

            var (_, total) = await _repo.GetMagicLinksAsync(new GetMagicLinksRequest { ProjectKey = pk, Status = status });

            total.Should().BeGreaterThanOrEqualTo(0);
        }

        [Fact]
        public async Task IncrementUsageCount_IncreasesCount()
        {
            var pk = _ns + "-p4";
            var link = NewLink(pk);
            await _repo.CreateMagicLinkAsync(link);

            var updated = await _repo.IncrementUsageCountAsync(link.ItemId);
            updated!.UsageCount.Should().Be(1);
        }

        [Fact]
        public async Task MarkAsExpired_SetsExpired()
        {
            var pk = _ns + "-p5";
            var link = NewLink(pk);
            await _repo.CreateMagicLinkAsync(link);

            (await _repo.MarkAsExpiredAsync(link.ItemId, MagicLinkExpiredReason.ManuallyDisabled)).Should().BeTrue();
            var loaded = await _repo.GetMagicLinkAsync(link.ItemId, pk);
            loaded!.IsExpired.Should().BeTrue();
        }

        [Fact]
        public async Task LinkBasedActionConfig_CreateGetUpdate_Works()
        {
            var pk = _ns + "-cfg";
            var config = new LinkBasedActionConfig { ItemId = Guid.NewGuid().ToString(), ProjectKey = pk };

            var id = await _repo.CreateLinkBasedActionConfigAsync(config);
            id.Should().Be(config.ItemId);

            (await _repo.GetLinkBasedActionConfigAsync(pk)).Should().NotBeNull();
            (await _repo.GetLinkConfigAsync(config.ItemId, pk)).Should().NotBeNull();

            (await _repo.UpdateLinkBasedActionConfigAsync(config)).Should().BeTrue();
        }

        [Fact]
        public async Task CreateVisitorUsage_Inserts()
        {
            var usage = new MagicLinkVisitorUsage { ItemId = Guid.NewGuid().ToString(), ProjectKey = _ns + "-visitor" };
            await _repo.CreateVisitorUsageAsync(usage);

            var count = await _fixture.GetCollection<MagicLinkVisitorUsage>("MagicLinkVisitorUsages")
                .CountDocumentsAsync(u => u.ItemId == usage.ItemId);
            count.Should().Be(1);
        }

        [Fact]
        public async Task GetClientCredentials_ReturnsInserted()
        {
            var pk = _ns + "-cc";
            var credential = new ClientCredential { ItemId = Guid.NewGuid().ToString() };
            await _fixture.GetCollection<ClientCredential>("ClientCredentials").InsertOneAsync(credential);

            var loaded = await _repo.GetClientCredentialsAsync(credential.ItemId, pk);
            loaded.Should().NotBeNull();
        }
    }
}
