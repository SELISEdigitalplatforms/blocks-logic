using System.Net;
using FluentAssertions;
using Proxy.DomainService.Utils;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// Covers <see cref="ProxyUpstreamGuard"/>: the synchronous literal-IP / scheme check used at
    /// config-write time and the DNS-resolving re-check used just before the send.
    /// </summary>
    public class ProxyUpstreamGuardTests
    {
        [Theory]
        [InlineData("127.0.0.1")]
        [InlineData("127.9.9.9")]
        [InlineData("10.0.0.1")]
        [InlineData("10.255.255.255")]
        [InlineData("172.16.0.1")]
        [InlineData("172.31.255.1")]
        [InlineData("192.168.1.1")]
        [InlineData("169.254.169.254")]
        [InlineData("0.0.0.0")]
        [InlineData("::1")]
        [InlineData("fe80::1")]
        [InlineData("fc00::1")]
        [InlineData("fd12:3456::1")]
        [InlineData("::ffff:127.0.0.1")]
        [InlineData("::ffff:10.0.0.1")]
        public void IsBlockedAddress_TrueForPrivateLoopbackLinkLocal(string ip)
        {
            ProxyUpstreamGuard.IsBlockedAddress(IPAddress.Parse(ip)).Should().BeTrue();
        }

        [Theory]
        [InlineData("8.8.8.8")]
        [InlineData("1.1.1.1")]
        [InlineData("172.15.0.1")]
        [InlineData("172.32.0.1")]
        [InlineData("192.167.0.1")]
        [InlineData("2606:4700:4700::1111")]
        public void IsBlockedAddress_FalseForPublic(string ip)
        {
            ProxyUpstreamGuard.IsBlockedAddress(IPAddress.Parse(ip)).Should().BeFalse();
        }

        [Fact]
        public void IsDisallowedTarget_RejectsNonHttpScheme()
        {
            ProxyUpstreamGuard.IsDisallowedTarget("ftp://example.com/x", out var reason).Should().BeTrue();
            reason.Should().Contain("http or https");
        }

        [Fact]
        public void IsDisallowedTarget_RejectsLoopbackLiteral()
        {
            ProxyUpstreamGuard.IsDisallowedTarget("https://127.0.0.1/x", out _).Should().BeTrue();
        }

        [Fact]
        public void IsDisallowedTarget_PassesHostname_NoDnsAtWriteTime()
        {
            // A hostname is never resolved here; that is deferred to the send-time re-check.
            ProxyUpstreamGuard.IsDisallowedTarget("https://api.stripe.com/v1", out _).Should().BeFalse();
        }

        [Fact]
        public async Task IsTargetBlockedAsync_TrueWhenHostResolvesToLoopback()
        {
            var guard = new ProxyUpstreamGuard((_, _) => Task.FromResult(new[] { IPAddress.Loopback }));

            (await guard.IsTargetBlockedAsync("https://rebind.example.com/x")).Should().BeTrue();
        }

        [Fact]
        public async Task IsTargetBlockedAsync_FalseWhenHostResolvesToPublic()
        {
            var guard = new ProxyUpstreamGuard((_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }));

            (await guard.IsTargetBlockedAsync("https://example.com/x")).Should().BeFalse();
        }

        [Fact]
        public async Task IsTargetBlockedAsync_TrueWhenAnyResolvedAddressIsPrivate()
        {
            var guard = new ProxyUpstreamGuard((_, _) => Task.FromResult(new[]
            {
                IPAddress.Parse("93.184.216.34"),
                IPAddress.Parse("10.0.0.5"),
            }));

            (await guard.IsTargetBlockedAsync("https://example.com/x")).Should().BeTrue();
        }

        [Fact]
        public async Task IsTargetBlockedAsync_FalseWhenDnsFails()
        {
            var guard = new ProxyUpstreamGuard((_, _) =>
                Task.FromException<IPAddress[]>(new System.Net.Sockets.SocketException()));

            (await guard.IsTargetBlockedAsync("https://no-such-host.invalid/x")).Should().BeFalse();
        }

        [Fact]
        public async Task IsTargetBlockedAsync_TrueForLiteralPrivateIp_WithoutResolving()
        {
            var resolverCalled = false;
            var guard = new ProxyUpstreamGuard((_, _) =>
            {
                resolverCalled = true;
                return Task.FromResult(Array.Empty<IPAddress>());
            });

            (await guard.IsTargetBlockedAsync("https://169.254.169.254/latest/")).Should().BeTrue();
            resolverCalled.Should().BeFalse();
        }
    }
}
