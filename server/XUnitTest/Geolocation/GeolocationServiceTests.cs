using DomainService.Geolocation;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

namespace XUnitTest.Geolocation
{
    /// <summary>
    /// Unit tests for <see cref="GeolocationService"/>. The service guards a paid lookup, so the
    /// things worth pinning are the two refusals that stop a request before it reaches the provider
    /// (no addresses, more than ten) and the fact that a provider failure comes back as an
    /// unsuccessful response rather than an exception. Visitor-address extraction prefers the
    /// forwarded header over the socket address, which decides what gets looked up at all.
    /// </summary>
    public class GeolocationServiceTests
    {
        private readonly Mock<IGeolocationRepository> _repository = new();

        private GeolocationService CreateService() => new(_repository.Object);

        private void SetupLookup(params IpLookup[] lookups) =>
            _repository.Setup(r => r.ResolveMultipleIpsToCountryAsync(
                          It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
                      .ReturnsAsync(lookups);

        private static HttpContext ContextWith(string? forwardedFor, string? remoteIp)
        {
            var context = new DefaultHttpContext();
            if (forwardedFor is not null)
                context.Request.Headers["X-Forwarded-For"] = forwardedFor;
            if (remoteIp is not null)
                context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteIp);
            return context;
        }

        // ---- LocateIpAsync ----

        [Fact]
        public async Task LocateIpAsync_RefusesARequestWithNoAddresses()
        {
            var result = await CreateService().LocateIpAsync(new LocateIpRequest { IpAddresses = [] });

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Be("IP addresses are required");
            _repository.Verify(r => r.ResolveMultipleIpsToCountryAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task LocateIpAsync_RefusesANullAddressList()
        {
            var result = await CreateService().LocateIpAsync(new LocateIpRequest { IpAddresses = null! });

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Be("IP addresses are required");
        }

        [Fact]
        public async Task LocateIpAsync_RefusesMoreThanTenAddresses()
        {
            var eleven = Enumerable.Range(1, 11).Select(i => $"10.0.0.{i}").ToList();

            var result = await CreateService().LocateIpAsync(new LocateIpRequest { IpAddresses = eleven });

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Be("Maximum 10 IP addresses allowed per request");
            _repository.Verify(r => r.ResolveMultipleIpsToCountryAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task LocateIpAsync_AcceptsExactlyTenAddresses()
        {
            SetupLookup();
            var ten = Enumerable.Range(1, 10).Select(i => $"10.0.0.{i}").ToList();

            var result = await CreateService().LocateIpAsync(new LocateIpRequest { IpAddresses = ten });

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task LocateIpAsync_ReturnsWhatTheProviderResolved()
        {
            SetupLookup(new IpLookup { StartIp = "8.8.8.8", CountryCode = "US" });

            var result = await CreateService().LocateIpAsync(
                new LocateIpRequest { IpAddresses = ["8.8.8.8"] });

            result.IsSuccess.Should().BeTrue();
            result.IpLookups.Should().ContainSingle();
            result.IpLookups!.First().CountryCode.Should().Be("US");
        }

        [Fact]
        public async Task LocateIpAsync_PassesTheProviderChoiceThrough()
        {
            SetupLookup();

            await CreateService().LocateIpAsync(new LocateIpRequest
            {
                IpAddresses = ["8.8.8.8"],
                UseCustomProvider = true,
            });

            _repository.Verify(r => r.ResolveMultipleIpsToCountryAsync(
                It.IsAny<IEnumerable<string>>(), true), Times.Once);
        }

        [Fact]
        public async Task LocateIpAsync_ReportsAProviderFailureRatherThanThrowing()
        {
            _repository.Setup(r => r.ResolveMultipleIpsToCountryAsync(
                          It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
                      .ThrowsAsync(new HttpRequestException("provider unreachable"));

            var result = await CreateService().LocateIpAsync(
                new LocateIpRequest { IpAddresses = ["8.8.8.8"] });

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("provider unreachable");
        }

        // ---- LocateAsync ----

        [Fact]
        public async Task LocateAsync_RefusesWhenTheRequestContextYieldedNoAddress()
        {
            var result = await CreateService().LocateAsync(new LocateRequest(), []);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Be("No IP addresses found in request context");
        }

        [Fact]
        public async Task LocateAsync_RefusesANullAddressList()
        {
            var result = await CreateService().LocateAsync(new LocateRequest(), null!);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Be("No IP addresses found in request context");
        }

        [Fact]
        public async Task LocateAsync_ResolvesTheAddressesItWasGiven()
        {
            SetupLookup(new IpLookup { StartIp = "1.1.1.1", CountryCode = "AU" });

            var result = await CreateService().LocateAsync(new LocateRequest(), ["1.1.1.1"]);

            result.IsSuccess.Should().BeTrue();
            result.IpLookups!.First().CountryCode.Should().Be("AU");
        }

        [Fact]
        public async Task LocateAsync_DoesNotApplyTheTenAddressCeiling()
        {
            // The ceiling guards a caller-supplied list; addresses taken from the request context
            // are however many the proxy chain produced.
            SetupLookup();
            var many = Enumerable.Range(1, 20).Select(i => $"10.0.0.{i}").ToList();

            var result = await CreateService().LocateAsync(new LocateRequest(), many);

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task LocateAsync_ReportsAProviderFailureRatherThanThrowing()
        {
            _repository.Setup(r => r.ResolveMultipleIpsToCountryAsync(
                          It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
                      .ThrowsAsync(new TimeoutException("provider timed out"));

            var result = await CreateService().LocateAsync(new LocateRequest(), ["1.1.1.1"]);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("provider timed out");
        }

        // ---- visitor addresses ----

        [Fact]
        public void GetVisitorsIpAddresses_PrefersTheForwardedHeaderOverTheSocket()
        {
            var addresses = CreateService()
                .GetVisitorsIpAddresses(ContextWith("203.0.113.7", "10.0.0.1"));

            // Behind a proxy the socket address is the proxy, not the visitor.
            addresses.Should().Equal("203.0.113.7");
        }

        [Fact]
        public void GetVisitorsIpAddresses_FallsBackToTheSocketWhenThereIsNoHeader()
        {
            var addresses = CreateService().GetVisitorsIpAddresses(ContextWith(null, "10.0.0.1"));

            addresses.Should().Equal("10.0.0.1");
        }

        [Fact]
        public void GetVisitorsIpAddresses_FallsBackToTheSocketForABlankHeader()
        {
            var addresses = CreateService().GetVisitorsIpAddresses(ContextWith("   ", "10.0.0.1"));

            addresses.Should().Equal("10.0.0.1");
        }

        [Fact]
        public void GetVisitorsIpAddresses_SplitsAProxyChainAndTrimsEachHop()
        {
            var addresses = CreateService()
                .GetVisitorsIpAddresses(ContextWith("203.0.113.7, 198.51.100.2 , 10.0.0.1", null));

            addresses.Should().Equal("203.0.113.7", "198.51.100.2", "10.0.0.1");
        }

        [Fact]
        public void GetVisitorsIpAddresses_DropsEmptyHopsFromTheChain()
        {
            var addresses = CreateService()
                .GetVisitorsIpAddresses(ContextWith("203.0.113.7,,198.51.100.2", null));

            addresses.Should().Equal("203.0.113.7", "198.51.100.2");
        }

        [Fact]
        public void GetVisitorsIpAddresses_YieldsNothingWhenThereIsNeitherHeaderNorSocket()
        {
            var addresses = CreateService().GetVisitorsIpAddresses(ContextWith(null, null));

            addresses.Should().BeEmpty();
        }
    }
}
