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
    /// Monaco (functions code editor, workflow code fields) is loaded by @monaco-editor/loader
    /// from this pinned CDN path, with its CSS, codicon font and workers. A path source, not the
    /// whole jsdelivr host: the host serves every npm package, so allowing it would let an
    /// injected tag load any script. If the loader version in client/package-lock.json changes,
    /// this must change with it (the code editor stays blank otherwise).
    /// </summary>
    public const string MonacoSource = "https://cdn.jsdelivr.net/npm/monaco-editor@0.55.1/";

    /// <summary>reCAPTCHA on the login/sign-up forms (client/app/components/captcha).</summary>
    private const string RecaptchaScriptSources = "https://www.google.com/recaptcha/ https://www.gstatic.com/recaptcha/";
    private const string RecaptchaFrameSource = "https://www.google.com/recaptcha/";

    /// <summary>
    /// Fixed third parties the SPA calls: Rollbar (genesis-os error reporting) and Azure Blob
    /// (workflow import PUTs the file to a pre-signed URL; the account differs per environment).
    /// </summary>
    private static readonly string[] FixedConnectSources =
    [
        "https://api.rollbar.com",
        "https://*.blob.core.windows.net",
    ];

    public static string Build(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var runtime = configuration.GetSection("FrontendRuntime");
        var csp = configuration.GetSection("Csp");

        return BuildPolicy(
            connectSrc: Origins(runtime, ConnectOriginKeys)
                .Concat(Origins(runtime, WebSocketOriginKeys).Select(ToWebSocketOrigin))
                .Concat(Split(csp["ExtraConnectSrc"])),
            formAction: Origins(runtime, FormActionOriginKeys).Concat(Split(csp["ExtraFormAction"])));
    }

    /// <summary>
    /// The policy itself, separated from configuration so it can be asserted directly.
    /// </summary>
    public static string BuildPolicy(IEnumerable<string?> connectSrc, IEnumerable<string?> formAction)
    {
        var connect = Normalize(connectSrc);
        var form = Normalize(formAction);

        return string.Join(
            " ",
            "default-src 'self';",
            // Runtime config is the external /runtime-config.js, so no inline script is needed.
            $"script-src 'self' {MonacoSource} {RecaptchaScriptSources};",
            // genesis-os pages, Radix, xyflow and Monaco inject <style> elements and style
            // attributes at runtime (see blocks-brain lessons 2026-10-06, monitor login blank).
            $"style-src 'self' 'unsafe-inline' https://fonts.googleapis.com {MonacoSource};",
            // Images cannot run script; avatars and icons come from per-environment storage hosts.
            "img-src 'self' data: blob: https:;",
            $"font-src 'self' data: https://fonts.gstatic.com {MonacoSource};",
            $"connect-src 'self' {string.Join(" ", FixedConnectSources)}{Suffix(connect)};",
            // Monaco runs its language workers from blob: bootstraps.
            "worker-src 'self' blob:;",
            $"frame-src {RecaptchaFrameSource};",
            "frame-ancestors 'none';",
            "base-uri 'self';",
            "object-src 'none';",
            $"form-action 'self'{Suffix(form)}");
    }

    /// <summary>Reduce configured values to distinct, sorted origins.</summary>
    private static List<string> Normalize(IEnumerable<string?> values) =>
        values
            .Select(ToOrigin)
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
