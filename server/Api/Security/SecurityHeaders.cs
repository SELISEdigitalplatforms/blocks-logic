using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace BlocksTemplate.Api.Security;

/// <summary>How the CSP header is sent. Set by <c>Csp:Mode</c> (env var <c>Csp__Mode</c>).</summary>
public enum CspMode
{
    /// <summary>Default. The browser blocks what the policy does not allow.</summary>
    Enforce,

    /// <summary>
    /// Kill switch without a rebuild: sent as Content-Security-Policy-Report-Only, so the
    /// browser only logs violations in the console and blocks nothing.
    /// </summary>
    ReportOnly,

    /// <summary>No CSP header at all. The other security headers stay.</summary>
    Off,
}

/// <summary>
/// Browser-facing security headers for the SPA, its static files and the API.
/// <para>
/// Never overwrites a header the response already carries: proxy gateway and public function
/// answers set their own <c>Content-Security-Policy: sandbox</c>, <c>X-Content-Type-Options</c>
/// and <c>Cache-Control</c> (ProxiesController.AddGatewaySecurityHeaders,
/// FunctionHttpResponseMapper), and those must reach the client unchanged.
/// </para>
/// <para>
/// The SPA policy and frame/permissions headers go only on pages and static files, not on
/// <c>/api</c> (JSON and user-defined function responses) or <c>/swagger</c> (its own UI).
/// </para>
/// </summary>
public static class SecurityHeaders
{
    public static CspMode ReadMode(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var value = configuration["Csp:Mode"];

        // Unknown or empty values enforce: a typo must not quietly switch protection off.
        return Enum.TryParse<CspMode>(value, ignoreCase: true, out var mode) && Enum.IsDefined(mode)
            ? mode
            : CspMode.Enforce;
    }

    public static void Apply(HttpContext context, string policy, CspMode mode)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(policy);

        var headers = context.Response.Headers;
        var path = context.Request.Path;

        SetIfAbsent(headers, "X-Content-Type-Options", "nosniff");
        SetIfAbsent(headers, "Strict-Transport-Security", "max-age=31536000; includeSubDomains");

        if (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SetIfAbsent(headers, "X-Frame-Options", "DENY");
        SetIfAbsent(headers, "Referrer-Policy", "strict-origin-when-cross-origin");
        SetIfAbsent(headers, "Permissions-Policy", "camera=(), microphone=(), geolocation=()");

        if (!headers.ContainsKey("Content-Security-Policy") &&
            !headers.ContainsKey("Content-Security-Policy-Report-Only"))
        {
            switch (mode)
            {
                case CspMode.Enforce:
                    headers["Content-Security-Policy"] = policy;
                    break;
                case CspMode.ReportOnly:
                    headers["Content-Security-Policy-Report-Only"] = policy;
                    break;
            }

            // The group the policy's report-to names. Same origin, so relative is enough.
            if (mode != CspMode.Off)
            {
                SetIfAbsent(headers, "Reporting-Endpoints",
                    $"{ContentSecurityPolicy.ReportGroup}=\"{ContentSecurityPolicy.ReportPath}\"");
            }
        }

        var value = path.Value ?? "";
        if (value.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
        {
            // Content-hashed Vite output never changes under the same name.
            SetIfAbsent(headers, "Cache-Control", "public, max-age=31536000, immutable");
        }
        else if (value.EndsWith("/runtime-config.js", StringComparison.OrdinalIgnoreCase) ||
                 value.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                 !System.IO.Path.HasExtension(value))
        {
            // The SPA shell and runtime config change on deploy/config change without a new name.
            SetIfAbsent(headers, "Cache-Control", "no-cache");
        }
    }

    private static void SetIfAbsent(IHeaderDictionary headers, string name, string value)
    {
        if (!headers.ContainsKey(name)) headers[name] = value;
    }
}
