using System.Net;
using System.Net.Sockets;

namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// SSRF guard for the proxy's outbound target. Two entry points, deliberately asymmetric:
    /// <list type="bullet">
    /// <item><see cref="IsDisallowedTarget"/> &mdash; synchronous, no DNS. Rejects a non-<c>http(s)</c>
    /// scheme and a <em>literal-IP</em> host that sits in a private / loopback / link-local range. Called
    /// from <see cref="ProxyConfigValidator"/> on every create / update / revert so a config can never be
    /// saved that points straight at <c>127.0.0.1</c>, <c>169.254.169.254</c>, RFC1918, etc.</item>
    /// <item><see cref="IProxyUpstreamGuard.IsTargetBlockedAsync"/> &mdash; resolves the host and rejects if
    /// <em>any</em> resolved address is in those ranges. Called by the forwarder immediately before the send
    /// as a DNS-rebinding defence (a hostname that resolved public at write time but now points inward).</item>
    /// </list>
    /// Blocked ranges: <c>0.0.0.0/8</c>, <c>10.0.0.0/8</c>, <c>127.0.0.0/8</c>, <c>169.254.0.0/16</c>,
    /// <c>172.16.0.0/12</c>, <c>192.168.0.0/16</c>, <c>::1/128</c>, <c>::/128</c>, <c>fc00::/7</c>,
    /// <c>fe80::/10</c>, and IPv4-mapped forms of the above.
    /// </summary>
    public interface IProxyUpstreamGuard
    {
        /// <summary>
        /// Resolves <paramref name="url"/>'s host and returns <c>true</c> when the target must not be called:
        /// a non-<c>http(s)</c> scheme, or any resolved address in a blocked range. A DNS failure returns
        /// <c>false</c> &mdash; the send is left to fail on its own and map to <c>UpstreamUnreachable</c>.
        /// </summary>
        Task<bool> IsTargetBlockedAsync(string url, CancellationToken cancellationToken = default);
    }

    /// <inheritdoc cref="IProxyUpstreamGuard"/>
    public sealed class ProxyUpstreamGuard : IProxyUpstreamGuard
    {
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

        /// <summary><c>true</c> when <paramref name="address"/> is loopback, unspecified, private, or link-local.</summary>
        internal static bool IsBlockedAddress(IPAddress address)
        {
            if (address is null)
            {
                return true;
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
                return b[0] == 0                              // 0.0.0.0/8   "this host"
                    || b[0] == 10                             // 10.0.0.0/8
                    || b[0] == 127                            // 127.0.0.0/8 loopback
                    || (b[0] == 169 && b[1] == 254)           // 169.254.0.0/16 link-local (incl. IMDS)
                    || (b[0] == 172 && (b[1] & 0xF0) == 16)   // 172.16.0.0/12
                    || (b[0] == 192 && b[1] == 168);          // 192.168.0.0/16
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
                return (b[0] & 0xFE) == 0xFC;                 // fc00::/7 unique-local
            }

            return false;
        }
    }
}
