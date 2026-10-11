using FluentAssertions;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Utils;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// The console shows server validation messages word for word, and an undecided limit is never shown
    /// (decision 2026-10-08 "no false claims in UI text"). The check still runs; only the number leaves the text.
    /// Decided numbers stay: timeout 1-30 s, retry attempts max 3, rate limit 1-100 000.
    /// </summary>
    public class ProxyValidationMessageTests
    {
        private static ProxyConfigValidationResult Validate(
            string name = "X",
            string upstream = "https://api.x.com",
            IEnumerable<ProxyKeyValueInputDto>? headers = null,
            IEnumerable<ProxyKeyValueInputDto>? query = null,
            IEnumerable<ProxyRouteConfigInputDto>? routes = null,
            ProxyAccessInputDto? access = null,
            ProxyResilienceInputDto? resilience = null) =>
            ProxyConfigValidator.Validate(
                name, upstream, new[] { "GET" }, headers, query,
                routes: routes, access: access, resilience: resilience);

        private static void ShouldFailWithoutNumber(ProxyConfigValidationResult result, string key, string expected)
        {
            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainKey(key).WhoseValue.Should().Be(expected);
            expected.Any(char.IsDigit).Should().BeFalse();
        }

        [Fact]
        public void NameTooLong_IsRefused_WithoutTheLimit() =>
            ShouldFailWithoutNumber(Validate(name: new string('a', 81)), "name", "Name is required and must not be too long.");

        [Fact]
        public void NameAtTheLimit_IsAccepted() =>
            Validate(name: new string('a', 80)).Errors.Should().NotContainKey("name");

        [Fact]
        public void UpstreamTooLong_IsRefused_WithoutTheLimit() =>
            ShouldFailWithoutNumber(
                Validate(upstream: "https://api.x.com/" + new string('a', 2048)), "upstream", "Upstream URL is too long.");

        [Fact]
        public void KeyTooLong_IsRefused_WithoutTheLimit() =>
            ShouldFailWithoutNumber(
                Validate(query: new[] { new ProxyKeyValueInputDto { Key = new string('k', 257), Value = "v" } }),
                "query",
                "Each query key is required, must not be too long, and must not have leading or trailing whitespace.");

        [Fact]
        public void ValueTooLong_IsRefused_WithoutTheLimit() =>
            ShouldFailWithoutNumber(
                Validate(headers: new[] { new ProxyKeyValueInputDto { Key = "X-Api", Value = new string('v', 4097) } }),
                "headers",
                "Each headers value is too long.");

        [Fact]
        public void TooManyRoutes_IsRefused_WithoutTheLimit() =>
            ShouldFailWithoutNumber(
                Validate(routes: Enumerable.Range(0, ProxyRoutePath.MaxRoutes + 1)
                    .Select(i => new ProxyRouteConfigInputDto { Method = "GET", Path = $"r{i}" })
                    .ToArray()),
                "routes",
                "Too many routes.");

        [Fact]
        public void RoutePathTooLong_IsRefused_WithoutTheLimit()
        {
            ProxyRoutePath.TryParseTemplate(new string('a', ProxyRoutePath.MaxPathLength + 1), out _, out var reason)
                .Should().BeFalse();
            reason.Should().Be("A path is too long.");
        }

        [Fact]
        public void RoutePathTooManySegments_IsRefused_WithoutTheLimit()
        {
            var path = string.Join('/', Enumerable.Repeat("a", ProxyRoutePath.MaxSegments + 1));
            ProxyRoutePath.TryParseTemplate(path, out _, out var reason).Should().BeFalse();
            reason.Should().Be("A path has too many segments.");
        }

        [Fact]
        public void TooManyAccessEntries_IsRefused_WithoutTheLimit() =>
            ShouldFailWithoutNumber(
                Validate(access: new ProxyAccessInputDto
                {
                    Kind = "BlocksToken",
                    Roles = new ProxyAccessRuleDto { Values = Enumerable.Range(0, 51).Select(i => $"role{i}").ToList() },
                }),
                "access.roles",
                "Too many roles entries.");

        [Fact]
        public void AccessEntryTooLong_IsRefused_WithoutTheLimit() =>
            ShouldFailWithoutNumber(
                Validate(access: new ProxyAccessInputDto
                {
                    Kind = "BlocksToken",
                    Permissions = new ProxyAccessRuleDto { Values = new List<string> { new string('p', 201) } },
                }),
                "access.permissions",
                "Each permissions entry is too long.");

        [Theory]
        [InlineData(0)]
        [InlineData(31)]
        public void RetryDelayOutOfRange_IsRefused_WithoutTheLimit(int delay) =>
            ShouldFailWithoutNumber(
                Validate(resilience: new ProxyResilienceInputDto
                {
                    Retry = new ProxyRetryInputDto { Attempts = 2, Idempotent = true, InitialDelaySeconds = delay },
                }),
                "resilience",
                "The retry delay for this proxy must be at least one second and not too long.");

        [Theory]
        [InlineData(0)]
        [InlineData(101)]
        public void BreakerThresholdOutOfRange_IsRefused_WithoutTheLimit(int threshold) =>
            ShouldFailWithoutNumber(
                Validate(resilience: new ProxyResilienceInputDto
                {
                    Breaker = new ProxyBreakerInputDto { FailureThreshold = threshold, OpenSeconds = 30 },
                }),
                "resilience",
                "The breaker failure threshold for this proxy must be at least one and not too high.");

        [Theory]
        [InlineData(0)]
        [InlineData(601)]
        public void BreakerOpenOutOfRange_IsRefused_WithoutTheLimit(int open) =>
            ShouldFailWithoutNumber(
                Validate(resilience: new ProxyResilienceInputDto
                {
                    Breaker = new ProxyBreakerInputDto { FailureThreshold = 5, OpenSeconds = open },
                }),
                "resilience",
                "The breaker open duration for this proxy must be at least one second and not too long.");

        [Fact]
        public void DecidedTimeoutRange_KeepsItsNumbers()
        {
            var result = Validate(resilience: new ProxyResilienceInputDto { TimeoutSeconds = 31 });
            result.Errors.Should().ContainKey("resilience")
                .WhoseValue.Should().Be("The timeout for this proxy must be between 1 and 30 seconds.");
        }
    }
}
