using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;

namespace Functions.DomainService.Services
{
    /// <summary>
    /// Shapes an HTTP invocation into the <c>input</c> a handler receives.
    /// <para>
    /// The public route is a gateway-style catch-all — any method, any path under the function id
    /// — so the handler has to be told what was actually called, not just handed a body. Every
    /// HTTP-triggered run therefore receives one shape:
    /// </para>
    /// <code>
    /// {
    ///   "method":  "POST",
    ///   "path":    "orders/42",                 // what followed /api/fn/{id}/, "" for none
    ///   "query":   { "limit": "10", "tag": ["a", "b"] },
    ///   "headers": { "content-type": "application/json", ... },
    ///   "body":    { ... } | "raw text" | null,
    ///   "rawBody": "base64 of the exact bytes" | null
    /// }
    /// </code>
    /// <para>
    /// Editor test runs are wrapped the same way (<see cref="ForTest"/>) so the code a tenant tests
    /// is the code that runs in production — <c>input.body</c> is the test payload. Workflow
    /// invocations are not: there the previous node's output <i>is</i> the input, and the trigger
    /// card says so.
    /// </para>
    /// <para>
    /// Headers are an allow-list, never the request's full set. The envelope screen refuses any
    /// credential-shaped key (<c>authorization</c>, <c>cookie</c>, <c>x-blocks-key</c> …) and the
    /// spec forbids a token from ever entering a sandbox, so the only headers that travel are the
    /// content-negotiation and provenance ones a handler can legitimately act on — and the screen
    /// keeps checking <c>input.headers</c>, so a regression in this list fails the run rather than
    /// leaking. Query and body keys are the caller's own and pass through as-is, credential-shaped
    /// or not (<c>?token=</c>, a signup form's <c>password</c>): the screen exempts them, because
    /// the caller chose to send that data to this function.
    /// </para>
    /// </summary>
    public static class FunctionHttpInputBuilder
    {
        /// <summary>
        /// Request headers a handler may see. Lower-case; matched case-insensitively. Nothing
        /// that can carry a credential, and nothing the platform itself uses to route the call.
        /// </summary>
        public static readonly IReadOnlySet<string> ForwardedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "content-type",
            "content-length",
            "accept",
            "accept-language",
            "accept-encoding",
            "user-agent",
            "origin",
            "referer",
            "x-request-id",
            "x-correlation-id",
            "x-forwarded-for",
            "x-forwarded-proto",
            "x-forwarded-host",

            // Webhook signatures (F-3, 2026-10-07): a signature proves who sent the body, it
            // grants nothing, so it may travel. Verified against `rawBody` and the tenant's own
            // signing secret (a bound variable), never against the parsed `body`.
            "stripe-signature",
            "x-hub-signature",
            "x-hub-signature-256",
            "x-github-event",
            "x-github-delivery",
            "x-shopify-hmac-sha256",
            "x-shopify-topic",
            "x-shopify-shop-domain",
            "x-shopify-webhook-id",
            "x-slack-signature",
            "x-slack-request-timestamp",
            // Svix and Standard Webhooks (Clerk, Resend, OpenAI and others).
            "svix-id",
            "svix-timestamp",
            "svix-signature",
            "webhook-id",
            "webhook-timestamp",
            "webhook-signature",
        };

        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        /// <summary>The input for a real HTTP invocation.</summary>
        public static string Build(InvokeFunctionRequestDto request)
        {
            ArgumentNullException.ThrowIfNull(request);

            var input = new JsonObject
            {
                ["method"] = (request.Method ?? "POST").ToUpperInvariant(),
                ["path"] = NormalisePath(request.Path),
                ["query"] = Query(request.Query),
                ["headers"] = Headers(request.Headers),
                ["body"] = Body(request.Body, request.ContentType),
            };

            return WithRawBody(input, request.Body);
        }

        /// <summary>
        /// Room the rest of the envelope (identity, env, limits) needs beside the input; the same
        /// margin <see cref="MaxBodyBytes"/> leaves.
        /// </summary>
        private const long EnvelopeMargin = 64 * 1024;

        /// <summary>
        /// Adds <c>rawBody</c>: the request's exact bytes, base64. A webhook signature is computed
        /// over those bytes, and the parsed <c>body</c> cannot give them back (whitespace, key
        /// order, number forms). It carries the body a second time, so it is added only when the
        /// whole input still fits the input ceiling — otherwise <c>null</c>, and a body that is
        /// accepted today is never refused for it. With no body it is <c>null</c>.
        /// </summary>
        private static string WithRawBody(JsonObject input, byte[]? body)
        {
            if (body is null || body.Length == 0)
            {
                input["rawBody"] = null;
                return input.ToJsonString(SerializerOptions);
            }

            input["rawBody"] = null;
            var without = input.ToJsonString(SerializerOptions);
            var base64Length = 4L * ((body.Length + 2) / 3);
            if (Encoding.UTF8.GetByteCount(without) + base64Length > FunctionLimits.Ceiling.InputBytes - EnvelopeMargin)
            {
                return without;
            }

            input["rawBody"] = Convert.ToBase64String(body);
            return input.ToJsonString(SerializerOptions);
        }

        /// <summary>The wire verb for a trigger's method: <c>GET</c> or <c>POST</c>.</summary>
        public static string Verb(HttpTriggerMethod method) =>
            method == HttpTriggerMethod.Get ? "GET" : "POST";

        /// <summary>Every verb the public route is registered for, and so every verb a trigger may list.</summary>
        public static readonly IReadOnlyList<string> RoutableVerbs = ["GET", "POST", "PUT", "PATCH", "DELETE"];

        /// <summary>
        /// The verbs a trigger answers: its <see cref="TriggerConfig.HttpMethods"/> list, upper-cased
        /// and de-duplicated, in the order the tenant chose; or, when that list is empty (every
        /// document stored before the list existed), the single legacy <see cref="TriggerConfig.HttpMethod"/>.
        /// Anything outside <see cref="RoutableVerbs"/> is dropped rather than trusted — the
        /// validator refuses it on save, and a stored stray value must not widen the route.
        /// </summary>
        public static IReadOnlyList<string> AllowedVerbs(TriggerConfig trigger)
        {
            ArgumentNullException.ThrowIfNull(trigger);

            var listed = (trigger.HttpMethods ?? [])
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim().ToUpperInvariant())
                .Where(m => RoutableVerbs.Contains(m))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return listed.Count > 0 ? listed : [Verb(trigger.HttpMethod)];
        }

        /// <summary>
        /// The verb an editor test simulates: the legacy method when the list still includes it
        /// (so a function that only gained extra verbs tests exactly as before), otherwise the
        /// first verb the tenant listed.
        /// </summary>
        public static string TestVerb(TriggerConfig trigger)
        {
            var allowed = AllowedVerbs(trigger);
            var legacy = Verb(trigger.HttpMethod);
            return allowed.Contains(legacy) ? legacy : allowed[0];
        }

        /// <summary>
        /// The input for an editor test run, shaped as a real call to the function's root with the
        /// trigger's own method, so <c>handler(input)</c> reads identically in the editor and in
        /// production. For POST the payload is the body. For GET — where a real call has no body —
        /// the payload's top-level fields become the query string, each value as the string a URL
        /// would carry (an array stays a repeated key); a payload that is not an object has nowhere
        /// to go on a GET and yields an empty query.
        /// </summary>
        public static string ForTest(string? inputJson, HttpTriggerMethod method = HttpTriggerMethod.Post)
            => ForTest(inputJson, Verb(method));

        /// <summary>
        /// <see cref="ForTest(string?, HttpTriggerMethod)"/> for any routable verb: GET carries the
        /// payload as the query, every other verb (POST, PUT, PATCH, DELETE) as the body.
        /// </summary>
        public static string ForTest(string? inputJson, string verb)
        {
            var payload = ParseBodyText(inputJson);
            var normalised = string.IsNullOrWhiteSpace(verb) ? "POST" : verb.Trim().ToUpperInvariant();
            var isGet = normalised == "GET";

            var input = new JsonObject
            {
                ["method"] = normalised,
                ["path"] = string.Empty,
                ["query"] = isGet ? QueryFromPayload(payload) : new JsonObject(),
                ["headers"] = isGet
                    ? new JsonObject { ["accept"] = "application/json" }
                    : new JsonObject { ["content-type"] = "application/json" },
                ["body"] = isGet ? null : payload,
            };

            // A test carries its payload as the raw body too, so code that verifies or reads
            // rawBody runs in Test as it will in production.
            return WithRawBody(input, isGet || string.IsNullOrWhiteSpace(inputJson) ? null : Encoding.UTF8.GetBytes(inputJson));
        }

        private static JsonObject QueryFromPayload(JsonNode? payload)
        {
            var query = new JsonObject();
            if (payload is not JsonObject fields) return query;

            foreach (var (key, value) in fields)
            {
                query[key] = value switch
                {
                    null => JsonValue.Create(string.Empty),
                    JsonArray items => new JsonArray(items.Select(i => (JsonNode?)JsonValue.Create(QueryText(i))).ToArray()),
                    _ => JsonValue.Create(QueryText(value)),
                };
            }

            return query;
        }

        /// <summary>A scalar as a query string carries it; anything structured as its JSON text.</summary>
        private static string QueryText(JsonNode? node) => node switch
        {
            null => string.Empty,
            JsonValue value when value.TryGetValue<string>(out var s) => s,
            JsonValue value => value.ToJsonString(SerializerOptions),
            _ => node.ToJsonString(SerializerOptions),
        };

        /// <summary>
        /// <c>/orders//42/</c> → <c>orders/42</c>. The route template already strips the leading
        /// slash; this makes doubled and trailing ones irrelevant too, so a handler can compare
        /// against a literal without a normalisation step of its own.
        /// </summary>
        public static string NormalisePath(string? path) =>
            string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries));

        private static JsonObject Query(IReadOnlyDictionary<string, string[]>? query)
        {
            var node = new JsonObject();
            if (query is null) return node;

            foreach (var (key, values) in query)
            {
                if (string.IsNullOrEmpty(key)) continue;
                var present = values?.Where(v => v is not null).ToArray() ?? [];
                // A single value is a string, a repeated key an array — the shape a query string
                // library would produce, so `input.query.limit` is not `["10"]` by surprise.
                node[key] = present.Length switch
                {
                    0 => JsonValue.Create(string.Empty),
                    1 => JsonValue.Create(present[0]),
                    _ => new JsonArray(present.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()),
                };
            }

            return node;
        }

        private static JsonObject Headers(IReadOnlyDictionary<string, string>? headers)
        {
            var node = new JsonObject();
            if (headers is null) return node;

            foreach (var (name, value) in headers)
            {
                if (!ForwardedHeaders.Contains(name)) continue;
                node[name.ToLowerInvariant()] = value ?? string.Empty;
            }

            return node;
        }

        /// <summary>
        /// JSON bodies arrive parsed, everything else as text. A body that declares JSON but does
        /// not parse is still delivered — as the raw string — so the handler can see what the
        /// caller sent rather than the run failing on the caller's mistake.
        /// </summary>
        private static JsonNode? Body(byte[]? body, string? contentType)
        {
            if (body is null || body.Length == 0) return null;

            // Bytes that are not valid UTF-8 come through as U+FFFD rather than failing the run:
            // the handler sees that something arrived and where it broke, which is more useful
            // than a refused call for a body the sandbox could not have used anyway.
            var text = Encoding.UTF8.GetString(body);

            var declaresJson = contentType is not null
                && contentType.Contains("json", StringComparison.OrdinalIgnoreCase);

            // Undeclared content types are still tried as JSON: curl without -H sends
            // x-www-form-urlencoded for a JSON payload, and a handler would rather have the object.
            if (declaresJson || contentType is null || LooksLikeJson(text))
            {
                return ParseBodyText(text);
            }

            return JsonValue.Create(text);
        }

        private static JsonNode? ParseBodyText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                return JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                return JsonValue.Create(text);
            }
        }

        private static bool LooksLikeJson(string text)
        {
            var trimmed = text.AsSpan().TrimStart();
            return trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '[');
        }

        /// <summary>
        /// Hard ceiling on a request body. The whole envelope — identity, env, limits and this
        /// body — must fit <see cref="FunctionLimits.Ceiling.InputBytes"/> (1 MB), which the
        /// runtime also enforces, so the body itself is capped a little under that to leave room
        /// for the rest. Over this, the call is refused with 413 before anything is queued.
        /// </summary>
        public const long MaxBodyBytes = FunctionLimits.Ceiling.InputBytes - 64 * 1024;
    }
}
