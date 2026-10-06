using System.Text;
using System.Text.Json;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Enums;
using Functions.DomainService.Queue;

namespace Functions.DomainService.Services
{
    /// <summary>
    /// What a public caller in synchronous mode receives once the run has finished: the
    /// function's own answer as an HTTP response (sandbox/REUSE.md, "Answer mapping").
    /// <list type="bullet">
    /// <item>
    /// A result that is an object with a numeric <c>statusCode</c> between 100 and 599 is an HTTP
    /// response the function built: <c>{ statusCode, headers?, body? }</c>. A string body is sent
    /// as is, with the function's own <c>content-type</c> or <c>text/plain</c>; any other body as
    /// JSON (the function's content type if it set one, else <c>application/json</c>).
    /// </item>
    /// <item>Any other result — a number, an array, an object without a status — is
    /// <c>200 application/json</c> with that value, so a handler that just returns data works.</item>
    /// <item>A failed run is <c>502</c> with <c>{ error: { code, message } }</c>, a timed-out one
    /// <c>504</c>: the platform is the gateway here, and these are the gateway statuses.</item>
    /// </list>
    /// <para>
    /// <b>Headers pass through an allow-list</b>, never a deny-list: the response is served from
    /// the shared platform origin, so anything not known to be harmless is dropped. Allowed are
    /// the content and caching headers in <see cref="AllowedHeaders"/>, <c>Location</c> on a 3xx
    /// with an http(s) or relative target only, and <c>x-*</c> names other than the ones proxies
    /// and the platform use for identity and routing (<c>x-forwarded-*</c>, <c>x-real-ip</c>,
    /// <c>x-original-*</c>, <c>x-blocks-*</c>). <c>Set-Cookie</c> is never forwarded: a cookie set
    /// here would land on the Blocks API domain. At most <see cref="MaxHeaders"/> headers and
    /// <see cref="MaxHeaderBytes"/> of names and values pass; the rest are dropped. A name or value
    /// Kestrel would refuse (a CR/LF smuggled into a value, a non-token name) is dropped too,
    /// rather than turning a finished run into a 500.
    /// </para>
    /// <para>
    /// <b>Two headers are always added.</b> The body is tenant content served from the platform
    /// origin, so <c>X-Content-Type-Options: nosniff</c> stops a browser re-reading JSON as HTML,
    /// and <c>Content-Security-Policy: sandbox</c> puts any HTML a function returns in an opaque
    /// origin, so it cannot read the platform's storage or act as the platform page. A function's
    /// own CSP still applies on top (browsers enforce every CSP header they get).
    /// </para>
    /// </summary>
    public static class FunctionHttpResponseMapper
    {
        /// <summary>One finished HTTP answer, ready for the controller to write.</summary>
        public sealed record Response(
            int StatusCode,
            IReadOnlyList<KeyValuePair<string, string>> Headers,
            string? ContentType,
            byte[] Body);

        /// <summary>
        /// Named headers a function may set, besides <c>content-type</c> (which becomes the
        /// response's content type), <c>location</c> (3xx only) and the allowed <c>x-*</c> names.
        /// </summary>
        internal static readonly IReadOnlySet<string> AllowedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "content-language",
            "content-disposition",
            "cache-control",
            "expires",
            "last-modified",
            "etag",
            "vary",
            "retry-after",
        };

        /// <summary><c>x-*</c> prefixes proxies and the platform use for identity and routing.</summary>
        private static readonly string[] ReservedXPrefixes = ["x-forwarded-", "x-original-", "x-blocks-"];

        /// <summary>Most headers a function's answer may carry; extras are dropped.</summary>
        internal const int MaxHeaders = 32;

        /// <summary>Most bytes of header names plus values a function's answer may carry; extras are dropped.</summary>
        internal const int MaxHeaderBytes = 8 * 1024;
        internal const string JsonContentType = "application/json; charset=utf-8";
        internal const string TextContentType = "text/plain; charset=utf-8";

        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        /// <summary>Statuses that must not carry a body; Kestrel throws if one is written.</summary>
        private static readonly HashSet<int> BodylessStatuses = [204, 205, 304];

        /// <summary>
        /// Maps a finished run. Call only for a terminal status — an unfinished run is the 202 and
        /// never reaches here.
        /// </summary>
        public static Response Map(InvokeResultDto result)
        {
            ArgumentNullException.ThrowIfNull(result);

            var status = FunctionWireMapping.ToRunStatus(result.Status, out _);
            var timedOut = status == RunStatus.TimedOut
                || string.Equals(result.ErrorCode, nameof(RunErrorCode.TimedOut), StringComparison.Ordinal);

            // OUTPUT_FAILED is a function that answered and an output action that did not: the
            // caller asked for the function's answer, and it exists, so that is what it gets. The
            // failed delivery is visible on the run, which is where output actions are reported.
            if (!timedOut && status is RunStatus.Succeeded or RunStatus.OutputFailed)
            {
                return MapAnswer(result.Result, result.RunId);
            }

            return Error(
                result.RunId,
                timedOut ? 504 : 502,
                result.ErrorCode ?? (timedOut ? nameof(RunErrorCode.TimedOut) : result.Status),
                result.ErrorMessage ?? (timedOut ? "the function did not finish within its time limit" : "the function run failed"));
        }

        private static Response MapAnswer(string? resultJson, string runId)
        {
            if (string.IsNullOrWhiteSpace(resultJson))
            {
                // The handler returned undefined: still a JSON answer, the same "null" a
                // poll would report, rather than an empty 200 a client cannot parse.
                return Json(200, [], "null");
            }

            JsonElement root;
            try
            {
                using var document = JsonDocument.Parse(resultJson);
                root = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                // The runner stores JSON; anything else is passed on as text rather than lost.
                return new Response(200, SecurityHeaders(), TextContentType, Encoding.UTF8.GetBytes(resultJson));
            }

            if (!TryStatusCode(root, out var statusCode))
            {
                return Json(200, [], resultJson);
            }

            var headers = new List<KeyValuePair<string, string>>();
            string? contentType = null;
            var headerBytes = 0;
            if (root.TryGetProperty("headers", out var headerObject) && headerObject.ValueKind == JsonValueKind.Object)
            {
                foreach (var header in headerObject.EnumerateObject())
                {
                    var name = header.Name.Trim();
                    if (!IsToken(name)) continue;

                    foreach (var value in HeaderValues(header.Value))
                    {
                        if (!IsSafeValue(value)) continue;

                        if (string.Equals(name, "content-type", StringComparison.OrdinalIgnoreCase))
                        {
                            contentType ??= value;
                            continue;
                        }
                        if (!IsAllowed(name, value, statusCode)) continue;

                        // Over either cap the header is dropped, not the answer.
                        var size = name.Length + value.Length;
                        if (headers.Count >= MaxHeaders || headerBytes + size > MaxHeaderBytes) continue;

                        headerBytes += size;
                        headers.Add(new KeyValuePair<string, string>(name, value));
                    }
                }
            }

            headers.AddRange(SecurityHeaders());

            if (statusCode < 200)
            {
                // 1xx cannot be a final response: Kestrel would send an interim status and leave
                // the caller waiting for one that never comes. The function asked for something
                // HTTP cannot deliver, which is a bad answer from upstream — a 502.
                return Error(runId, 502, "INVALID_STATUS_CODE",
                    $"the function returned statusCode {statusCode}, which cannot be a final HTTP response");
            }

            if (BodylessStatuses.Contains(statusCode)
                || !root.TryGetProperty("body", out var body)
                || body.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return new Response(statusCode, headers, BodylessStatuses.Contains(statusCode) ? null : contentType, []);
            }

            if (body.ValueKind == JsonValueKind.String)
            {
                return new Response(statusCode, headers, contentType ?? TextContentType, Encoding.UTF8.GetBytes(body.GetString() ?? string.Empty));
            }

            return new Response(statusCode, headers, contentType ?? JsonContentType, Encoding.UTF8.GetBytes(body.GetRawText()));
        }

        /// <summary>An integral <c>statusCode</c> in 100–599 makes the result an HTTP response.</summary>
        private static bool TryStatusCode(JsonElement root, out int statusCode)
        {
            statusCode = 0;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("statusCode", out var code)
                && code.ValueKind == JsonValueKind.Number
                && code.TryGetInt32(out statusCode)
                && statusCode is >= 100 and <= 599;
        }

        /// <summary>A string, number or boolean is one value; an array is one header line per scalar element.</summary>
        private static IEnumerable<string> HeaderValues(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    yield return value.GetString() ?? string.Empty;
                    break;
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    yield return value.GetRawText();
                    break;
                case JsonValueKind.Array:
                    foreach (var item in value.EnumerateArray())
                    {
                        if (item.ValueKind is JsonValueKind.Array or JsonValueKind.Object or JsonValueKind.Null) continue;
                        foreach (var scalar in HeaderValues(item)) yield return scalar;
                    }
                    break;
                default:
                    // Objects and null have no header form; skipped rather than stringified.
                    break;
            }
        }

        /// <summary>RFC 9110 token characters only — what Kestrel accepts as a header name.</summary>
        private static bool IsToken(string name)
        {
            if (name.Length == 0) return false;
            foreach (var c in name)
            {
                var ok = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'
                    || "!#$%&'*+-.^_`|~".Contains(c);
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>No control characters (CR/LF above all: response splitting) and ASCII only, as Kestrel requires by default.</summary>
        private static bool IsSafeValue(string value)
        {
            foreach (var c in value)
            {
                if (c == '\t') continue;
                if (c < 0x20 || c >= 0x7f) return false;
            }
            return true;
        }

        /// <summary>The allow-list (see the type summary).</summary>
        private static bool IsAllowed(string name, string value, int statusCode)
        {
            if (AllowedHeaders.Contains(name)) return true;

            if (string.Equals(name, "location", StringComparison.OrdinalIgnoreCase))
            {
                return statusCode is >= 300 and <= 399 && IsSafeLocation(value);
            }

            if (!name.StartsWith("x-", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(name, "x-real-ip", StringComparison.OrdinalIgnoreCase)) return false;
            // Set by the platform below; a function must not switch sniffing back on.
            if (string.Equals(name, "x-content-type-options", StringComparison.OrdinalIgnoreCase)) return false;
            return !ReservedXPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// An absolute http/https URL, or a relative one. No other scheme (<c>javascript:</c>,
        /// <c>data:</c>, …), and no scheme-relative <c>//host</c> or <c>/\host</c>, which browsers
        /// treat as another origin. Checked by hand for the leading slash because on Linux
        /// <c>Uri</c> reads <c>/path</c> as an absolute file URI.
        /// </summary>
        internal static bool IsSafeLocation(string value)
        {
            var target = value.Trim();
            if (target.Length == 0) return false;

            if (target[0] is '/' or '\\')
            {
                return target.Length == 1 || (target[1] is not '/' and not '\\');
            }

            if (Uri.TryCreate(target, UriKind.Absolute, out var absolute))
            {
                return absolute.Scheme == Uri.UriSchemeHttps || absolute.Scheme == Uri.UriSchemeHttp;
            }

            // A path-relative target ("orders/9", "?page=2"): nothing before a ':' may look like a scheme.
            var colon = target.IndexOf(':');
            var slash = target.IndexOfAny(['/', '?', '#']);
            return colon < 0 || (slash >= 0 && slash < colon);
        }

        private static List<KeyValuePair<string, string>> SecurityHeaders() =>
        [
            new("X-Content-Type-Options", "nosniff"),
            new("Content-Security-Policy", "sandbox"),
        ];

        private static Response Json(int statusCode, List<KeyValuePair<string, string>> headers, string json)
        {
            headers.AddRange(SecurityHeaders());
            return new Response(statusCode, headers, JsonContentType, Encoding.UTF8.GetBytes(json));
        }

        private static Response Error(string runId, int statusCode, string code, string message)
        {
            // The run id travels with the error so the caller can still poll the run or quote it.
            var json = JsonSerializer.Serialize(new { error = new { code, message }, runId }, SerializerOptions);
            return Json(statusCode, [], json);
        }
    }
}
