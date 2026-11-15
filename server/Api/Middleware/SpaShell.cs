using BlocksTemplate.Api.Security;
using Microsoft.AspNetCore.Http;
using Path = System.IO.Path;

namespace BlocksTemplate.Api.Middleware;

/// <summary>
/// Serves the SPA shell (index.html) with the response's CSP style nonce (meta[name=csp-nonce]).
/// The shell is rendered from a template read once at startup instead of being sent verbatim by
/// the static file middleware, so it carries the same nonce as the response's header
/// (<see cref="StyleNonce.ItemKey"/>, set by <see cref="SecurityHeaders.InvokeAsync"/>).
/// <para>
/// Middleware rather than MapFallback. A fallback endpoint matches every path on every method,
/// which makes it a valid candidate for a request that reached a real API route with the wrong
/// verb, and routing then prefers it over answering 405: POST to a [HttpGet] action, and PUT to
/// /api/fn/{id}, both came back 200 with index.html. Constraining the fallback's route to exclude
/// /api turns those 405s into 404s and stops "/" matching at all. Serving the SPA from middleware
/// leaves /api/** entirely to the router, so a wrong method is a 405 and an unknown API route is
/// a 404, while every other path that nothing else answers gets the shell.
/// </para>
/// </summary>
internal static class SpaShell
{
    /// <summary>The template, or null when the host has no built SPA (API-only runs).</summary>
    public static async Task<string?> ReadTemplateAsync(string? webRootPath)
    {
        var indexHtml = Path.Combine(webRootPath ?? "", "index.html");
        return File.Exists(indexHtml) ? await File.ReadAllTextAsync(indexHtml).ConfigureAwait(false) : null;
    }

    /// <summary>The methods the shell answers: GET and HEAD.</summary>
    public static bool IsPageMethod(string method) => HttpMethods.IsGet(method) || HttpMethods.IsHead(method);

    /// <summary>
    /// A read of the shell itself ("/" or "/index.html"), which the static file middleware would
    /// otherwise send verbatim, without the nonce.
    /// </summary>
    public static bool IsRootRequest(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsPageMethod(request.Method)) return false;
        var path = request.Path.Value;
        return path is "/" or "" or null || string.Equals(path, "/index.html", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when the last path segment has a dot. A missing asset must stay a 404: answering a
    /// missing .js chunk with HTML turns a deploy problem into a parse error in the browser.
    /// </summary>
    public static bool LooksLikeAFile(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value)) return false;

        var lastSegment = value.AsSpan(value.LastIndexOf('/') + 1);
        return lastSegment.Contains('.');
    }

    /// <summary>
    /// Renders the root shell; for any other GET or HEAD outside /api that is not a file and
    /// that nothing downstream answered (404 before the response started), renders the shell
    /// as the SPA fallback. Everything else passes through unchanged.
    /// </summary>
    public static async Task InvokeAsync(HttpContext context, RequestDelegate next, string template)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (IsRootRequest(context.Request))
        {
            await WriteAsync(context, template).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);

        if (IsFallbackCandidate(context.Request) &&
            context.Response.StatusCode == StatusCodes.Status404NotFound &&
            !context.Response.HasStarted)
        {
            context.Response.Clear();
            await WriteAsync(context, template).ConfigureAwait(false);
        }
    }

    private static bool IsFallbackCandidate(HttpRequest request) =>
        IsPageMethod(request.Method) &&
        !request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) &&
        !LooksLikeAFile(request.Path);

    /// <summary>Writes the shell with the response's nonce as text/html.</summary>
    public static Task WriteAsync(HttpContext context, string template)
    {
        ArgumentNullException.ThrowIfNull(context);
        var nonce = (string)context.Items[StyleNonce.ItemKey]!;
        context.Response.ContentType = "text/html; charset=utf-8";
        return context.Response.WriteAsync(ContentSecurityPolicy.RenderIndex(template, nonce));
    }
}
