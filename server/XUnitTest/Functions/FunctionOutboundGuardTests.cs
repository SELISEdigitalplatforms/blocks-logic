using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using Functions.DomainService.Utils;
using Functions.DomainService.Validation;

namespace XUnitTest.Functions
{
    /// <summary>
    /// <see cref="FunctionOutboundGuard"/>: the SSRF guard between a tenant-typed output-action URL
    /// and the platform's network. Weighted toward the spellings and tricks an attacker actually
    /// uses — alternate IP notations, mapped IPv6, names that resolve inward, and a name that
    /// changes its answer between the check and the connection.
    /// </summary>
    public class FunctionOutboundGuardTests
    {
        // ------------------------------------------------------- save time, no DNS ----

        [Theory]
        [InlineData("http://127.0.0.1/x")]
        [InlineData("http://127.1.2.3/x")]
        [InlineData("http://10.0.0.1/x")]
        [InlineData("http://172.16.5.4/x")]
        [InlineData("http://172.31.255.255/x")]
        [InlineData("http://192.168.0.1/x")]
        [InlineData("http://169.254.169.254/latest/meta-data/")]
        [InlineData("http://0.0.0.0/x")]
        [InlineData("http://[::1]/x")]
        [InlineData("http://[::]/x")]
        [InlineData("http://[fc00::1]/x")]
        [InlineData("http://[fd00:ec2::254]/x")]
        [InlineData("http://[fe80::1]/x")]
        [InlineData("http://[::ffff:10.0.0.1]/x")]
        [InlineData("http://[::ffff:127.0.0.1]/x")]
        [InlineData("http://[::ffff:169.254.169.254]/x")]
        [InlineData("http://[::ffff:a00:1]/x")]
        [InlineData("http://2130706433/x")]          // decimal 127.0.0.1
        [InlineData("http://0177.0.0.1/x")]          // octal
        [InlineData("http://0x7f.0.0.1/x")]          // hex
        [InlineData("http://0x7f000001/x")]          // single hex
        [InlineData("http://017700000001/x")]        // single octal
        [InlineData("http://127.1/x")]               // short form
        [InlineData("http://2852039166/x")]          // decimal 169.254.169.254
        [InlineData("http://100.100.100.200/x")]     // CGNAT: Alibaba metadata
        [InlineData("http://168.63.129.16/x")]       // Azure WireServer
        [InlineData("http://224.0.0.1/x")]
        [InlineData("http://255.255.255.255/x")]
        [InlineData("http://[64:ff9b::a00:1]/x")]    // NAT64 of 10.0.0.1
        [InlineData("http://[2002:a00:1::]/x")]      // 6to4 of 10.0.0.1
        [InlineData("http://[::10.0.0.1]/x")]        // IPv4-compatible
        [InlineData("http://[ff02::1]/x")]
        [InlineData("http://[fec0::1]/x")]
        public void A_literal_blocked_address_in_any_spelling_is_refused_at_save(string url)
        {
            FunctionOutboundGuard.IsDisallowedUrl(url, out var reason).Should().BeTrue(url);
            reason.Should().NotBeNullOrWhiteSpace();
        }

        [Theory]
        [InlineData("http://localhost/x")]
        [InlineData("http://LOCALHOST:8080/x")]
        [InlineData("http://localhost./x")]
        [InlineData("http://api.localhost/x")]
        [InlineData("http://localhost.localdomain/x")]
        [InlineData("http://ip6-localhost/x")]
        [InlineData("http://metadata.google.internal/computeMetadata/v1/")]
        [InlineData("http://metadata/x")]
        [InlineData("http://instance-data/x")]
        [InlineData("http://redis:6379/")]           // single-label: a container / cluster service
        [InlineData("http://mongo/")]
        [InlineData("http://1.2.3.4.5/x")]           // numeric but not an IPv4 Uri accepts
        public void A_host_meaning_this_machine_or_an_internal_service_is_refused_at_save(string url)
        {
            FunctionOutboundGuard.IsDisallowedUrl(url, out _).Should().BeTrue(url);
        }

        [Theory]
        [InlineData("https://hooks.slack.com/services/T/B/X")]
        [InlineData("https://api.stripe.com/v1/charges")]
        [InlineData("http://93.184.216.34/x")]
        [InlineData("https://[2606:2800:220:1:248:1893:25c8:1946]/x")]
        [InlineData("https://172.32.0.1/x")]          // just outside 172.16/12
        [InlineData("https://100.128.0.1/x")]         // just outside 100.64/10
        [InlineData("https://my-localhost-tools.example.com/x")]
        [InlineData("https://123.example.com/x")]
        public void A_public_destination_passes_at_save(string url)
        {
            FunctionOutboundGuard.IsDisallowedUrl(url, out var reason).Should().BeFalse(reason);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not a url")]
        public void A_non_url_is_left_to_the_validators_own_rule(string? url)
            => FunctionOutboundGuard.IsDisallowedUrl(url, out _).Should().BeFalse();

        [Fact]
        public void A_non_http_scheme_is_refused()
            => FunctionOutboundGuard.IsDisallowedUrl("gopher://example.com/x", out _).Should().BeTrue();

        [Fact]
        public void An_address_with_a_scope_id_is_judged_by_the_address()
            => FunctionOutboundGuard.IsBlockedAddress(IPAddress.Parse("fe80::1%2")).Should().BeTrue();

        [Fact]
        public void A_null_address_fails_closed()
            => FunctionOutboundGuard.IsBlockedAddress(null!).Should().BeTrue();

        // --------------------------------------------------------- validator wiring ----

        private static OutputAction Action(string url, bool enabled = true) => new()
        {
            Id = "a1",
            Kind = OutputActionKind.ExternalHttp,
            Enabled = enabled,
            Url = url,
            Method = "POST",
            TimeoutSeconds = 5,
        };

        [Theory]
        [InlineData("http://169.254.169.254/latest/meta-data/")]
        [InlineData("http://[::ffff:10.0.0.1]/x")]
        [InlineData("http://2130706433/x")]
        [InlineData("http://localhost:5000/api")]
        public void The_save_validator_rejects_an_inward_output_action(string url)
        {
            var result = new OutputActionValidator().Validate(Action(url));

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(OutputAction.Url) && e.ErrorMessage.Contains("not allowed"));
        }

        [Fact]
        public void The_save_validator_accepts_a_public_output_action()
            => new OutputActionValidator().Validate(Action("https://hooks.example.com/x")).IsValid.Should().BeTrue();

        [Fact]
        public void A_non_url_gets_one_message_not_two()
        {
            var result = new OutputActionValidator().Validate(Action("nope"));
            result.Errors.Where(e => e.PropertyName == nameof(OutputAction.Url)).Should().ContainSingle();
        }

        // ------------------------------------------------- send time, with DNS ----

        private static FunctionOutboundGuard Resolving(params string[] addresses)
            => new((_, _) => Task.FromResult(addresses.Select(IPAddress.Parse).ToArray()));

        [Fact]
        public async Task A_name_resolving_public_is_allowed()
        {
            var act = () => Resolving("93.184.216.34").EnsureAllowedAsync(new Uri("https://hooks.example.com/x"));
            await act.Should().NotThrowAsync();
        }

        [Theory]
        [InlineData("10.0.0.1")]
        [InlineData("::ffff:10.0.0.1")]
        [InlineData("fd12::1")]
        public async Task A_name_resolving_inward_is_blocked(string address)
        {
            var act = () => Resolving(address).EnsureAllowedAsync(new Uri("https://hooks.example.com/x"));
            await act.Should().ThrowAsync<OutboundTargetBlockedException>();
        }

        [Fact]
        public async Task A_name_resolving_to_nothing_is_a_connection_failure_not_a_refusal()
        {
            var act = () => Resolving().EnsureAllowedAsync(new Uri("https://hooks.example.com/x"));
            await act.Should().ThrowAsync<SocketException>();
        }

        [Fact]
        public async Task A_dns_failure_propagates_as_a_connection_failure()
        {
            var guard = new FunctionOutboundGuard((_, _) => throw new SocketException((int)SocketError.HostNotFound));
            var act = () => guard.EnsureAllowedAsync(new Uri("https://nope.example.com/x"));
            await act.Should().ThrowAsync<SocketException>();
        }

        [Fact]
        public async Task A_literal_ip_never_asks_dns()
        {
            var asked = false;
            var guard = new FunctionOutboundGuard((_, _) => { asked = true; return Task.FromResult(Array.Empty<IPAddress>()); });

            await guard.EnsureAllowedAsync(new Uri("https://93.184.216.34/x"));

            asked.Should().BeFalse();
        }

        // ------------------------------------------------ the real handler, real sockets ----

        [Fact]
        public void The_primary_handler_follows_no_redirects_uses_no_proxy_and_connects_through_the_guard()
        {
            using var handler = FunctionOutboundGuard.CreatePrimaryHandler(new FunctionOutboundGuard());

            handler.AllowAutoRedirect.Should().BeFalse();
            handler.UseProxy.Should().BeFalse();
            handler.ConnectCallback.Should().NotBeNull();
        }

        [Fact]
        public async Task Dns_rebinding_to_loopback_is_refused_at_connect_and_no_connection_is_made()
        {
            // The pre-flight check would have seen a public answer; this is the connection's own
            // resolution returning loopback. The listener must never see a connection.
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var guard = Resolving("127.0.0.1");
            using var client = new HttpClient(FunctionOutboundGuard.CreatePrimaryHandler(guard));

            var act = () => client.GetAsync($"http://rebind.example.com:{port}/");

            var thrown = await act.Should().ThrowAsync<HttpRequestException>();
            thrown.Which.InnerException.Should().BeOfType<OutboundTargetBlockedException>();
            listener.Pending().Should().BeFalse();
        }

        [Fact]
        public async Task The_connection_goes_to_the_vetted_address_and_a_redirect_to_it_is_not_followed()
        {
            // With the address rule relaxed (the only way to have a live peer on a test box is
            // loopback), prove the callback connects to what it resolved and that the real
            // handler reports a 302 instead of following it to the metadata service.
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var connections = 0;

            var server = Task.Run(async () =>
            {
                using var socket = await listener.AcceptSocketAsync();
                Interlocked.Increment(ref connections);
                var buffer = new byte[4096];
                await socket.ReceiveAsync(buffer, SocketFlags.None);
                var response = "HTTP/1.1 302 Found\r\nLocation: http://169.254.169.254/latest/meta-data/\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                await socket.SendAsync(Encoding.ASCII.GetBytes(response), SocketFlags.None);
                socket.Shutdown(SocketShutdown.Both);
            });

            var guard = new FunctionOutboundGuard(
                (_, _) => Task.FromResult(new[] { IPAddress.Loopback }), isBlocked: a => a.Equals(IPAddress.Parse("169.254.169.254")));
            using var client = new HttpClient(FunctionOutboundGuard.CreatePrimaryHandler(guard));

            using var reply = await client.GetAsync($"http://hooks.example.com:{port}/");
            await server;

            reply.StatusCode.Should().Be(HttpStatusCode.Found);
            connections.Should().Be(1);
        }
    }
}
