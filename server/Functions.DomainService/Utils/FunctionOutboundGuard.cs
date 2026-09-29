using System.Net;
using System.Net.Sockets;
using Proxy.DomainService.Utils;

namespace Functions.DomainService.Utils
{
    /// <summary>
    /// The destination of an output action is not one the platform will call: a private,
    /// loopback, link-local or metadata address, or a host that resolves to one. Never retried —
    /// the same URL will be refused the same way on every attempt.
    /// </summary>
    public sealed class OutboundTargetBlockedException(string message) : Exception(message);

    /// <summary>
    /// SSRF guard for output actions, the one place a run's result leaves the platform to a URL a
    /// tenant typed. Three layers, because each one alone has a known hole:
    /// <list type="number">
    /// <item><see cref="IsDisallowedUrl"/> &mdash; synchronous, DNS-free, run by the save-time
    /// validator: rejects a literal blocked IP (in any spelling <see cref="Uri"/> canonicalises:
    /// decimal, octal, hex, IPv4-mapped IPv6) and the host names that mean "this machine" or "the
    /// cloud metadata service". A public-looking hostname passes here; DNS is not asked at save.</item>
    /// <item><see cref="EnsureAllowedAsync"/> &mdash; run by the processor before every send, on the
    /// URL <i>after</i> secret substitution (a <c>{{secret.x}}</c> can supply the host): resolves
    /// the name and refuses if <i>any</i> address is blocked, so the tenant gets a clear
    /// "destination not allowed" rather than a connection error.</item>
    /// <item><see cref="ConnectAsync"/> &mdash; the <see cref="SocketsHttpHandler.ConnectCallback"/>
    /// of the output-action client: resolves again, vets every address, and connects the socket to
    /// a vetted address itself. This is what actually closes DNS rebinding (a name that answered
    /// public to layer 2 and private to the connection) and every redirect / retry, because it
    /// runs for every connection the handler opens. Auto-redirect is off as well, see
    /// <see cref="CreatePrimaryHandler"/>.</item>
    /// </list>
    /// <para>
    /// The address ranges are <see cref="ProxyUpstreamGuard"/>'s, reused through its public
    /// <see cref="ProxyUpstreamGuard.IsDisallowedTarget"/> rather than copied, so the proxy and
    /// Functions cannot drift. Functions adds a few ranges the proxy guard does not cover and that
    /// matter for a webhook sender specifically (<see cref="IsBlockedBeyondProxyRanges"/>); those
    /// belong upstream in the shared guard eventually.
    /// </para>
    /// </summary>
    public interface IFunctionOutboundGuard
    {
        /// <summary>
        /// Resolves <paramref name="uri"/>'s host and throws <see cref="OutboundTargetBlockedException"/>
        /// when it is disallowed or any resolved address is blocked. A DNS failure is left to throw
        /// its own <see cref="SocketException"/>: that is a connection problem, not a refusal.
        /// </summary>
        Task EnsureAllowedAsync(Uri uri, CancellationToken cancellationToken = default);

        /// <summary>The <see cref="SocketsHttpHandler.ConnectCallback"/>: vets, then connects to a vetted address.</summary>
        ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IFunctionOutboundGuard"/>
    public sealed class FunctionOutboundGuard : IFunctionOutboundGuard
    {
        /// <summary>What the tenant is told. Deliberately says nothing about which address it resolved to.</summary>
        public const string BlockedMessage =
            "the destination is a private, loopback, link-local or metadata address, which output actions may not call";

        /// <summary>
        /// Names that mean "this machine" or "the metadata service" without ever reaching DNS
        /// (resolver hosts files, cloud-provider search domains). Compared after trimming a
        /// trailing dot; <c>*.localhost</c> is handled separately (RFC 6761).
        /// </summary>
        private static readonly HashSet<string> BlockedHostNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "localhost",
            "localhost.localdomain",
            "ip6-localhost",
            "ip6-loopback",
            "metadata",
            "metadata.google.internal",
            "instance-data",
            "instance-data.ec2.internal",
        };

        private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
        private readonly Func<IPAddress, bool> _isBlocked;

        public FunctionOutboundGuard()
            : this(Dns.GetHostAddressesAsync)
        {
        }

        /// <summary>Test seam: inject a fake host resolver.</summary>
        public FunctionOutboundGuard(Func<string, CancellationToken, Task<IPAddress[]>> resolve)
            : this(resolve, IsBlockedAddress)
        {
        }

        /// <summary>
        /// Test seam: also replace the address rule, so a test can prove <see cref="ConnectAsync"/>
        /// really connects to the address it vetted — which, on a test box, is loopback.
        /// </summary>
        internal FunctionOutboundGuard(
            Func<string, CancellationToken, Task<IPAddress[]>> resolve, Func<IPAddress, bool> isBlocked)
        {
            _resolve = resolve;
            _isBlocked = isBlocked;
        }

        /// <summary>
        /// The primary handler for the output-action <see cref="HttpClient"/>. No redirects (a 3xx
        /// is reported as the status it is — following one would be a second, unvetted request),
        /// no ambient proxy (the connect callback would then vet the proxy instead of the target),
        /// and every connection goes through <see cref="ConnectAsync"/>.
        /// </summary>
        public static SocketsHttpHandler CreatePrimaryHandler(IFunctionOutboundGuard guard)
        {
            ArgumentNullException.ThrowIfNull(guard);
            return new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                UseCookies = false,
                ConnectCallback = guard.ConnectAsync,
            };
        }

        /// <summary>
        /// Save-time check, no DNS. <c>true</c> (with a human <paramref name="reason"/>) when the URL
        /// must be rejected. A value that is not an absolute URI returns <c>false</c> so the
        /// validator's own "absolute http(s) URL" rule owns that message.
        /// </summary>
        public static bool IsDisallowedUrl(string? url, out string reason)
        {
            reason = string.Empty;

            if (!Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out var uri))
            {
                return false;
            }

            // Scheme and literal-IP ranges: the proxy's rule, verbatim.
            if (ProxyUpstreamGuard.IsDisallowedTarget(uri.AbsoluteUri, out var proxyReason))
            {
                reason = proxyReason.Replace("Upstream", "The output action", StringComparison.Ordinal);
                return true;
            }

            if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
            {
                if (!IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal) || IsBlockedAddress(literal))
                {
                    reason = "The output action host is a private, loopback, link-local or reserved address, which is not allowed.";
                    return true;
                }
                return false;
            }

            var host = uri.IdnHost.TrimEnd('.');
            if (BlockedHostNames.Contains(host) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            {
                reason = "The output action host refers to this machine or a metadata service, which is not allowed.";
                return true;
            }

            // A single-label name ("redis", "mongo", "api") is a container / cluster / intranet
            // service, never a public webhook endpoint, which always has a dot in it.
            if (!host.Contains('.'))
            {
                reason = "The output action host must be a fully qualified public domain name.";
                return true;
            }

            // Anything that is only digits, dots and hex markers but that Uri did not accept as an
            // IPv4 literal (e.g. "0x7f.1.2.3.4") is still an address to getaddrinfo on some
            // platforms. No public domain looks like that, so it is refused outright.
            if (LooksNumeric(host))
            {
                reason = "The output action host looks like a numeric address in a non-standard form, which is not allowed.";
                return true;
            }

            return false;
        }

        public async Task EnsureAllowedAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(uri);

            if (IsDisallowedUrl(uri.AbsoluteUri, out _))
            {
                throw new OutboundTargetBlockedException(BlockedMessage);
            }

            await ResolveVettedAsync(uri.IdnHost, cancellationToken);
        }

        public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
        {
            var endpoint = context.DnsEndPoint;
            var addresses = await ResolveVettedAsync(endpoint.Host, cancellationToken);

            // Connect to the addresses just vetted — never hand the host name to the socket, which
            // would resolve it again and reopen the rebinding window this callback exists to close.
            Exception? last = null;
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken);

                    // Belt and braces: what the kernel says we are connected to must itself pass.
                    if (socket.RemoteEndPoint is not IPEndPoint connected || _isBlocked(connected.Address))
                    {
                        throw new OutboundTargetBlockedException(BlockedMessage);
                    }

                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (OutboundTargetBlockedException)
                {
                    socket.Dispose();
                    throw;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    socket.Dispose();
                    last = ex;
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }

            throw last ?? new SocketException((int)SocketError.HostNotFound);
        }

        /// <summary>
        /// Resolves <paramref name="host"/> (a literal IP resolves to itself) and returns its
        /// addresses only if <i>every</i> one passes. All, not any: a name with one public and one
        /// private record is a rebinding setup, and the handler's own happy-eyeballs choice of
        /// which record to use is not something to bet the metadata service on.
        /// </summary>
        private async Task<IPAddress[]> ResolveVettedAsync(string host, CancellationToken cancellationToken)
        {
            var trimmed = (host ?? string.Empty).Trim('[', ']');

            IPAddress[] addresses;
            if (IPAddress.TryParse(trimmed, out var literal))
            {
                addresses = [literal];
            }
            else
            {
                if (string.IsNullOrWhiteSpace(trimmed) || IsDisallowedUrl($"http://{trimmed}/", out _))
                {
                    throw new OutboundTargetBlockedException(BlockedMessage);
                }
                addresses = await _resolve(trimmed, cancellationToken);
            }

            if (addresses is null || addresses.Length == 0)
            {
                throw new SocketException((int)SocketError.HostNotFound);
            }

            if (addresses.Any(_isBlocked))
            {
                throw new OutboundTargetBlockedException(BlockedMessage);
            }

            return addresses;
        }

        /// <summary>
        /// <c>true</c> for an address output actions must never reach: every range
        /// <see cref="ProxyUpstreamGuard"/> blocks, plus <see cref="IsBlockedBeyondProxyRanges"/>.
        /// Fails closed — an address that cannot be expressed as a URL literal is blocked.
        /// </summary>
        public static bool IsBlockedAddress(IPAddress address)
        {
            if (address is null)
            {
                return true;
            }

            // A scope id (fe80::1%eth0) is not part of the address and does not survive a URL
            // literal; drop it rather than failing the parse below open.
            if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0)
            {
                address = new IPAddress(address.GetAddressBytes());
            }

            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            var literal = address.AddressFamily == AddressFamily.InterNetworkV6
                ? $"http://[{address}]/"
                : $"http://{address}/";

            if (!Uri.TryCreate(literal, UriKind.Absolute, out _))
            {
                return true;
            }

            return ProxyUpstreamGuard.IsDisallowedTarget(literal, out _) || IsBlockedBeyondProxyRanges(address);
        }

        /// <summary>
        /// Ranges the shared proxy guard does not block but a server-side webhook sender must:
        /// <list type="bullet">
        /// <item><c>100.64.0.0/10</c> carrier-grade NAT, used for cloud-internal endpoints (e.g. Alibaba metadata at 100.100.100.200).</item>
        /// <item><c>168.63.129.16</c> Azure WireServer / platform DNS &mdash; a public-looking address reachable only from inside Azure.</item>
        /// <item><c>192.0.0.0/24</c>, <c>198.18.0.0/15</c>, <c>224.0.0.0/4</c> multicast, <c>240.0.0.0/4</c> reserved and broadcast.</item>
        /// <item>IPv6 multicast <c>ff00::/8</c>, deprecated site-local <c>fec0::/10</c>, and IPv6
        /// forms that embed an IPv4 address &mdash; IPv4-compatible <c>::a.b.c.d</c>, NAT64
        /// <c>64:ff9b::/96</c> and 6to4 <c>2002::/16</c> &mdash; judged by the IPv4 they carry.</item>
        /// </list>
        /// </summary>
        internal static bool IsBlockedBeyondProxyRanges(IPAddress address)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = address.GetAddressBytes();
                return (b[0] == 100 && (b[1] & 0xC0) == 64)            // 100.64.0.0/10
                    || (b[0] == 168 && b[1] == 63 && b[2] == 129 && b[3] == 16)
                    || (b[0] == 192 && b[1] == 0 && b[2] == 0)         // 192.0.0.0/24
                    || (b[0] == 198 && (b[1] & 0xFE) == 18)            // 198.18.0.0/15
                    || b[0] >= 224;                                    // multicast, reserved, broadcast
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var b = address.GetAddressBytes();
                if (b[0] == 0xFF) return true;                                  // ff00::/8
                if (b[0] == 0xFE && (b[1] & 0xC0) == 0xC0) return true;         // fec0::/10

                // ::a.b.c.d (first 96 bits zero) and 64:ff9b::a.b.c.d carry the IPv4 in the last 32 bits.
                var embedsLast32 = b.Take(12).All(x => x == 0)
                    || (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b.Skip(4).Take(8).All(x => x == 0));
                if (embedsLast32)
                {
                    return IsBlockedAddress(new IPAddress(b.Skip(12).Take(4).ToArray()));
                }

                // 2002:AABB:CCDD::/48 carries the IPv4 AA.BB.CC.DD.
                if (b[0] == 0x20 && b[1] == 0x02)
                {
                    return IsBlockedAddress(new IPAddress(b.Skip(2).Take(4).ToArray()));
                }
            }

            return false;
        }

        private static bool LooksNumeric(string host) =>
            host.All(c => char.IsAsciiHexDigit(c) || c is '.' or 'x' or 'X')
            && host.Split('.').All(part => part.Length > 0 && (part.All(char.IsAsciiDigit)
                || (part.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && part.Length > 2)));
    }
}
