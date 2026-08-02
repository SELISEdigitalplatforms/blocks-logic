using Blocks.Genesis;
using DomainService.Geolocation;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Net;
using System.Text.Json;

namespace XUnitTest.Geolocation
{
    /// <summary>
    /// Unit tests for <see cref="GeolocationRepository"/>. The cache and the HTTP client are both
    /// mocked. Every public method swallows its own exceptions and answers with a safe default, so
    /// the failure cases assert that default rather than an expected throw.
    /// </summary>
    public class GeolocationRepositoryTests
    {
        private readonly Mock<ICacheClient> _cache = new();

        private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
        {
            public List<string> Urls { get; } = [];

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Urls.Add(request.RequestUri!.ToString());
                return Task.FromResult(responder(request));
            }
        }

        private StubHandler _handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });

        private GeolocationRepository Build(
            Func<HttpRequestMessage, HttpResponseMessage>? responder = null,
            string? apiUrl = "https://geo.example.com/lookup",
            string? apiKey = "geo-key")
        {
            if (responder is not null) _handler = new StubHandler(responder);

            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(_handler));

            var settings = new Dictionary<string, string?>();
            if (apiUrl is not null) settings["GeolocationApiUrl"] = apiUrl;
            if (apiKey is not null) settings["GeolocationApiKey"] = apiKey;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

            return new GeolocationRepository(_cache.Object, factory.Object, configuration);
        }

        private void Cached(string key, string? value) =>
            _cache.Setup(c => c.GetStringValueAsync(key)).ReturnsAsync(value);

        [Fact]
        public async Task IsGeoRestrictionEnabledAsync_ReadsTheFlagFromCache()
        {
            Cached("geo_restriction_enabled_t1", "true");

            (await Build().IsGeoRestrictionEnabledAsync("t1")).Should().BeTrue();
        }

        [Theory]
        [InlineData("false")]
        [InlineData("not-a-bool")]
        [InlineData("")]
        [InlineData(null)]
        public async Task IsGeoRestrictionEnabledAsync_DefaultsToFalse(string? cached)
        {
            Cached("geo_restriction_enabled_t1", cached);

            (await Build().IsGeoRestrictionEnabledAsync("t1")).Should().BeFalse();
        }

        [Fact]
        public async Task IsGeoRestrictionEnabledAsync_FailsClosedToFalseWhenTheCacheThrows()
        {
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("redis down"));

            (await Build().IsGeoRestrictionEnabledAsync("t1")).Should().BeFalse();
        }

        [Fact]
        public async Task IsCountryBlockedAsync_KeysTheLookupByTenantAndCountry()
        {
            Cached("blocked_country_t1_DE", "true");

            (await Build().IsCountryBlockedAsync("DE", "t1")).Should().BeTrue();
            (await Build().IsCountryBlockedAsync("FR", "t1")).Should().BeFalse("a different country has its own key");
        }

        [Fact]
        public async Task IsUserBlockedFromCountryAsync_KeysTheLookupByTenantUserAndCountry()
        {
            Cached("blocked_user_country_t1_u1_DE", "true");

            (await Build().IsUserBlockedFromCountryAsync("DE", "u1", "t1")).Should().BeTrue();
            (await Build().IsUserBlockedFromCountryAsync("DE", "u2", "t1")).Should().BeFalse();
        }

        [Fact]
        public async Task IsRoleBlockedFromCountryAsync_IsTrueWhenAnyRoleIsBlocked()
        {
            Cached("blocked_role_country_t1_admin_DE", null);
            Cached("blocked_role_country_t1_auditor_DE", "true");

            (await Build().IsRoleBlockedFromCountryAsync("DE", ["admin", "auditor"], "t1")).Should().BeTrue();
        }

        [Fact]
        public async Task IsRoleBlockedFromCountryAsync_IsFalseWhenNoRoleIsBlocked()
        {
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync("false");

            (await Build().IsRoleBlockedFromCountryAsync("DE", ["admin", "auditor"], "t1")).Should().BeFalse();
        }

        [Fact]
        public async Task IsRoleBlockedFromCountryAsync_IsFalseForAnEmptyRoleSet()
        {
            (await Build().IsRoleBlockedFromCountryAsync("DE", [], "t1")).Should().BeFalse();
            _cache.Verify(c => c.GetStringValueAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ResolveIpToCountryAsync_ReturnsNullWhenNoAddressIsSupplied()
        {
            (await Build().ResolveIpToCountryAsync([], "t1")).Should().BeNull();
            (await Build().ResolveIpToCountryAsync([""], "t1")).Should().BeNull();
        }

        [Fact]
        public async Task ResolveIpToCountryAsync_ReturnsTheCachedLookupWithoutRewritingIt()
        {
            var stored = new IpLookup { CountryCode = "DE", City = "Berlin" };
            Cached("ip_lookup_8.8.8.8", JsonSerializer.Serialize(stored));

            var result = await Build().ResolveIpToCountryAsync(["8.8.8.8"], "t1");

            result!.CountryCode.Should().Be("DE");
            result.City.Should().Be("Berlin");
            _cache.Verify(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
        }

        [Fact]
        public async Task ResolveIpToCountryAsync_CachesAFreshLookupForAnHour()
        {
            Cached("ip_lookup_1.1.1.1", null);

            var result = await Build().ResolveIpToCountryAsync(["1.1.1.1"], "t1");

            result.Should().NotBeNull();
            _cache.Verify(c => c.AddStringValueAsync("ip_lookup_1.1.1.1", It.IsAny<string>(), 3600), Times.Once);
        }

        [Fact]
        public async Task ResolveIpToCountryAsync_UsesTheFirstAddressOnly()
        {
            Cached("ip_lookup_1.1.1.1", null);

            await Build().ResolveIpToCountryAsync(["1.1.1.1", "2.2.2.2"], "t1");

            _cache.Verify(c => c.GetStringValueAsync("ip_lookup_1.1.1.1"), Times.Once);
            _cache.Verify(c => c.GetStringValueAsync("ip_lookup_2.2.2.2"), Times.Never);
        }

        [Fact]
        public async Task ResolveIpToCountryAsync_ReturnsNullWhenTheCacheThrows()
        {
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ThrowsAsync(new TimeoutException());

            (await Build().ResolveIpToCountryAsync(["1.1.1.1"], "t1")).Should().BeNull();
        }

        [Fact]
        public async Task ResolveIpToCountryAsync_FallsThroughWhenTheCachedPayloadIsNotAnIpLookup()
        {
            Cached("ip_lookup_1.1.1.1", "null");

            var result = await Build().ResolveIpToCountryAsync(["1.1.1.1"], "t1");

            result.Should().NotBeNull("a null payload has to be treated as a cache miss");
        }

        [Fact]
        public async Task ResolveMultipleIpsToCountryAsync_ReturnsOneEntryPerAddress()
        {
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync((string?)null);

            var result = await Build().ResolveMultipleIpsToCountryAsync(["1.1.1.1", "8.8.8.8"]);

            result.Should().NotBeNull();
            result.Length.Should().Be(2);
        }

        [Fact]
        public async Task ResolveMultipleIpsToCountryAsync_ReusesCachedEntries()
        {
            Cached("ip_lookup_1.1.1.1", JsonSerializer.Serialize(new IpLookup { CountryCode = "FR" }));
            Cached("ip_lookup_8.8.8.8", null);

            var result = await Build().ResolveMultipleIpsToCountryAsync(["1.1.1.1", "8.8.8.8"]);

            result.Should().Contain(r => r != null && r.CountryCode == "FR");
        }

        [Fact]
        public async Task ResolveMultipleIpsToCountryAsync_HandlesAnEmptySet()
        {
            var result = await Build().ResolveMultipleIpsToCountryAsync([]);

            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task ResolveMultipleIpsToCountryAsync_SurvivesAFailingExternalProvider()
        {
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync((string?)null);
            var sut = Build(responder: _ => throw new HttpRequestException("network down"));

            var act = () => sut.ResolveMultipleIpsToCountryAsync(["1.1.1.1"], useCustomProvider: true);

            await act.Should().NotThrowAsync();
        }
    }
}
