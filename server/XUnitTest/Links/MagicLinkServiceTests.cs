using Blocks.Genesis;
using DomainService.MagicLink;
using DomainService.MagicLink.Models;
using DomainService.MagicLink.Service;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using DomainService.MagicLink.Events;
using MagicLinkEntity = DomainService.MagicLink.Models.MagicLink;

namespace XUnitTest.Links
{
    /// <summary>
    /// Unit tests for <see cref="MagicLinkService"/>. Every dependency is an interface, so the
    /// repository, cache and message client are mocked and the create, read, remove and config
    /// paths are exercised in memory. The service catches its own exceptions and reports them on
    /// the response, so the failure cases assert the response rather than an expected throw.
    /// </summary>
    public class MagicLinkServiceTests
    {
        private readonly Mock<IMagicLinkRepository> _repo = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IMessageClient> _messages = new();
        private readonly MagicLinkService _sut;

        public MagicLinkServiceTests()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RootTenantId"] = "root-tenant",
                    ["MagicLinkBaseAddress"] = "https://links.example.com/",
                })
                .Build();

            // No stored link means the first generated id is accepted as unique.
            _repo.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string?>()))
                 .ReturnsAsync((MagicLinkEntity?)null);
            _repo.Setup(r => r.CreateMagicLinkAsync(It.IsAny<MagicLinkEntity>()))
                 .ReturnsAsync("created");

            _sut = new MagicLinkService(
                NullLogger<MagicLinkService>.Instance,
                _repo.Object,
                _cache.Object,
                _messages.Object,
                configuration);
        }

        private static CreateMagicLinkRequest Request(
            MagicLinkType type = MagicLinkType.Redirect,
            string uri = "https://target.example.com/page",
            string? projectKey = null,
            long expiryLifeSpan = 0,
            string? configId = null,
            string? method = null) => new()
            {
                Type = type,
                Uri = uri,
                ProjectKey = projectKey,
                ExpiryLifeSpan = expiryLifeSpan,
                LinkBasedActionConfigId = configId,
                RequestMethod = method,
            };

        private MagicLinkEntity? Created =>
            _created.Count > 0 ? _created[^1] : null;

        private readonly List<MagicLinkEntity> _created = [];

        private void CaptureCreates() =>
            _repo.Setup(r => r.CreateMagicLinkAsync(It.IsAny<MagicLinkEntity>()))
                 .ReturnsAsync((MagicLinkEntity l) =>
                 {
                     _created.Add(l);
                     return l.ItemId;
                 });

        [Fact]
        public async Task CreateLinkAsync_SavesTheLinkAndReturnsItsShortUri()
        {
            CaptureCreates();

            var result = await _sut.CreateLinkAsync(Request());

            result.IsSuccess.Should().BeTrue();
            result.LinkId.Should().NotBeNullOrWhiteSpace();
            result.ShortUri.Should().Be($"https://links.example.com/{result.LinkId}");
            result.Type.Should().Be(nameof(MagicLinkType.Redirect));

            _repo.Verify(r => r.CreateMagicLinkAsync(It.IsAny<MagicLinkEntity>()), Times.Once);
            _cache.Verify(c => c.AddStringValueAsync(result.LinkId!, It.IsAny<string>(), It.IsAny<long>()), Times.Once);
        }

        [Fact]
        public async Task CreateLinkAsync_FallsBackToTheRootTenantWhenNoProjectKeyIsGiven()
        {
            CaptureCreates();

            await _sut.CreateLinkAsync(Request(projectKey: null));

            Created!.ProjectKey.Should().Be("root-tenant");
        }

        [Fact]
        public async Task CreateLinkAsync_KeepsAnExplicitProjectKey()
        {
            CaptureCreates();

            await _sut.CreateLinkAsync(Request(projectKey: "proj-9"));

            Created!.ProjectKey.Should().Be("proj-9");
        }

        [Fact]
        public async Task CreateLinkAsync_SetsAnExpiryOnlyWhenALifespanIsGiven()
        {
            CaptureCreates();

            await _sut.CreateLinkAsync(Request(expiryLifeSpan: 60_000));
            Created!.ExpiryDate.Should().NotBeNull();
            Created!.IsExpired.Should().BeFalse();

            await _sut.CreateLinkAsync(Request(expiryLifeSpan: 0));
            Created!.ExpiryDate.Should().BeNull("a lifespan of zero means the link does not expire");
        }

        [Fact]
        public async Task CreateLinkAsync_NormalisesTheRequestMethodToUpperCase()
        {
            CaptureCreates();

            await _sut.CreateLinkAsync(Request(method: "post"));

            Created!.RequestMethod.Should().Be("POST");
        }

        [Fact]
        public async Task CreateLinkAsync_StartsTheUsageCountAtZero()
        {
            CaptureCreates();

            await _sut.CreateLinkAsync(Request());

            Created!.UsageCount.Should().Be(0);
        }

        [Fact]
        public async Task CreateLinkAsync_PrefersTheConfigShortUrlBaseOverConfiguration()
        {
            CaptureCreates();
            _repo.Setup(r => r.GetLinkConfigAsync("cfg-1", It.IsAny<string>()))
                 .ReturnsAsync(new LinkBasedActionConfig { ShortUrlBase = "https://custom.example.com/" });

            var result = await _sut.CreateLinkAsync(Request(configId: "cfg-1"));

            result.ShortUri.Should().StartWith("https://custom.example.com/");
        }

        [Fact]
        public async Task CreateLinkAsync_StillCreatesTheLinkWhenTheNamedConfigIsMissing()
        {
            CaptureCreates();
            _repo.Setup(r => r.GetLinkConfigAsync("missing", It.IsAny<string>()))
                 .ReturnsAsync((LinkBasedActionConfig?)null);

            var result = await _sut.CreateLinkAsync(Request(configId: "missing"));

            result.IsSuccess.Should().BeTrue();
            result.ShortUri.Should().StartWith("https://links.example.com/");
        }

        [Fact]
        public async Task CreateLinkAsync_RetriesWhenTheGeneratedIdCollides()
        {
            CaptureCreates();
            var calls = 0;
            _repo.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string?>()))
                 .ReturnsAsync(() => ++calls == 1
                     ? new MagicLinkEntity { ItemId = "taken" }
                     : null);

            var result = await _sut.CreateLinkAsync(Request());

            result.IsSuccess.Should().BeTrue();
            calls.Should().BeGreaterThan(1, "a collision has to force another attempt");
        }

        [Fact]
        public async Task CreateLinkAsync_ReportsTheFailureOnTheResponseWhenTheSaveThrows()
        {
            _repo.Setup(r => r.CreateMagicLinkAsync(It.IsAny<MagicLinkEntity>()))
                 .ThrowsAsync(new InvalidOperationException("mongo down"));

            var result = await _sut.CreateLinkAsync(Request());

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("mongo down");
        }

        [Fact]
        public async Task CreateLinksAsync_CreatesEveryRequestedLink()
        {
            CaptureCreates();

            var result = await _sut.CreateLinksAsync(new CreateMagicLinksRequest
            {
                Requests = [Request(uri: "https://a.example.com"), Request(uri: "https://b.example.com")],
                ProjectKey = "proj-1",
            });

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.CreateMagicLinkAsync(It.IsAny<MagicLinkEntity>()), Times.Exactly(2));
        }

        [Fact]
        public async Task CreateLinksAsync_HandlesAnEmptyBatch()
        {
            var result = await _sut.CreateLinksAsync(new CreateMagicLinksRequest { Requests = [], ProjectKey = "p" });

            _repo.Verify(r => r.CreateMagicLinkAsync(It.IsAny<MagicLinkEntity>()), Times.Never);
            result.Should().NotBeNull();
        }

        [Fact]
        public async Task GetLinkAsync_ReturnsTheStoredLink()
        {
            _repo.Setup(r => r.GetMagicLinkAsync("abc123", It.IsAny<string?>()))
                 .ReturnsAsync(new MagicLinkEntity
                 {
                     ItemId = "abc123",
                     Uri = "https://target.example.com",
                     Type = MagicLinkType.Redirect,
                     ProjectKey = "proj-1",
                 });

            var result = await _sut.GetLinkAsync(new GetMagicLinkRequest { ItemId = "abc123", ProjectKey = "proj-1" });

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task GetLinkAsync_ReportsAMissingLink()
        {
            _repo.Setup(r => r.GetMagicLinkAsync("nope", It.IsAny<string?>()))
                 .ReturnsAsync((MagicLinkEntity?)null);

            var result = await _sut.GetLinkAsync(new GetMagicLinkRequest { ItemId = "nope", ProjectKey = "proj-1" });

            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task GetLinksAsync_PassesThroughTheRepositoryPage()
        {
            _repo.Setup(r => r.GetMagicLinksAsync(It.IsAny<GetMagicLinksRequest>()))
                 .ReturnsAsync(([new MagicLinkEntity { ItemId = "a" }], 1));

            var result = await _sut.GetLinksAsync(new GetMagicLinksRequest { ProjectKey = "proj-1" });

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task RemoveLinksAsync_MarksEachRequestedLinkExpired()
        {
            _repo.Setup(r => r.GetMagicLinksByIdsAsync(It.IsAny<List<string>>(), It.IsAny<string>()))
                 .ReturnsAsync([
                     new MagicLinkEntity { ItemId = "a", ProjectKey = "proj-1" },
                     new MagicLinkEntity { ItemId = "b", ProjectKey = "proj-1" },
                 ]);
            _repo.Setup(r => r.MarkAsExpiredAsync(It.IsAny<string>(), It.IsAny<MagicLinkExpiredReason>()))
                 .ReturnsAsync(true);
            _cache.Setup(c => c.KeyExistsAsync(It.IsAny<string>())).ReturnsAsync(true);

            var result = await _sut.RemoveLinksAsync(new RemoveMagicLinksRequest
            {
                LinkIds = ["a", "b"],
                ProjectKey = "proj-1",
            });

            result.Should().NotBeNull();
            _cache.Verify(c => c.RemoveKeyAsync("a"), Times.Once);
            _cache.Verify(c => c.RemoveKeyAsync("b"), Times.Once);
        }

        [Fact]
        public async Task RemoveLinksAsync_SkipsTheCacheWhenTheKeyIsNotThere()
        {
            _repo.Setup(r => r.GetMagicLinksByIdsAsync(It.IsAny<List<string>>(), It.IsAny<string>()))
                 .ReturnsAsync([new MagicLinkEntity { ItemId = "a", ProjectKey = "proj-1" }]);
            _repo.Setup(r => r.MarkAsExpiredAsync(It.IsAny<string>(), It.IsAny<MagicLinkExpiredReason>()))
                 .ReturnsAsync(true);
            _cache.Setup(c => c.KeyExistsAsync("a")).ReturnsAsync(false);

            await _sut.RemoveLinksAsync(new RemoveMagicLinksRequest { LinkIds = ["a"], ProjectKey = "proj-1" });

            _cache.Verify(c => c.RemoveKeyAsync("a"), Times.Never);
        }

        [Fact]
        public async Task RemoveLinksAsync_ReportsTheFailureWhenTheLookupThrows()
        {
            _repo.Setup(r => r.GetMagicLinksByIdsAsync(It.IsAny<List<string>>(), It.IsAny<string>()))
                 .ThrowsAsync(new TimeoutException("mongo down"));

            var result = await _sut.RemoveLinksAsync(new RemoveMagicLinksRequest { LinkIds = ["a"], ProjectKey = "p" });

            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task GetLinkBasedActionConfigAsync_ReturnsTheStoredConfig()
        {
            _repo.Setup(r => r.GetLinkBasedActionConfigAsync("proj-1"))
                 .ReturnsAsync(new LinkBasedActionConfig { ShortUrlBase = "https://s.example.com" });

            var result = await _sut.GetLinkBasedActionConfigAsync(
                new GetLinkBasedActionConfigRequest { ProjectKey = "proj-1" });

            result.Should().NotBeNull();
        }

        [Fact]
        public async Task GetLinkBasedActionConfigAsync_HandlesNoConfigAtAll()
        {
            _repo.Setup(r => r.GetLinkBasedActionConfigAsync(It.IsAny<string>()))
                 .ReturnsAsync((LinkBasedActionConfig?)null);

            var result = await _sut.GetLinkBasedActionConfigAsync(
                new GetLinkBasedActionConfigRequest { ProjectKey = "proj-1" });

            result.Should().NotBeNull();
        }

        [Fact]
        public async Task SendUsageEventAsync_PublishesTheEvent()
        {
            await _sut.SendUsageEventAsync(new MagicLinkUsageEvent
            {
                LinkId = "abc",
                ProjectKey = "proj-1",
            });

            _messages.Invocations.Should().NotBeEmpty("the usage event has to reach the message client");
        }

        [Fact]
        public async Task SendActionEventAsync_PublishesTheEvent()
        {
            await _sut.SendActionEventAsync(new MagicLinkActionEvent
            {
                LinkId = "abc",
                ProjectKey = "proj-1",
            });

            _messages.Invocations.Should().NotBeEmpty("the action event has to reach the message client");
        }
    }
}
