using System.Text.Json;
using System.Text.Json.Serialization;
using Proxy.DomainService.Entities;

namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// Encodes a route's overrides as the single string value a <c>ProxyFieldChange</c> carries, and decodes
    /// it back for Revert.
    /// <para>
    /// A route is addressed as a whole (<c>route:&lt;METHOD&gt; &lt;path&gt;</c>) rather than broken into one
    /// address per member, as headers and query are. A route's members only make sense together — reverting
    /// an upstream-path change without the body-merge change that accompanied it would produce a
    /// configuration nobody ever saved — and the <c>(method, path)</c> pair is the identity, so an edit to
    /// either is an add plus a remove, not a change.
    /// </para>
    /// </summary>
    public static class ProxyRouteCodec
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };

        /// <summary>The stable address of a route within a proxy's change set.</summary>
        public static string AddressOf(ProxyRouteConfig route) =>
            $"{route.Method.Wire()} {ProxyRoutePath.Normalize(route.Path)}";

        /// <summary>Human label for the change history, e.g. <c>"route GET /orders/{id}"</c>.</summary>
        public static string LabelOf(ProxyRouteConfig route) =>
            $"route {route.Method.Wire()} /{ProxyRoutePath.Normalize(route.Path)}";

        /// <summary>
        /// The route's overrides as JSON. Method and path are omitted: they are the address, so including
        /// them would let a value disagree with the field it is stored under.
        /// </summary>
        public static string Encode(ProxyRouteConfig route) => JsonSerializer.Serialize(
            new Payload
            {
                UpstreamPath = route.UpstreamPath,
                Headers = route.Headers,
                Query = route.Query,
                BodyMerge = route.BodyMerge,
                ResponseMode = route.ResponseMode?.ToString(),
                ResponseInclude = route.ResponseInclude,
                Resilience = route.Resilience,
            },
            Options);

        /// <summary>
        /// Rebuilds a route from its address and encoded overrides. Returns <c>null</c> when either is
        /// unusable, so a hand-edited or future-version history row is skipped rather than throwing mid-revert.
        /// </summary>
        public static ProxyRouteConfig? Decode(string address, string? encoded)
        {
            var separator = address.IndexOf(' ', StringComparison.Ordinal);
            if (separator <= 0
                || !HttpMethodTypeExtensions.TryParse(address[..separator], out var method))
            {
                return null;
            }

            // Revert restores stored field values without re-running ProxyConfigValidator, so this is the
            // only place a route coming back from a version row is checked. A history row written by another
            // build — or edited in the database — must not be able to install a template the validator would
            // have refused, which is the one way a '..' could reach the forwarder.
            var path = ProxyRoutePath.Normalize(address[(separator + 1)..]);
            if (!ProxyRoutePath.TryParseTemplate(path, out var parameters, out _))
            {
                return null;
            }

            Payload? payload;
            try
            {
                payload = string.IsNullOrWhiteSpace(encoded)
                    ? new Payload()
                    : JsonSerializer.Deserialize<Payload>(encoded, Options);
            }
            catch (JsonException)
            {
                return null;
            }

            if (payload is null)
            {
                return null;
            }

            if (payload.UpstreamPath is not null)
            {
                var upstreamPath = ProxyRoutePath.Normalize(payload.UpstreamPath);
                if (!ProxyRoutePath.TryParseTemplate(upstreamPath, out var upstreamParameters, out _)
                    || upstreamParameters.Exists(u => !parameters.Contains(u, StringComparer.Ordinal)))
                {
                    return null;
                }

                payload.UpstreamPath = upstreamPath;
            }

            ProxyResponseMode? responseMode = null;
            if (!string.IsNullOrWhiteSpace(payload.ResponseMode)
                && Enum.TryParse<ProxyResponseMode>(payload.ResponseMode, ignoreCase: true, out var parsed))
            {
                responseMode = parsed;
            }

            return new ProxyRouteConfig
            {
                Method = method,
                Path = path,
                UpstreamPath = payload.UpstreamPath,
                Headers = payload.Headers,
                Query = payload.Query,
                BodyMerge = payload.BodyMerge,
                ResponseMode = responseMode,
                ResponseInclude = payload.ResponseInclude,

                // Revalidated on the way back in, not trusted. A history row can be older than the
                // current bounds, or edited in the database, and revert does not re-run the validator —
                // so a value outside them is dropped rather than installed.
                Resilience = SafeResilience(payload.Resilience),
            };
        }

        /// <summary>Value-equality over everything except the address, to decide whether a route changed.</summary>
        public static bool OverridesEqual(ProxyRouteConfig a, ProxyRouteConfig b) =>
            string.Equals(Encode(a), Encode(b), StringComparison.Ordinal);

        private sealed class Payload
        {
            public string? UpstreamPath { get; set; }

            public List<ProxyKeyValue>? Headers { get; set; }

            public List<ProxyKeyValue>? Query { get; set; }

            public List<ProxyKeyValue>? BodyMerge { get; set; }

            public string? ResponseMode { get; set; }

            public List<string>? ResponseInclude { get; set; }

            public ProxyResilienceConfig? Resilience { get; set; }
        }

        /// <summary>
        /// Keeps a restored resilience config only where every value is still inside today's bounds.
        /// Out-of-range members are dropped individually, so one stale number does not discard the rest.
        /// </summary>
        private static ProxyResilienceConfig? SafeResilience(ProxyResilienceConfig? source)
        {
            if (source is null) return null;

            var safe = new ProxyResilienceConfig();

            if (source.TimeoutSeconds is { } timeout
                && timeout >= 1 && timeout <= ProxyConfigValidator.MaxTimeoutSeconds)
            {
                safe.TimeoutSeconds = timeout;
            }

            // The idempotency claim travels with the policy. A restored retry without it is dropped:
            // the tenant's assertion that repeating is safe cannot be inferred from an old row.
            if (source.Retry is { Idempotent: true } retry
                && retry.Attempts > 1 && retry.Attempts <= ProxyConfigValidator.MaxRetryAttempts
                && retry.InitialDelaySeconds >= 1
                && retry.InitialDelaySeconds <= ProxyConfigValidator.MaxRetryDelaySeconds)
            {
                safe.Retry = retry;
            }

            if (source.Breaker is { } breaker
                && breaker.FailureThreshold >= 1
                && breaker.FailureThreshold <= ProxyConfigValidator.MaxBreakerThreshold
                && breaker.OpenSeconds >= 1
                && breaker.OpenSeconds <= ProxyConfigValidator.MaxBreakerOpenSeconds)
            {
                safe.Breaker = breaker;
            }

            return safe.IsEmpty ? null : safe;
        }
    }
}
