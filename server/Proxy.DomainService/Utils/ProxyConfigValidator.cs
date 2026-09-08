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

        public string Upstream { get; set; } = string.Empty;

        public List<HttpMethodType> Methods { get; set; } = new();

        public List<ProxyKeyValue> Headers { get; set; } = new();

        public List<ProxyKeyValue> Query { get; set; } = new();

        public List<ProxyMethodConfig> MethodConfigs { get; set; } = new();
    }

    /// <summary>
    /// Shallow, deterministic validation of a proxy configuration payload. Trims and normalizes on the way
    /// through: name is trimmed, upstream is trimmed, methods are upper-cased / de-duplicated /
    /// first-occurrence ordered, and each header / query value is flagged for a <c>${SECRET.NAME}</c> reference.
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
            IEnumerable<ProxyMethodConfigInputDto>? methodConfigs = null)
        {
            var result = new ProxyConfigValidationResult();

            ValidateName(name, result);
            ValidateUpstream(upstream, result);
            result.Methods = NormalizeMethods(methods, result);
            result.Headers = NormalizePairs(headers, "headers", result);
            result.Query = NormalizePairs(query, "query", result);
            result.MethodConfigs = NormalizeMethodConfigs(methodConfigs, result);

            return result;
        }

        private static void ValidateName(string? name, ProxyConfigValidationResult result)
        {
            var trimmed = (name ?? string.Empty).Trim();
            result.Name = trimmed;
            if (trimmed.Length == 0 || trimmed.Length > MaxNameLength)
            {
                result.Errors["name"] = $"Name is required and must be {MaxNameLength} characters or fewer.";
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

                if (value.Length > MaxValueLength)
                {
                    result.Errors[field] = $"Each {field} value must be {MaxValueLength} characters or fewer.";
                }

                normalized.Add(new ProxyKeyValue
                {
                    Key = key,
                    Value = value,
                    // A ${SECRET.NAME} value is always a secret reference; the console's "Vault" checkbox opts a
                    // plain value into the same treatment.
                    IsSecretRef = pair?.IsSecretRef == true || ProxySecretRef.IsSecretReference(value),
                });
            }

            return normalized;
        }
    }
}
