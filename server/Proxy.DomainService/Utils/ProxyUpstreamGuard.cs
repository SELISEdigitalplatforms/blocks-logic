using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// SSRF guard for the proxy's outbound target. Three entry points:
    /// <list type="bullet">
    /// <item><see cref="IsDisallowedTarget"/> &mdash; synchronous, no DNS. Rejects a non-<c>http(s)</c>
    /// scheme, a <em>literal-IP</em> host in a blocked range, and a host name that means this machine or a
    /// cloud metadata service. Called from <see cref="ProxyConfigValidator"/> on every create / update /
    /// revert so a config can never be saved that points straight at <c>127.0.0.1</c>,
    /// <c>169.254.169.254</c>, <c>metadata.google.internal</c>, RFC1918, etc.</item>
    /// <item><see cref="IProxyUpstreamGuard.IsTargetBlockedAsync"/> &mdash; resolves the host and rejects if
    /// <em>any</em> resolved address is blocked. Called by the forwarder just before the send so a blocked
    /// target gets a clear <c>UpstreamBlocked</c> outcome.</item>
    /// <item><see cref="ConnectAsync"/> &mdash; the upstream handler's <c>ConnectCallback</c>. This is the
    /// enforcement: it resolves once, vets every address, and connects to those exact addresses, so a name
    /// that answers "public" to the pre-check and "private" to the socket (DNS rebinding) cannot get through.
    /// A DNS failure here means no connection at all.</item>
    /// </list>
    /// Blocked ranges: <c>0.0.0.0/8</c>, <c>10.0.0.0/8</c>, <c>100.64.0.0/10</c>, <c>127.0.0.0/8</c>,
    /// <c>168.63.129.16</c>, <c>169.254.0.0/16</c>, <c>172.16.0.0/12</c>, <c>192.0.0.0/24</c>,
    /// <c>192.168.0.0/16</c>, <c>198.18.0.0/15</c>, <c>224.0.0.0/3</c>, <c>::1/128</c>, <c>::/128</c>,
    /// <c>fc00::/7</c>, <c>fe80::/10</c>, <c>fec0::/10</c>, <c>ff00::/8</c>, and IPv6 forms that carry an
    /// IPv4 (mapped, compatible, NAT64 <c>64:ff9b::/96</c>, 6to4 <c>2002::/16</c>) judged by that IPv4.
    /// Kept inside Proxy on purpose: Functions has its own guard for its own purpose (user, 2026-10-07).
    /// </summary>
    public interface IProxyUpstreamGuard
    {
        /// <summary>
        /// Resolves <paramref name="url"/>'s host and returns <c>true</c> when the target must not be called:
        /// a non-<c>http(s)</c> scheme, a blocked host name, or any resolved address in a blocked range. A DNS
        /// failure returns <c>false</c> here; <see cref="ProxyUpstreamGuard.ConnectAsync"/> then fails the
        /// connection, which maps to <c>UpstreamUnreachable</c>.
        /// </summary>
        Task<bool> IsTargetBlockedAsync(string url, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Thrown from <see cref="ProxyUpstreamGuard.ConnectAsync"/> when the target resolves to a blocked
    /// address. The handler wraps it in an <see cref="HttpRequestException"/>; the forwarder maps it to
    /// <c>UpstreamBlocked</c> and never retries it.
    /// </summary>
    public sealed class ProxyUpstreamBlockedException : IOException
    {
        public ProxyUpstreamBlockedException()
            : base("The upstream endpoint is not an allowed destination.")
        {
        }
    }

    /// <inheritdoc cref="IProxyUpstreamGuard"/>
    public sealed class ProxyUpstreamGuard : IProxyUpstreamGuard
    {
        /// <summary>Names that mean this machine or a cloud metadata service, whatever DNS says.</summary>
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

        public ProxyUpstreamGuard()
            : this(Dns.GetHostAddressesAsync)
        {
        }

        /// <summary>Test seam: inject a fake host resolver.</summary>
        public ProxyUpstreamGuard(Func<string, CancellationToken, Task<IPAddress[]>> resolve)
        {
            _resolve = resolve;
        }

        /// <summary>
        /// The primary handler for the upstream <see cref="HttpClient"/>. No redirects (a 3xx is relayed, not
        /// followed), no ambient proxy (the connect callback would then vet the proxy instead of the target),
        /// and every connection goes through <see cref="ConnectAsync"/>.
        /// <para>
        /// No cookie jar (PX-2): with .NET's default <c>UseCookies = true</c> one jar serves every tenant for the
        /// handler's lifetime, so a session cookie a vendor set for tenant A rode along on tenant B's calls.
        /// Blocks never keeps a vendor cookie; any <c>Set-Cookie</c> from upstream is dropped.
        /// </para>
        /// </summary>
        public static SocketsHttpHandler CreatePrimaryHandler(ProxyUpstreamGuard guard)
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
        /// Synchronous, DNS-free check for config-write validation. <c>true</c> (with a human
        /// <paramref name="reason"/>) when the target must be rejected. A value that is not an absolute URI
        /// returns <c>false</c> here so the caller's existing "absolute https:// URL" rule owns that failure.
        /// </summary>
        public static bool IsDisallowedTarget(string? url, out string reason)
        {
            reason = string.Empty;

            if (!Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                reason = "Upstream URL must use http or https.";
                return true;
            }

            if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
                && IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal)
                && IsBlockedAddress(literal))
            {
                reason = "Upstream host is a private, loopback, or link-local address, which is not allowed.";
                return true;
            }

            if (IsBlockedHostName(uri.IdnHost))
            {
                reason = "Upstream host refers to this machine or a cloud metadata service, which is not allowed.";
                return true;
            }

            return false;
        }

        public async Task<bool> IsTargetBlockedAsync(string url, CancellationToken cancellationToken = default)
        {
            if (!Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                return true;
            }

            // A literal-IP host never hits DNS.
            if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal))
            {
                return IsBlockedAddress(literal);
            }

            if (IsBlockedHostName(uri.IdnHost))
            {
                return true;
            }

            IPAddress[] addresses;
            try
            {
                addresses = await _resolve(uri.IdnHost, cancellationToken);
            }
            catch (SocketException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }

            return addresses.Length > 0 && addresses.Any(IsBlockedAddress);
        }

        /// <summary>
        /// <c>ConnectCallback</c> for the upstream handler. Resolves the host once, refuses if any address is
        /// blocked, and connects only to the addresses it just vetted &mdash; never handing the name to the
        /// socket, which would resolve it again and reopen the rebinding window.
        /// </summary>
        public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
        {
            var endpoint = context.DnsEndPoint;
            var addresses = await ResolveVettedAsync(endpoint.Host, cancellationToken);

            Exception? last = null;
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken);

                    // What the kernel says we are connected to must itself pass.
                    if (socket.RemoteEndPoint is not IPEndPoint connected || IsBlockedAddress(connected.Address))
                    {
                        throw new ProxyUpstreamBlockedException();
                    }

                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (ProxyUpstreamBlockedException)
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
        /// Resolves <paramref name="host"/> (a literal IP resolves to itself) and returns its addresses only if
        /// <i>every</i> one passes. All, not any: a name with one public and one private record is a rebinding
        /// setup. No address, or a DNS failure, throws &mdash; there is nothing safe to connect to.
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
                if (string.IsNullOrWhiteSpace(trimmed) || IsBlockedHostName(trimmed))
                {
                    throw new ProxyUpstreamBlockedException();
                }

                addresses = await _resolve(trimmed, cancellationToken);
            }

            if (addresses is null || addresses.Length == 0)
            {
                throw new SocketException((int)SocketError.HostNotFound);
            }

            if (addresses.Any(IsBlockedAddress))
            {
                throw new ProxyUpstreamBlockedException();
            }

            return addresses;
        }

        private static bool IsBlockedHostName(string? host)
        {
            var name = (host ?? string.Empty).Trim().TrimEnd('.');
            return BlockedHostNames.Contains(name)
                || name.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary><c>true</c> when <paramref name="address"/> is in any blocked range (see the type summary).</summary>
        internal static bool IsBlockedAddress(IPAddress address)
        {
            if (address is null)
            {
                return true;
            }

            // A scope id (fe80::1%eth0) is not part of the address.
            if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0)
            {
                address = new IPAddress(address.GetAddressBytes());
            }

            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            if (IPAddress.IsLoopback(address))
            {
                return true;
            }

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = address.GetAddressBytes();
                return b[0] == 0                                        // 0.0.0.0/8   "this host"
                    || b[0] == 10                                       // 10.0.0.0/8
                    || (b[0] == 100 && (b[1] & 0xC0) == 64)             // 100.64.0.0/10 carrier-grade NAT (Alibaba metadata)
                    || b[0] == 127                                      // 127.0.0.0/8 loopback
                    || (b[0] == 168 && b[1] == 63 && b[2] == 129 && b[3] == 16) // Azure WireServer
                    || (b[0] == 169 && b[1] == 254)                     // 169.254.0.0/16 link-local (incl. IMDS)
                    || (b[0] == 172 && (b[1] & 0xF0) == 16)             // 172.16.0.0/12
                    || (b[0] == 192 && b[1] == 0 && b[2] == 0)          // 192.0.0.0/24
                    || (b[0] == 192 && b[1] == 168)                     // 192.168.0.0/16
                    || (b[0] == 198 && (b[1] & 0xFE) == 18)             // 198.18.0.0/15
                    || b[0] >= 224;                                     // multicast, reserved, broadcast
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (address.IsIPv6LinkLocal)                  // fe80::/10
                {
                    return true;
                }

                if (address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6Loopback))
                {
                    return true;                              // ::/128, ::1/128
                }

                var b = address.GetAddressBytes();
                if ((b[0] & 0xFE) == 0xFC) return true;                         // fc00::/7 unique-local
                if (b[0] == 0xFF) return true;                                  // ff00::/8 multicast
                if (b[0] == 0xFE && (b[1] & 0xC0) == 0xC0) return true;         // fec0::/10 site-local

                // ::a.b.c.d (IPv4-compatible) and 64:ff9b::a.b.c.d (NAT64) carry the IPv4 in the last 32 bits.
                var embedsLast32 = b.Take(12).All(x => x == 0)
                    || (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b.Skip(4).Take(8).All(x => x == 0));
                if (embedsLast32)
                {
                    return IsBlockedAddress(new IPAddress(b.Skip(12).Take(4).ToArray()));
                }

                // 2002:AABB:CCDD::/48 (6to4) carries the IPv4 AA.BB.CC.DD.
                if (b[0] == 0x20 && b[1] == 0x02)
                {
                    return IsBlockedAddress(new IPAddress(b.Skip(2).Take(4).ToArray()));
                }
            }

            return false;
        }
    }
}
