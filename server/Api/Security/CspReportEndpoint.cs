using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BlocksTemplate.Api.Security;

/// <summary>
/// Receives the browser's CSP violation reports (<c>report-to</c> / <c>report-uri</c>) and logs
/// them, so a policy that blocks something real shows up in the logs instead of only in a
/// user's console.
/// <para>
/// The endpoint is anonymous by nature (browsers send no credentials), so it trusts nothing:
/// the body is capped, only report content types are read, control characters are stripped
/// before logging, and logging is capped per minute so a flood cannot fill the logs. It always
/// answers 204 for an accepted report and never echoes input.
/// </para>
/// </summary>
public static class CspReportEndpoint
{
    /// <summary>A real report is well under 2 KB; a batch of a few stays under this.</summary>
    public const int MaxBodyBytes = 16 * 1024;

    /// <summary>What is kept of one body in the log line.</summary>
    public const int MaxLoggedChars = 2048;

    /// <summary>Log lines per minute per process; the rest are counted, not logged.</summary>
    public const int MaxLogsPerMinute = 30;

    private static readonly string[] AcceptedContentTypes =
    [
        "application/reports+json", // report-to (Reporting API)
        "application/csp-report",   // report-uri (legacy, Firefox)
        "application/json",
    ];

    private static readonly object Gate = new();
    private static long _windowMinute;
    private static int _loggedInWindow;
    private static int _droppedInWindow;

    public static bool Matches(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method) &&
        context.Request.Path.Equals(ContentSecurityPolicy.ReportPath, StringComparison.OrdinalIgnoreCase);

    public static async Task HandleAsync(HttpContext context, ILogger logger, Func<DateTimeOffset>? now = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        var request = context.Request;
        var mediaType = (request.ContentType ?? "").Split(';')[0].Trim();
        if (!AcceptedContentTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }

        if (request.ContentLength > MaxBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        // Read at most one byte past the cap, so a body without Content-Length (chunked) is
        // still refused without buffering it all.
        var buffer = new byte[MaxBodyBytes + 1];
        var read = 0;
        int n;
        while (read < buffer.Length &&
               (n = await request.Body.ReadAsync(buffer.AsMemory(read), context.RequestAborted)) > 0)
        {
            read += n;
        }

        if (read > MaxBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        context.Response.StatusCode = StatusCodes.Status204NoContent;

        if (read == 0 || !TakeLogSlot((now ?? (() => DateTimeOffset.UtcNow))(), out var dropped)) return;

        logger.LogWarning(
            "CSP violation report ({ContentType}, {DroppedSinceLast} dropped by rate cap): {Report}",
            mediaType, dropped, Sanitize(Encoding.UTF8.GetString(buffer, 0, read)));
    }

    /// <summary>One shared budget per minute. Returns how many reports were dropped before this one.</summary>
    public static bool TakeLogSlot(DateTimeOffset now, out int droppedBefore)
    {
        var minute = now.ToUnixTimeSeconds() / 60;
        lock (Gate)
        {
            if (minute != _windowMinute)
            {
                _windowMinute = minute;
                _loggedInWindow = 0;
                droppedBefore = _droppedInWindow;
                _droppedInWindow = 0;
            }
            else
            {
                droppedBefore = 0;
            }

            if (_loggedInWindow >= MaxLogsPerMinute)
            {
                _droppedInWindow++;
                return false;
            }

            _loggedInWindow++;
            return true;
        }
    }

    /// <summary>Strip control characters (no forged log lines) and cut to <see cref="MaxLoggedChars"/>.</summary>
    public static string Sanitize(string body)
    {
        var builder = new StringBuilder(Math.Min(body.Length, MaxLoggedChars));
        foreach (var c in body)
        {
            if (builder.Length >= MaxLoggedChars) break;
            builder.Append(char.IsControl(c) ? ' ' : c);
        }

        return builder.ToString();
    }

    /// <summary>Test hook: start every test from an empty budget.</summary>
    public static void ResetForTests()
    {
        lock (Gate)
        {
            _windowMinute = 0;
            _loggedInWindow = 0;
            _droppedInWindow = 0;
        }
    }
}
