using Microsoft.Extensions.Configuration;

namespace BlocksTemplate.Api.Security;

/// <summary>
/// Builds the SPA's Content-Security-Policy from configuration.
/// <para>
/// The origins come from the same <c>FrontendRuntime</c> section that fills the SPA's runtime
/// config, so the policy describes whatever environment the host is actually running in (one
/// image runs on dev, stg and prod). Same pattern as blocks-os / blocks-iam / blocks-data
/// <c>server/Api/Security/ContentSecurityPolicy.cs</c>. Anything no runtime key describes goes in
/// <c>Csp:ExtraConnectSrc</c> / <c>Csp:ExtraFormAction</c> rather than back into code.
/// </para>
/// </summary>
public static class ContentSecurityPolicy
{
    /// <summary><c>FrontendRuntime</c> keys holding an origin the SPA makes requests to.</summary>
    internal static readonly string[] ConnectOriginKeys =
    [
        "BLOCKS_API_BASE_URL",
        "BLOCKS_IAM_BASE_URL",
        "BLOCKS_IDP_BASE_URL",
        "BLOCKS_CONSTRUCT_URL",
        "BLOCKS_LOCALIZATION_BASE_URL",
        "BLOCKS_AGENTS_BASE_URL",
        "BLOCKS_DATA_BASE_URL",
        "BLOCKS_UTILITIES_BASE_URL",
        "BLOCKS_LOGIC_BASE_URL",
        "BLOCKS_MONITOR_BASE_URL",
        "BLOCKS_RELEASE_BASE_URL",
        "BLOCKS_STUDIO_BASE_URL",
        "BLOCKS_OS_BASE_URL",
        "BLOCKS_PUBLIC_API_BASE_URL",
    ];

    /// <summary>
    /// Hosts the SPA opens a WebSocket to: the notification hub. CSP does not let an
    /// <c>https:</c> source match a <c>wss:</c> URL, so the <c>wss:</c> twin is emitted too.
    /// </summary>
    internal static readonly string[] WebSocketOriginKeys =
    [
        "BLOCKS_LOGIC_BASE_URL",
    ];

    /// <summary>Where a login POST may be sent: the identity host and the portal.</summary>
    internal static readonly string[] FormActionOriginKeys =
    [
        "BLOCKS_IAM_BASE_URL",
        "BLOCKS_IDP_BASE_URL",
        "BLOCKS_OS_BASE_URL",
    ];

    /// <summary>
    /// Where the browser sends CSP violation reports (<see cref="CspReportEndpoint"/>). Same
    /// origin, outside <c>/api</c> so tenant validation and auth do not reject the browser's
    /// anonymous POST.
    /// </summary>
    public const string ReportPath = "/csp-report";

    /// <summary>Reporting API group name used by <c>report-to</c> and <c>Reporting-Endpoints</c>.</summary>
    public const string ReportGroup = "csp-endpoint";

    /// <summary>
    /// Used only when <c>Csp:BlobStorageSrc</c> is not set: workflow import PUTs the file to a
    /// pre-signed Azure Blob URL and the storage account differs per environment, so an
    /// unconfigured environment keeps working. Set the exact account(s) to drop the wildcard.
    /// </summary>
    public const string BlobStorageFallback = "https://*.blob.core.windows.net";

    /// <summary>Fixed third parties the SPA calls: Rollbar (genesis-os error reporting).</summary>
    private static readonly string[] FixedConnectSources =
    [
        "https://api.rollbar.com",
    ];

    public static string Build(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var runtime = configuration.GetSection("FrontendRuntime");
        var csp = configuration.GetSection("Csp");

        // Storage holds both workflow-import uploads (connect) and avatars/logos (img).
        var blobStorage = Normalize(Split(csp["BlobStorageSrc"]));
        IEnumerable<string?> blob = blobStorage.Count > 0 ? blobStorage : [BlobStorageFallback];

        return BuildPolicy(
            connectSrc: Origins(runtime, ConnectOriginKeys)
                .Concat(Origins(runtime, WebSocketOriginKeys).Select(ToWebSocketOrigin))
                .Concat(blob)
                .Concat(Split(csp["ExtraConnectSrc"])),
            formAction: Origins(runtime, FormActionOriginKeys).Concat(Split(csp["ExtraFormAction"])),
            imgSrc: blob.Concat(Split(csp["ExtraImgSrc"])));
    }

    /// <summary>
    /// The policy itself, separated from configuration so it can be asserted directly.
    /// </summary>
    public static string BuildPolicy(
        IEnumerable<string?> connectSrc,
        IEnumerable<string?> formAction,
        IEnumerable<string?>? imgSrc = null)
    {
        var connect = Normalize(connectSrc);
        var form = Normalize(formAction);
        var img = Normalize(imgSrc ?? []);

        return string.Join(
            " ",
            "default-src 'self';",
            // Runtime config is the external /runtime-config.js, so no inline script is needed.
            // Monaco is served from this origin (/monaco/vs, client/app/lib/monaco-loader.ts).
            "script-src 'self';",
            // genesis-os pages, Radix, xyflow and Monaco inject <style> elements and style
            // attributes at runtime (see blocks-brain lessons 2026-10-06, monitor login blank).
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com;",
            // Same shape as os/iam/data/monitor: avatars and logos come from storage hosts
            // (Csp:BlobStorageSrc) and anything else from Csp:ExtraImgSrc.
            $"img-src 'self' data: blob:{Suffix(img)};",
            "font-src 'self' data: https://fonts.gstatic.com;",
            $"connect-src 'self' {string.Join(" ", FixedConnectSources)}{Suffix(connect)};",
            // Monaco runs its language workers from blob: bootstraps.
            "worker-src 'self' blob:;",
            "frame-ancestors 'none';",
            "base-uri 'self';",
            "object-src 'none';",
            $"form-action 'self'{Suffix(form)};",
            // report-to for current browsers (Reporting-Endpoints header, SecurityHeaders),
            // report-uri for Firefox, which has no report-to yet.
            $"report-to {ReportGroup};",
            $"report-uri {ReportPath}");
    }

    /// <summary>
    /// Reduce configured values to distinct, sorted origins. The blob fallback is the one
    /// wildcard kept as is (a URI parser rejects <c>*.</c> hosts, and config cannot add it).
    /// </summary>
    private static List<string> Normalize(IEnumerable<string?> values) =>
        values
            .Select(value => ReferenceEquals(value, BlobStorageFallback) ? value : ToOrigin(value))
            .Where(origin => origin is not null)
            .Select(origin => origin!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(origin => origin, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IEnumerable<string?> Origins(IConfiguration section, IEnumerable<string> keys) =>
        keys.Select(key => section[key]);

    /// <summary>
    /// A CSP source is an origin, so any path, query or trailing slash is dropped. A value that
    /// is not an absolute http(s)/ws(s) URL -- including an unfilled <c>__BLOCKS_*__</c>
    /// placeholder -- is ignored rather than emitted verbatim, so a malformed secret cannot
    /// inject a directive.
    /// </summary>
    public static string? ToOrigin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)) return null;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps
            && uri.Scheme != Uri.UriSchemeWs && uri.Scheme != Uri.UriSchemeWss) return null;

        return uri.GetLeftPart(UriPartial.Authority);
    }

    /// <summary>
    /// The wss origin a browser uses to open a socket to an https host, or null. Plain http and
    /// ws hosts get no socket allowance: the socket would carry the session token in clear text.
    /// </summary>
    public static string? ToWebSocketOrigin(string? value)
    {
        var origin = ToOrigin(value);
        if (origin is null) return null;

        if (origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return "wss://" + origin["https://".Length..];
        if (origin.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            return origin;
        return null;
    }

    /// <summary>Space-, comma- or semicolon-separated list, for origins no runtime key describes.</summary>
    public static IEnumerable<string?> Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Suffix(List<string> origins) =>
        origins.Count == 0 ? string.Empty : " " + string.Join(" ", origins);
}
