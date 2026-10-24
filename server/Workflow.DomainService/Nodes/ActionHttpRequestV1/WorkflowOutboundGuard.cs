using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;

namespace Workflow.DomainService.Nodes.ActionHttpRequestV1
{
    /// <summary>Settings of the HTTP Request step (<c>Workflow:HttpNode</c>).</summary>
    public sealed class WorkflowHttpNodeOptions
    {
        public const string SectionName = "Workflow:HttpNode";

        /// <summary>Used when <see cref="TrustedHosts"/> is empty.</summary>
        public static readonly IReadOnlyList<string> DefaultTrustedHosts = ["seliseblocks.com", "blocksdevelopers.com"];

        /// <summary>
        /// Blocks hosts. A Blocks token (Blocks Authentication, Client Credential) is sent only to one of
        /// these or a subdomain of one. Empty = <see cref="DefaultTrustedHosts"/>.
        /// </summary>
        public List<string> TrustedHosts { get; set; } = [];
    }

    /// <summary>The HTTP Request step may not call this destination. Never retried.</summary>
    public sealed class WorkflowOutboundBlockedException : Exception
    {
        public const string DefaultMessage =
            "The URL must be http or https and must not point to a private, loopback, link-local or reserved address.";

        public WorkflowOutboundBlockedException() : base(DefaultMessage)
        {
        }
    }

    /// <summary>A Blocks token was about to be sent to a host that is not a Blocks host.</summary>
    public sealed class BlocksTokenHostNotTrustedException : Exception
    {
        public const string DefaultMessage =
            "Blocks Authentication and Client Credential send a Blocks token only to Blocks hosts. " +
            "Use a Blocks URL, or set the Authorization header yourself.";

        public BlocksTokenHostNotTrustedException() : base(DefaultMessage)
        {
        }
    }

    /// <summary>The response body is over <see cref="WorkflowOutboundGuard.MaxResponseBytes"/>.</summary>
    public sealed class ResponseTooLargeException : Exception
    {
        public const string DefaultMessage = "The response is larger than the 10 MB limit.";

        public ResponseTooLargeException() : base(DefaultMessage)
        {
        }
    }

    /// <summary>
    /// Outbound rules of the HTTP Request step. Own copy for this module (decision: modules do not share
    /// guard code). Three layers:
    /// <list type="number">
    /// <item><see cref="EnsureAllowedAsync"/> before the send: http/https only, a literal IP must pass,
    /// a host name is resolved and every address must pass.</item>
    /// <item><see cref="ConnectAsync"/> as the client's <see cref="SocketsHttpHandler.ConnectCallback"/>:
    /// resolves again, checks every address and connects to those addresses only. Runs for every
    /// connection, so it covers DNS rebinding and every redirect hop.</item>
    /// <item>Trusted (Blocks) hosts skip the address check: in-cluster DNS may answer with private IPs.
    /// A literal IP is never trusted.</item>
    /// </list>
    /// </summary>
    public interface IWorkflowOutboundGuard
    {
        /// <summary>True when <paramref name="uri"/>'s host is a trusted Blocks host or a subdomain of one.</summary>
        bool IsTrustedHost(Uri uri);

        /// <summary>Throws <see cref="WorkflowOutboundBlockedException"/> when the step may not call <paramref name="uri"/>.</summary>
        Task EnsureAllowedAsync(Uri uri, CancellationToken cancellationToken = default);

        /// <summary>The connect callback: checks, then connects to a checked address.</summary>
        ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IWorkflowOutboundGuard"/>
    public sealed class WorkflowOutboundGuard : IWorkflowOutboundGuard
    {
        /// <summary>Name of the guarded <see cref="HttpClient"/>.</summary>
        public const string HttpClientName = "WorkflowHttpRequest";

        /// <summary>Response body limit: 10 MB.</summary>
        public const long MaxResponseBytes = 10L * 1024 * 1024;

        /// <summary>Redirect hops the client follows.</summary>
        public const int MaxRedirects = 5;

        private readonly string[] _trustedHosts;
        private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
        private readonly Func<IPAddress, bool> _isBlocked;

        public WorkflowOutboundGuard(IOptions<WorkflowHttpNodeOptions> options)
            : this(options.Value.TrustedHosts, Dns.GetHostAddressesAsync, IsBlockedAddress)
        {
        }

        /// <summary>Test seam: trusted hosts, a host resolver and the address rule.</summary>
        public WorkflowOutboundGuard(
            IEnumerable<string>? trustedHosts,
            Func<string, CancellationToken, Task<IPAddress[]>> resolve,
            Func<IPAddress, bool> isBlocked)
        {
            var hosts = (trustedHosts ?? [])
                .Select(NormalizeHost)
                .Where(h => h.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            _trustedHosts = hosts.Length > 0 ? hosts : WorkflowHttpNodeOptions.DefaultTrustedHosts.ToArray();
            _resolve = resolve;
            _isBlocked = isBlocked;
        }

        /// <summary>
        /// The primary handler of <see cref="HttpClientName"/>: every connection goes through
        /// <see cref="ConnectAsync"/>, no ambient proxy (the callback would then check the proxy, not the
        /// target), no shared cookie jar, at most <see cref="MaxRedirects"/> redirect hops. On a redirect the
        /// handler drops the Authorization header itself.
        /// </summary>
        public static SocketsHttpHandler CreatePrimaryHandler(IWorkflowOutboundGuard guard)
        {
            ArgumentNullException.ThrowIfNull(guard);
            return new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = MaxRedirects,
                UseProxy = false,
                UseCookies = false,
                ConnectCallback = guard.ConnectAsync,
            };
        }

        public bool IsTrustedHost(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);
            return IsTrustedHostName(uri.HostNameType, uri.IdnHost);
        }

        public Task EnsureAllowedAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(uri);
            if (!uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new WorkflowOutboundBlockedException();
            }

            return ResolveCheckedAsync(uri.IdnHost, cancellationToken);
        }

        public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
        {
            var endpoint = context.DnsEndPoint;
            var addresses = await ResolveCheckedAsync(endpoint.Host, cancellationToken);

            // Connect to the checked addresses only. Handing the name to the socket would resolve it again.
            Exception? last = null;
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
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
        /// Resolves <paramref name="host"/> (a literal IP resolves to itself). Every address must pass,
        /// unless the host is a trusted name. A DNS failure throws its own <see cref="SocketException"/>.
        /// </summary>
        private async Task<IPAddress[]> ResolveCheckedAsync(string host, CancellationToken cancellationToken)
        {
            var trimmed = (host ?? string.Empty).Trim('[', ']');
            if (trimmed.Length == 0)
            {
                throw new WorkflowOutboundBlockedException();
            }

            if (IPAddress.TryParse(trimmed, out var literal))
            {
                return _isBlocked(literal) ? throw new WorkflowOutboundBlockedException() : [literal];
            }

            var addresses = await _resolve(trimmed, cancellationToken);
            if (addresses is null || addresses.Length == 0)
            {
                throw new SocketException((int)SocketError.HostNotFound);
            }

            if (!IsTrustedHostName(UriHostNameType.Dns, trimmed) && addresses.Any(_isBlocked))
            {
                throw new WorkflowOutboundBlockedException();
            }

            return addresses;
        }

        /// <summary>Exact match or a subdomain, on a dot boundary. A literal IP is never trusted.</summary>
        private bool IsTrustedHostName(UriHostNameType type, string host)
        {
            if (type is UriHostNameType.IPv4 or UriHostNameType.IPv6)
            {
                return false;
            }

            var name = NormalizeHost(host);
            if (name.Length == 0 || IPAddress.TryParse(name.Trim('[', ']'), out _))
            {
                return false;
            }

            return _trustedHosts.Any(t => name == t || name.EndsWith("." + t, StringComparison.Ordinal));
        }

        private static string NormalizeHost(string? host) =>
            (host ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();

        /// <summary>
        /// True for an address the step must never reach: unspecified, loopback, private (RFC 1918),
        /// link-local (incl. metadata 169.254.169.254), CGNAT, multicast, reserved, IPv6 ULA / link-local /
        /// site-local / multicast, and IPv6 forms that carry an IPv4 address (mapped, compatible, NAT64,
        /// 6to4), judged by that IPv4. Fails closed on null.
        /// </summary>
        public static bool IsBlockedAddress(IPAddress? address)
        {
            if (address is null)
            {
                return true;
            }

            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            var b = address.GetAddressBytes();
            return address.AddressFamily switch
            {
                AddressFamily.InterNetwork => IsBlockedIPv4(b),
                AddressFamily.InterNetworkV6 => IsBlockedIPv6(b),
                _ => true,
            };
        }

        private static bool IsBlockedIPv4(byte[] b) =>
            b[0] == 0                                          // 0.0.0.0/8 unspecified / "this network"
            || b[0] == 10                                      // 10.0.0.0/8
            || b[0] == 127                                     // loopback
            || (b[0] == 169 && b[1] == 254)                    // link-local, metadata
            || (b[0] == 172 && (b[1] & 0xF0) == 16)            // 172.16.0.0/12
            || (b[0] == 192 && b[1] == 168)                    // 192.168.0.0/16
            || (b[0] == 100 && (b[1] & 0xC0) == 64)            // 100.64.0.0/10 CGNAT
            || (b[0] == 192 && b[1] == 0 && b[2] == 0)         // 192.0.0.0/24 IETF protocol assignments
            || (b[0] == 198 && (b[1] & 0xFE) == 18)            // 198.18.0.0/15 benchmarking
            || (b[0] == 168 && b[1] == 63 && b[2] == 129 && b[3] == 16) // Azure platform address
            || b[0] >= 224;                                    // multicast, reserved, broadcast

        private static bool IsBlockedIPv6(byte[] b)
        {
            if (b.All(x => x == 0) || IsLoopbackIPv6(b))
            {
                return true;                                   // :: and ::1
            }

            if (b[0] == 0xFF                                   // ff00::/8 multicast
                || (b[0] & 0xFE) == 0xFC                       // fc00::/7 ULA
                || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80)     // fe80::/10 link-local
                || (b[0] == 0xFE && (b[1] & 0xC0) == 0xC0))    // fec0::/10 site-local
            {
                return true;
            }

            var embedded = EmbeddedIPv4(b);
            return embedded is not null && IsBlockedIPv4(embedded);
        }

        private static bool IsLoopbackIPv6(byte[] b) => b.Take(15).All(x => x == 0) && b[15] == 1;

        /// <summary>The IPv4 inside ::a.b.c.d, 64:ff9b::a.b.c.d or 2002:aabb:ccdd::/48; else null.</summary>
        private static byte[]? EmbeddedIPv4(byte[] b)
        {
            var compatible = b.Take(12).All(x => x == 0);
            var nat64 = b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b.Skip(4).Take(8).All(x => x == 0);
            if (compatible || nat64)
            {
                return b.Skip(12).Take(4).ToArray();
            }

            return b[0] == 0x20 && b[1] == 0x02 ? b.Skip(2).Take(4).ToArray() : null;
        }
    }

    /// <summary>Reads a response body with a size limit.</summary>
    public static class WorkflowHttpResponseReader
    {
        /// <summary>
        /// Reads <paramref name="content"/> as text, at most <paramref name="maxBytes"/> bytes.
        /// Over the limit throws <see cref="ResponseTooLargeException"/>; nothing past the limit is read.
        /// </summary>
        public static Task<string> ReadAsStringCappedAsync(HttpContent content, long maxBytes, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
            if (content.Headers.ContentLength > maxBytes)
            {
                throw new ResponseTooLargeException();
            }

            return ReadCoreAsync(content, maxBytes, cancellationToken);
        }

        private static async Task<string> ReadCoreAsync(HttpContent content, long maxBytes, CancellationToken cancellationToken)
        {
            await using var stream = await content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    throw new ResponseTooLargeException();
                }
                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }

            buffer.Position = 0;
            using var reader = new StreamReader(buffer, ResolveEncoding(content), detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(cancellationToken);
        }

        private static Encoding ResolveEncoding(HttpContent content)
        {
            var charset = content.Headers.ContentType?.CharSet?.Trim('"');
            if (string.IsNullOrWhiteSpace(charset))
            {
                return Encoding.UTF8;
            }

            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
                // Unknown charset name: read as UTF-8 rather than fail the item.
                return Encoding.UTF8;
            }
        }
    }
}
