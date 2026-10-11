using FluentAssertions;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Utils;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// PS-10: a proxy config may not set headers that steer where and how the request travels. Covers the
    /// list itself and all three places a header can be configured: proxy-wide, per method, per route.
    /// </summary>
    public class ProxyReservedHeadersTests
    {
        [Theory]
        [InlineData("Host")]
        [InlineData("host")]
        [InlineData(" HOST ")]
        [InlineData("Content-Length")]
        [InlineData("Transfer-Encoding")]
        [InlineData("Connection")]
        [InlineData("Keep-Alive")]
        [InlineData("Upgrade")]
        [InlineData("TE")]
        [InlineData("Trailer")]
        [InlineData("Expect")]
        [InlineData("Forwarded")]
        [InlineData("X-Real-IP")]
        [InlineData("Proxy-Authorization")]
        [InlineData("x-forwarded-for")]
        [InlineData("X-Forwarded-Host")]
        public void Reserved_names_are_reserved_in_any_case(string name)
            => ProxyReservedHeaders.IsReserved(name).Should().BeTrue();

        [Theory]
        [InlineData("Authorization")]
        [InlineData("X-Api-Key")]
        [InlineData("Content-Type")]
        [InlineData("Accept")]
        [InlineData("OpenAI-Organization")]
        [InlineData("X-Hostname")]
        public void Ordinary_api_headers_are_allowed(string name)
            => ProxyReservedHeaders.IsReserved(name).Should().BeFalse();

        private static List<ProxyKeyValueInputDto> One(string key) => [new() { Key = key, Value = "v" }];

        [Fact]
        public void A_proxy_wide_Host_header_is_refused_on_save()
        {
            var result = ProxyConfigValidator.Validate(
                "Vendor", "https://api.vendor.com", ["GET"], headers: One("Host"), query: null);

            result.IsValid.Should().BeFalse();
            result.Errors["headers"].Should().Contain("\"Host\"").And.Contain("cannot be set");
        }

        [Fact]
        public void A_per_method_X_Forwarded_For_header_is_refused_on_save()
        {
            var result = ProxyConfigValidator.Validate(
                "Vendor", "https://api.vendor.com", ["GET"], headers: null, query: null,
                methodConfigs: [new ProxyMethodConfigInputDto { Method = "GET", Headers = One("X-Forwarded-For") }]);

            result.IsValid.Should().BeFalse();
            result.Errors["methodConfigs"].Should().Contain("X-Forwarded-For");
        }

        [Fact]
        public void A_per_route_Transfer_Encoding_header_is_refused_on_save()
        {
            var result = ProxyConfigValidator.Validate(
                "Vendor", "https://api.vendor.com", ["GET"], headers: null, query: null,
                routes: [new ProxyRouteConfigInputDto { Method = "GET", Path = "items", Headers = One("Transfer-Encoding") }]);

            result.IsValid.Should().BeFalse();
            result.Errors["routes"].Should().Contain("Transfer-Encoding");
        }

        [Fact]
        public void Ordinary_headers_still_save()
        {
            var result = ProxyConfigValidator.Validate(
                "Vendor", "https://api.vendor.com", ["GET"],
                headers: [new() { Key = "Authorization", Value = "Bearer x" }, new() { Key = "X-Api-Key", Value = "k" }],
                query: null);

            result.Errors.Should().NotContainKey("headers");
        }
    }
}
