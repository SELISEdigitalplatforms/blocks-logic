using Proxy.DomainService.Dtos;
using Proxy.DomainService.Entities;

namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// The normalized, validated result of a Create / Update payload. <see cref="Errors"/> is empty when the
    /// payload is valid; otherwise it maps each offending field to a human reason (SPEC &sect;3.4 /
    /// <c>PROXY_VALIDATION</c>). The normalized values are safe to persist.
    /// </summary>
    public sealed class ProxyConfigValidationResult
    {
        public Dictionary<string, string> Errors { get; } = new(StringComparer.Ordinal);

        public bool IsValid => Errors.Count == 0;

        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The slug <see cref="Name"/> derives to, via <see cref="ProxySlug"/>. Populated only when the name
        /// is valid, so a caller can persist it without re-deriving. Never empty on a valid result.
        /// </summary>
        public string Slug { get; set; } = string.Empty;

        public string Upstream { get; set; } = string.Empty;

        public List<HttpMethodType> Methods { get; set; } = new();

        public List<ProxyKeyValue> Headers { get; set; } = new();

        public List<ProxyKeyValue> Query { get; set; } = new();

        public List<ProxyKeyValue> BodyMerge { get; set; } = new();

        public List<ProxyMethodConfig> MethodConfigs { get; set; } = new();

        /// <summary>Normalized response-body treatment. Defaults to <see cref="ProxyResponseMode.All"/>.</summary>
        public ProxyResponseMode ResponseMode { get; set; } = ProxyResponseMode.All;

        /// <summary>
        /// Normalized (trimmed, blank-dropped, ordinal-deduped) response field paths. Kept regardless of
        /// <see cref="ResponseMode"/> so a mode round-trip does not lose the user's paths.
        /// </summary>
        public List<string> ResponseInclude { get; set; } = new();
    }

    /// <summary>
    /// Shallow, deterministic validation of a proxy configuration payload. Trims and normalizes on the way
    /// through: name is trimmed, upstream is trimmed, methods are upper-cased / de-duplicated /
    /// first-occurrence ordered. Header / query / body-merge values are stored verbatim, including any
    /// <c>{{$VAR.name}}</c> token; the validator stays pure, sync, and offline (no Key Vault call at save
    /// time) &mdash; the token's existence is checked only on the forward path.
    /// </summary>
    public static class ProxyConfigValidator
    {
        internal const int MaxNameLength = 80;
        internal const int MaxUpstreamLength = 2048;
        internal const int MaxKeyLength = 256;
        internal const int MaxValueLength = 4096;
        internal const int MaxMethods = 5;

        public static ProxyConfigValidationResult Validate(
            string? name,
            string? upstream,
            IEnumerable<string>? methods,
            IEnumerable<ProxyKeyValueInputDto>? headers,
            IEnumerable<ProxyKeyValueInputDto>? query,
            IEnumerable<ProxyMethodConfigInputDto>? methodConfigs = null,
            IEnumerable<ProxyKeyValueInputDto>? bodyMerge = null,
            string? responseMode = null,
            IEnumerable<string>? responseInclude = null)
        {
            var result = new ProxyConfigValidationResult();

            ValidateName(name, result);
            ValidateUpstream(upstream, result);
            result.Methods = NormalizeMethods(methods, result);
            result.Headers = NormalizePairs(headers, "headers", result);
            result.Query = NormalizePairs(query, "query", result);
            result.BodyMerge = NormalizePairs(bodyMerge, "bodyMerge", result);
            result.MethodConfigs = NormalizeMethodConfigs(methodConfigs, result);
            NormalizeResponseFilter(responseMode, responseInclude, result);

            return result;
        }

        /// <summary>
        /// Normalizes the response-field-filter layer (SPEC "response field filtering" &sect;4.3).
        /// <paramref name="responseMode"/> is parsed case-insensitively to <c>All</c> / <c>Select</c> (empty ⇒
        /// <c>All</c>); anything else is an error. <paramref name="responseInclude"/> entries are trimmed,
        /// blank-dropped, ordinal-deduped, then capped at <see cref="ProxyResponsePath.MaxPaths"/> and each
        /// checked against <see cref="ProxyResponsePath.IsValid"/>. An empty normalized list is valid
        /// (Select + empty ⇒ <c>{}</c> at runtime). Normalization runs regardless of mode.
        /// </summary>
        private static void NormalizeResponseFilter(
            string? responseMode, IEnumerable<string>? responseInclude, ProxyConfigValidationResult result)
        {
            var mode = (responseMode ?? string.Empty).Trim();
            if (mode.Length == 0)
            {
                result.ResponseMode = ProxyResponseMode.All;
            }
            else if (Enum.TryParse<ProxyResponseMode>(mode, ignoreCase: true, out var parsed))
            {
                result.ResponseMode = parsed;
            }
            else
            {
                result.Errors["responseMode"] = "responseMode must be 'All' or 'Select'.";
            }

            var normalized = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in responseInclude ?? Enumerable.Empty<string>())
            {
                var path = (raw ?? string.Empty).Trim();
                if (path.Length == 0 || !seen.Add(path))
                {
                    continue;
                }

                normalized.Add(path);
            }

            if (normalized.Count > ProxyResponsePath.MaxPaths)
            {
                result.Errors["responseInclude"] = $"At most {ProxyResponsePath.MaxPaths} response fields.";
            }
            else
            {
                var invalid = normalized.FirstOrDefault(p => !ProxyResponsePath.IsValid(p));
                if (invalid is not null)
                {
                    result.Errors["responseInclude"] = $"'{invalid}' is not a valid field path.";
                }
            }

            result.ResponseInclude = normalized;
        }

        /// <summary>
        /// Trims the name, then checks it derives to a usable slug. The gateway route is
        /// <c>/api/proxy/gateway/{slug}/{**path}</c>, so a name with no <c>[a-z0-9]</c> character at all
        /// (<c>"!!!"</c>, or a fully non-Latin name) would yield an empty slug and create a proxy that no
        /// client can ever reach &mdash; and the next such name would collide with it on the unique slug
        /// index, surfacing as a <c>PROXY_SLUG_CONFLICT</c> naming an unrelated proxy. Reject it here instead.
        /// </summary>
        private static void ValidateName(string? name, ProxyConfigValidationResult result)
        {
            var trimmed = (name ?? string.Empty).Trim();
            result.Name = trimmed;
            if (trimmed.Length == 0 || trimmed.Length > MaxNameLength)
            {
                result.Errors["name"] = $"Name is required and must be {MaxNameLength} characters or fewer.";
                return;
            }

            result.Slug = ProxySlug.From(trimmed);
            if (result.Slug.Length == 0)
            {
                result.Errors["name"] = "Name must contain at least one letter (a-z) or digit (0-9).";
            }
        }

        private static void ValidateUpstream(string? upstream, ProxyConfigValidationResult result)
        {
            var trimmed = (upstream ?? string.Empty).Trim();
            result.Upstream = trimmed;

            if (trimmed.Length > MaxUpstreamLength)
            {
                result.Errors["upstream"] = $"Upstream URL must be {MaxUpstreamLength} characters or fewer.";
                return;
            }

            if (!IsAbsoluteHttps(trimmed))
            {
                result.Errors["upstream"] = "Must be an absolute https:// URL.";
                return;
            }

            if (ProxyUpstreamGuard.IsDisallowedTarget(trimmed, out var guardReason))
            {
                result.Errors["upstream"] = guardReason;
            }
        }

        /// <summary><c>true</c> when <paramref name="value"/> is an absolute <c>https://</c> URL with a host.</summary>
        internal static bool IsAbsoluteHttps(string value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrEmpty(uri.Host);

        /// <summary>
        /// Normalizes the per-method override layer (SPEC D-feature &sect;3.1). Each entry must target a method
        /// that is in the (already normalized) allowed set, with no duplicates. An override member is either
        /// <c>null</c> (inherit the shared value) or a non-empty value: a blank upstream and an empty
        /// header / query list collapse to <c>null</c>, and an override whose three members are all <c>null</c>
        /// is dropped.
        /// </summary>
        private static List<ProxyMethodConfig> NormalizeMethodConfigs(
            IEnumerable<ProxyMethodConfigInputDto>? methodConfigs, ProxyConfigValidationResult result)
        {
            var normalized = new List<ProxyMethodConfig>();
            var seen = new HashSet<HttpMethodType>();

            foreach (var entry in methodConfigs ?? Enumerable.Empty<ProxyMethodConfigInputDto>())
            {
                if (entry is null)
                {
                    continue;
                }

                if (!HttpMethodTypeExtensions.TryParse(entry.Method, out var method)
                    || !result.Methods.Contains(method))
                {
                    result.Errors["methodConfigs"] = "Per-method overrides must target an allowed method.";
                    continue;
                }

                if (!seen.Add(method))
                {
                    result.Errors["methodConfigs"] = "Only one override per method is allowed.";
                    continue;
                }

                var upstream = (entry.Upstream ?? string.Empty).Trim();
                var resolvedUpstream = upstream.Length == 0 ? null : upstream;
                if (resolvedUpstream is not null && !IsAbsoluteHttps(resolvedUpstream))
                {
                    result.Errors["methodConfigs"] = "Each per-method upstream must be an absolute https:// URL.";
                }
                else if (resolvedUpstream is not null
                    && ProxyUpstreamGuard.IsDisallowedTarget(resolvedUpstream, out var mcGuardReason))
                {
                    result.Errors["methodConfigs"] = mcGuardReason;
                }

                var overrideHeaders = entry.Headers is null ? null : NormalizePairs(entry.Headers, "methodConfigs", result);
                var overrideQuery = entry.Query is null ? null : NormalizePairs(entry.Query, "methodConfigs", result);
                if (overrideHeaders is { Count: 0 })
                {
                    overrideHeaders = null;
                }

                if (overrideQuery is { Count: 0 })
                {
                    overrideQuery = null;
                }

                if (resolvedUpstream is null && overrideHeaders is null && overrideQuery is null)
                {
                    continue;
                }

                normalized.Add(new ProxyMethodConfig
                {
                    Method = method,
                    Upstream = resolvedUpstream,
                    Headers = overrideHeaders,
                    Query = overrideQuery,
                });
            }

            return normalized;
        }

        private static List<HttpMethodType> NormalizeMethods(IEnumerable<string>? methods, ProxyConfigValidationResult result)
        {
            var raw = (methods ?? Enumerable.Empty<string>())
                .Select(m => (m ?? string.Empty).Trim())
                .Where(m => m.Length > 0)
                .ToList();

            var normalized = new List<HttpMethodType>();
            var anyUnparseable = false;
            foreach (var method in raw)
            {
                if (!HttpMethodTypeExtensions.TryParse(method, out var parsed))
                {
                    anyUnparseable = true;
                    continue;
                }

                if (!normalized.Contains(parsed))
                {
                    normalized.Add(parsed);
                }
            }

            if (anyUnparseable || normalized.Count > MaxMethods)
            {
                result.Errors["methods"] = "Only GET, POST, PUT, PATCH, DELETE are allowed.";
            }
            else if (normalized.Count == 0)
            {
                result.Errors["methods"] = "At least one HTTP method is required.";
            }

            return normalized;
        }

        private static List<ProxyKeyValue> NormalizePairs(
            IEnumerable<ProxyKeyValueInputDto>? pairs, string field, ProxyConfigValidationResult result)
        {
            var normalized = new List<ProxyKeyValue>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Body-merge keys are JSON object keys: case-sensitive, so dedupe them ordinally, unlike headers.
            var seenBodyKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var pair in pairs ?? Enumerable.Empty<ProxyKeyValueInputDto>())
            {
                var key = pair?.Key ?? string.Empty;
                var value = pair?.Value ?? string.Empty;

                if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength || key.Trim().Length != key.Length)
                {
                    result.Errors[field] =
                        $"Each {field} key is required, must be {MaxKeyLength} characters or fewer, and must not have leading or trailing whitespace.";
                }
                else if (field == "headers" && !seenKeys.Add(key.Trim()))
                {
                    // A repeated header key would otherwise be sent as multiple header lines upstream.
                    result.Errors[field] = "Each header key must be unique (case-insensitive).";
                }
                else if (field == "bodyMerge" && !seenBodyKeys.Add(key.Trim()))
                {
                    result.Errors[field] = "Each body field key must be unique.";
                }

                if (value.Length > MaxValueLength)
                {
                    result.Errors[field] = $"Each {field} value must be {MaxValueLength} characters or fewer.";
                }

                normalized.Add(new ProxyKeyValue
                {
                    Key = key,
                    Value = value,
                });
            }

            return normalized;
        }
    }
}
