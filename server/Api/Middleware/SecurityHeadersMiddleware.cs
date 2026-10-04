namespace BlocksTemplate.Api.Middleware;

/// <summary>
/// Browser security headers for SPA + API (OWASP ZAP DAST bar).
/// Env bootstrap via /runtime-config.js; style-src keeps unsafe-inline for Radix/emotion.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        Apply(context);
        context.Response.OnStarting(static state =>
        {
            Apply((HttpContext)state!);
            return Task.CompletedTask;
        }, context);

        await _next(context);
    }

    internal static void Apply(HttpContext context)
    {
        var headers = context.Response.Headers;
        var path = context.Request.Path.Value ?? string.Empty;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

        if (!headers.ContainsKey("Content-Security-Policy"))
        {
            headers["Content-Security-Policy"] = BuildCsp();
        }

        if (!headers.ContainsKey("Cache-Control"))
        {
            ApplyCacheControl(headers, path);
        }
    }

    private static void ApplyCacheControl(IHeaderDictionary headers, string path)
    {
        if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
            || path == "/"
            || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("runtime-config.js", StringComparison.OrdinalIgnoreCase)
            || !Path.HasExtension(path))
        {
            headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            headers["Pragma"] = "no-cache";
        }
        else if (path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
        {
            headers["Cache-Control"] = "public, max-age=31536000, immutable";
        }
    }

    private static string BuildCsp()
    {
        // Wildcard covers shared + PR preview hosts (dev-logic-293, etc.).
        const string connectHosts =
            "https://*.blocksdevelopers.com " +
            "wss://*.blocksdevelopers.com " +
            "https://*.seliseblocks.com " +
            "wss://*.seliseblocks.com " +
            "https://blocksdev.blob.core.windows.net " +
            "https://az-cdn.selise.biz " +
            "https://api.rollbar.com " +
            "https://code.selise.biz";

        return
            "default-src 'self' blob:; " +
            "script-src 'self'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: blob: https://blocksdev.blob.core.windows.net https://az-cdn.selise.biz; " +
            "font-src 'self' data:; " +
            "connect-src 'self' " + connectHosts + "; " +
            "frame-ancestors 'none'; " +
            "base-uri 'self'; " +
            "object-src 'none'; " +
            "form-action 'self' https://*.blocksdevelopers.com https://*.seliseblocks.com";
    }
}
