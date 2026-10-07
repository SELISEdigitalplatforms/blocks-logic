using System.Net;
using System.Net.Sockets;
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

        [Theory]
        [InlineData("100.64.0.1")]
        [InlineData("100.100.100.200")]   // Alibaba metadata
        [InlineData("168.63.129.16")]     // Azure WireServer
        [InlineData("192.0.0.1")]
        [InlineData("198.18.0.1")]
        [InlineData("198.19.255.255")]
        [InlineData("224.0.0.1")]
        [InlineData("255.255.255.255")]
        [InlineData("ff02::1")]
        [InlineData("fec0::1")]
        [InlineData("::10.0.0.1")]          // IPv4-compatible
        [InlineData("64:ff9b::a9fe:a9fe")]  // NAT64 of 169.254.169.254
        [InlineData("2002:7f00:1::")]       // 6to4 of 127.0.0.1
        [InlineData("fe80::1%2")]           // scope id
        public void IsBlockedAddress_TrueForRangesAddedForP4(string ip)
        {
            ProxyUpstreamGuard.IsBlockedAddress(IPAddress.Parse(ip)).Should().BeTrue();
        }

        [Theory]
        [InlineData("100.63.255.255")]
        [InlineData("100.128.0.1")]
        [InlineData("168.63.129.17")]
        [InlineData("198.20.0.1")]
        [InlineData("223.255.255.255")]
        [InlineData("64:ff9b::808:808")]    // NAT64 of 8.8.8.8
        [InlineData("2002:808:808::")]      // 6to4 of 8.8.8.8
        public void IsBlockedAddress_FalseForPublicNeighbours(string ip)
        {
            ProxyUpstreamGuard.IsBlockedAddress(IPAddress.Parse(ip)).Should().BeFalse();
        }

        [Theory]
        [InlineData("https://localhost/x")]
        [InlineData("https://LOCALHOST./x")]
        [InlineData("https://api.localhost/x")]
        [InlineData("http://metadata.google.internal/computeMetadata/v1/")]
        [InlineData("http://metadata/x")]
        [InlineData("http://instance-data.ec2.internal/latest/")]
        public void IsDisallowedTarget_RejectsLocalAndMetadataNames(string url)
        {
            ProxyUpstreamGuard.IsDisallowedTarget(url, out var reason).Should().BeTrue();
            reason.Should().Contain("metadata");
        }

        [Fact]
        public async Task IsTargetBlockedAsync_TrueForMetadataName_WithoutResolving()
        {
            var resolverCalled = false;
            var guard = new ProxyUpstreamGuard((_, _) =>
            {
                resolverCalled = true;
                return Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") });
            });

            (await guard.IsTargetBlockedAsync("http://metadata.google.internal/x")).Should().BeTrue();
            resolverCalled.Should().BeFalse();
        }

        // ---------- connect-time enforcement (DNS rebinding) ----------

        [Fact]
        public async Task Send_NameRebindsToLoopbackAtConnect_IsRefused_AndNothingConnects()
        {
            // First answer (the pre-send check) is public; the second (the connect) is the real local
            // listener. The connect callback must refuse it and never open a socket to it.
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var calls = 0;
            var guard = new ProxyUpstreamGuard((_, _) => Task.FromResult(
                Interlocked.Increment(ref calls) == 1
                    ? new[] { IPAddress.Parse("93.184.216.34") }
                    : new[] { IPAddress.Loopback }));

            (await guard.IsTargetBlockedAsync($"http://rebind.example.com:{port}/")).Should().BeFalse();

            using var client = new HttpClient(ProxyUpstreamGuard.CreatePrimaryHandler(guard));
            var act = () => client.GetAsync($"http://rebind.example.com:{port}/");

            var ex = await act.Should().ThrowAsync<HttpRequestException>();
            ex.Which.InnerException.Should().BeOfType<ProxyUpstreamBlockedException>();
            listener.Pending().Should().BeFalse();
        }

        [Fact]
        public async Task Send_AnyResolvedAddressBlocked_IsRefused()
        {
            var guard = new ProxyUpstreamGuard((_, _) => Task.FromResult(new[]
            {
                IPAddress.Parse("93.184.216.34"),
                IPAddress.Parse("168.63.129.16"),
            }));

            using var client = new HttpClient(ProxyUpstreamGuard.CreatePrimaryHandler(guard));
            var act = () => client.GetAsync("http://mixed.example.com/");

            (await act.Should().ThrowAsync<HttpRequestException>())
                .Which.InnerException.Should().BeOfType<ProxyUpstreamBlockedException>();
        }

        [Fact]
        public async Task Send_LiteralBlockedIp_IsRefused_WithoutResolving()
        {
            var resolverCalled = false;
            var guard = new ProxyUpstreamGuard((_, _) =>
            {
                resolverCalled = true;
                return Task.FromResult(Array.Empty<IPAddress>());
            });

            using var client = new HttpClient(ProxyUpstreamGuard.CreatePrimaryHandler(guard));
            var act = () => client.GetAsync("http://169.254.169.254/latest/meta-data/");

            (await act.Should().ThrowAsync<HttpRequestException>())
                .Which.InnerException.Should().BeOfType<ProxyUpstreamBlockedException>();
            resolverCalled.Should().BeFalse();
        }

        [Fact]
        public async Task Send_DnsFails_FailsClosed_NoConnection()
        {
            var guard = new ProxyUpstreamGuard((_, _) =>
                Task.FromException<IPAddress[]>(new SocketException((int)SocketError.HostNotFound)));

            using var client = new HttpClient(ProxyUpstreamGuard.CreatePrimaryHandler(guard));
            var act = () => client.GetAsync("http://no-such-host.invalid/");

            // Unreachable, not blocked: the caller sees UpstreamUnreachable, and no socket was opened.
            (await act.Should().ThrowAsync<HttpRequestException>())
                .Which.InnerException.Should().BeOfType<SocketException>();
        }

        [Fact]
        public void CreatePrimaryHandler_NoRedirects_NoAmbientProxy_GuardedConnect()
        {
            using var handler = ProxyUpstreamGuard.CreatePrimaryHandler(new ProxyUpstreamGuard());

            handler.AllowAutoRedirect.Should().BeFalse();
            handler.UseProxy.Should().BeFalse();
            handler.ConnectCallback.Should().NotBeNull();
        }

        [Fact]
        public void CreatePrimaryHandler_KeepsNoCookies_SoNoTenantCarriesAnothersSession()
        {
            // PX-2: one handler serves every tenant; a shared cookie jar would replay tenant A's vendor
            // session on tenant B's call to the same host.
            using var handler = ProxyUpstreamGuard.CreatePrimaryHandler(new ProxyUpstreamGuard());

            handler.UseCookies.Should().BeFalse();
        }
    }
}
