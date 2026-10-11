using Common.InternalService.Access;
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

        /// <summary>
        /// Normalized route allowlist: paths trimmed of surrounding slashes, blank overrides collapsed to
        /// <c>null</c> (inherit). Empty ⇒ the proxy accepts its base path only.
        /// </summary>
        public List<ProxyRouteConfig> Routes { get; set; } = new();

        /// <summary>
        /// The proxy-wide timeout / retry / breaker settings, or <c>null</c> when the tenant configured
        /// none — which is the same as never having configured them.
        /// </summary>
        public ProxyResilienceConfig? Resilience { get; set; }

        /// <summary>The tenant's own gateway calls-per-minute limit, or <c>null</c> for the default (P-3).</summary>
        public int? RequestsPerMinute { get; set; }

        /// <summary>Normalized response-body treatment. Defaults to <see cref="ProxyResponseMode.All"/>.</summary>
        public ProxyResponseMode ResponseMode { get; set; } = ProxyResponseMode.All;

        /// <summary>
        /// Normalized (trimmed, blank-dropped, ordinal-deduped) response field paths. Kept regardless of
        /// <see cref="ResponseMode"/> so a mode round-trip does not lose the user's paths.
        /// </summary>
        public List<string> ResponseInclude { get; set; } = new();

        /// <summary>Normalized "Who can call it" policy. Defaults to a Blocks token with no further restriction.</summary>
        public EndpointAccessPolicy Access { get; set; } = EndpointAccessPolicy.RequireToken();
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
            IEnumerable<string>? responseInclude = null,
            IEnumerable<ProxyRouteConfigInputDto>? routes = null,
            ProxyAccessInputDto? access = null,
            ProxyResilienceInputDto? resilience = null,
            int? requestsPerMinute = null)
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
            result.Routes = NormalizeRoutes(routes, result);
            result.Access = NormalizeAccess(access, result);
            result.Resilience = NormalizeResilience(resilience, "this proxy", "resilience", result);
            result.RequestsPerMinute = NormalizeRequestsPerMinute(requestsPerMinute, result);

            return result;
        }

        /// <summary>The highest gateway calls-per-minute limit a tenant may set; the same ceiling as a function's (FN-19).</summary>
        public const int MaxRequestsPerMinute = 100_000;

        /// <summary>
        /// <c>null</c> keeps the default for the proxy's access. Zero or a negative number is refused rather
        /// than read as "no limit": a Public proxy must not be able to switch its limit off by accident.
        /// </summary>
        private static int? NormalizeRequestsPerMinute(int? value, ProxyConfigValidationResult result)
        {
            if (value is null) return null;

            if (value < 1 || value > MaxRequestsPerMinute)
            {
                result.Errors["requestsPerMinute"] =
                    $"The rate limit must be a whole number from 1 to {MaxRequestsPerMinute} calls per minute.";
                return null;
            }

            return value;
        }

        internal const int MaxAccessValues = 50;
        internal const int MaxAccessValueLength = 200;

        /// <summary>
        /// Normalizes "Who can call it". <c>kind</c> parses case-insensitively to <c>BlocksToken</c> (default)
        /// or <c>Public</c>; <c>combine</c> to <c>Or</c> (default) or <c>And</c>; a rule's <c>mode</c> to
        /// <c>any</c> (default) or <c>all</c>. Values are trimmed, blank-dropped and ordinal-deduped, capped at
        /// <see cref="MaxAccessValues"/> entries of at most <see cref="MaxAccessValueLength"/> characters, and may
        /// not contain a comma (the change history encodes a rule as a comma-separated list). A public policy
        /// with any role / permission value is rejected outright rather than having the lists dropped: the
        /// user asked for two contradictory things and should pick one.
        /// </summary>
        private static EndpointAccessPolicy NormalizeAccess(ProxyAccessInputDto? access, ProxyConfigValidationResult result)
        {
            var policy = EndpointAccessPolicy.RequireToken();
            if (access is null)
            {
                return policy;
            }

            var kind = (access.Kind ?? string.Empty).Trim();
            if (kind.Length > 0)
            {
                if (Enum.TryParse<EndpointAccessKind>(kind, ignoreCase: true, out var parsedKind))
                {
                    policy.Kind = parsedKind;
                }
                else
                {
                    result.Errors["access"] = "access.kind must be 'BlocksToken' or 'Public'.";
                }
            }

            var combine = (access.Combine ?? string.Empty).Trim();
            if (combine.Length > 0)
            {
                if (Enum.TryParse<EndpointAccessCombine>(combine, ignoreCase: true, out var parsedCombine))
                {
                    policy.Combine = parsedCombine;
                }
                else
                {
                    result.Errors["access"] = "access.combine must be 'Or' or 'And'.";
                }
            }

            policy.OrganizationId = (access.OrganizationId ?? string.Empty).Trim();
            policy.Roles = NormalizeAccessRule(access.Roles, "roles", result);
            policy.Permissions = NormalizeAccessRule(access.Permissions, "permissions", result);

            if (policy.IsPublic && policy.HasRestrictions)
            {
                result.Errors["access"] = "A public endpoint cannot be restricted by roles or permissions. Choose 'Blocks token' to restrict callers.";
            }

            return policy;
        }

        private static EndpointAccessRule NormalizeAccessRule(ProxyAccessRuleDto? rule, string label, ProxyConfigValidationResult result)
        {
            var normalized = new EndpointAccessRule { Mode = EndpointAccessRule.NormalizeMode(rule?.Mode) };
            if (rule?.Values is null)
            {
                return normalized;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in rule.Values)
            {
                var value = (raw ?? string.Empty).Trim();
                if (value.Length == 0 || !seen.Add(value))
                {
                    continue;
                }

                if (value.Length > MaxAccessValueLength)
                {
                    result.Errors[$"access.{label}"] = $"Each {label} entry is too long.";
                    continue;
                }

                if (value.Contains(',', StringComparison.Ordinal))
                {
                    result.Errors[$"access.{label}"] = $"A {label} entry may not contain a comma.";
                    continue;
                }

                normalized.Values.Add(value);
            }

            if (normalized.Values.Count > MaxAccessValues)
            {
                result.Errors[$"access.{label}"] = $"Too many {label} entries.";
            }

            return normalized;
        }

        /// <summary>
        /// Normalizes the route allowlist. Each route must target an allowed method and carry a well-formed
        /// path template; <c>(method, path)</c> must be unique, since two routes matching the same call would
        /// make the forward's behaviour depend on declaration order. Every parameter the upstream template
        /// uses must be declared by the client-facing one, or the rewrite would emit a literal <c>{name}</c>
        /// segment to the third party.
        /// <para>
        /// An override member is either <c>null</c> (inherit the shared value) or a value. Unlike headers and
        /// query, an explicitly empty <c>bodyMerge</c> / <c>responseInclude</c> is preserved as an override:
        /// "merge nothing" and "keep no fields" are meaningful for a route whose payload differs from the
        /// proxy-wide default, which is the case this whole layer exists to serve.
        /// </para>
        /// </summary>
        /// <summary>Bounds for the resilience settings. Ceilings, not defaults — nothing here is filled in.</summary>
        internal const int MaxTimeoutSeconds = 30;
        internal const int MaxRetryAttempts = 3;
        internal const int MaxRetryDelaySeconds = 30;
        internal const int MaxBreakerThreshold = 100;
        internal const int MaxBreakerOpenSeconds = 600;

        /// <summary>
        /// Turns the submitted resilience settings into a stored config, or <c>null</c> when nothing was
        /// asked for.
        /// <para>
        /// Nothing is defaulted. An absent member stays absent, and an object that asks for nothing comes
        /// back as <c>null</c> so it is stored exactly like "never configured" — which keeps a route that
        /// was saved through a form it did not fill in behaving as it always did.
        /// </para>
        /// <para>
        /// The ceiling on the timeout is not a product opinion. The gateway holds a connection and a
        /// request thread for the whole forward, so an unbounded value is a denial of service against this
        /// host; the tenant picks any value up to it.
        /// </para>
        /// </summary>
        internal static ProxyResilienceConfig? NormalizeResilience(
            ProxyResilienceInputDto? input, string where, string field, ProxyConfigValidationResult result)
        {
            if (input is null) return null;

            var config = new ProxyResilienceConfig();

            if (input.TimeoutSeconds is { } timeout)
            {
                if (timeout < 1 || timeout > MaxTimeoutSeconds)
                {
                    result.Errors[field] = $"The timeout for {where} must be between 1 and {MaxTimeoutSeconds} seconds.";
                    return null;
                }

                config.TimeoutSeconds = timeout;
            }

            if (input.Retry is { } retry)
            {
                var attempts = retry.Attempts ?? 1;
                if (attempts < 1 || attempts > MaxRetryAttempts)
                {
                    result.Errors[field] = $"Retry attempts for {where} must be between 1 and {MaxRetryAttempts}.";
                    return null;
                }

                // The rule this whole setting exists to enforce. The platform cannot tell whether an
                // upstream tolerates the same request twice, and guessing wrong bills someone twice, so
                // more than one attempt requires the tenant to say so in as many words.
                if (attempts > 1 && retry.Idempotent != true)
                {
                    result.Errors[field] =
                        $"Retries for {where} need 'idempotent' set to true — confirmation that sending this "
                        + "request more than once is safe. GET and HEAD usually are; POST usually is not.";
                    return null;
                }

                var backoff = ProxyBackoffKind.None;
                if (!string.IsNullOrWhiteSpace(retry.Backoff)
                    && !Enum.TryParse(retry.Backoff.Trim(), ignoreCase: true, out backoff))
                {
                    result.Errors[field] = $"The retry backoff for {where} must be 'None', 'Fixed' or 'Exponential'.";
                    return null;
                }

                var delay = retry.InitialDelaySeconds ?? 1;
                if (delay < 1 || delay > MaxRetryDelaySeconds)
                {
                    result.Errors[field] =
                        $"The retry delay for {where} must be at least one second and not too long.";
                    return null;
                }

                // One attempt is no retry at all, so it is stored as "not configured" rather than as a
                // policy that happens to do nothing.
                if (attempts > 1)
                {
                    config.Retry = new ProxyRetryConfig
                    {
                        Attempts = attempts,
                        Backoff = backoff,
                        InitialDelaySeconds = delay,
                        Idempotent = true,
                    };
                }
            }

            if (input.Breaker is { } breaker)
            {
                var threshold = breaker.FailureThreshold ?? 0;
                var openSeconds = breaker.OpenSeconds ?? 0;

                if (threshold < 1 || threshold > MaxBreakerThreshold)
                {
                    result.Errors[field] =
                        $"The breaker failure threshold for {where} must be at least one and not too high.";
                    return null;
                }

                if (openSeconds < 1 || openSeconds > MaxBreakerOpenSeconds)
                {
                    result.Errors[field] =
                        $"The breaker open duration for {where} must be at least one second and not too long.";
                    return null;
                }

                config.Breaker = new ProxyBreakerConfig
                {
                    FailureThreshold = threshold,
                    OpenSeconds = openSeconds,
                };
            }

            return config.IsEmpty ? null : config;
        }

        private static List<ProxyRouteConfig> NormalizeRoutes(
            IEnumerable<ProxyRouteConfigInputDto>? routes, ProxyConfigValidationResult result)
        {
            var normalized = new List<ProxyRouteConfig>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entry in routes ?? Enumerable.Empty<ProxyRouteConfigInputDto>())
            {
                if (entry is null)
                {
                    continue;
                }

                if (!HttpMethodTypeExtensions.TryParse(entry.Method, out var method)
                    || !result.Methods.Contains(method))
                {
                    result.Errors["routes"] = "Each route must target a method the proxy allows.";
                    continue;
                }

                var path = ProxyRoutePath.Normalize(entry.Path);
                if (!ProxyRoutePath.TryParseTemplate(path, out var parameters, out var pathReason))
                {
                    result.Errors["routes"] = pathReason;
                    continue;
                }

                if (!seen.Add($"{method.Wire()} {path}"))
                {
                    result.Errors["routes"] = $"Route '{method.Wire()} /{path}' is declared more than once.";
                    continue;
                }

                string? upstreamPath = null;
                if (entry.UpstreamPath is not null)
                {
                    upstreamPath = ProxyRoutePath.Normalize(entry.UpstreamPath);
                    if (!ProxyRoutePath.TryParseTemplate(upstreamPath, out var upstreamParameters, out var upstreamReason))
                    {
                        result.Errors["routes"] = upstreamReason;
                        continue;
                    }

                    var undeclared = upstreamParameters.FirstOrDefault(p => !parameters.Contains(p, StringComparer.Ordinal));
                    if (undeclared is not null)
                    {
                        result.Errors["routes"] =
                            $"Route '{method.Wire()} /{path}' rewrites to a parameter '{undeclared}' its path does not declare.";
                        continue;
                    }
                }

                // Validated under the route's own field kind so the dedupe and length rules still apply,
                // then re-filed under "routes": an error on a route's header must not light up the
                // proxy-wide Headers input in the console.
                var routeHeaders = entry.Headers is null ? null : NormalizeRoutePairs(entry.Headers, "headers", method, path, result);
                var routeQuery = entry.Query is null ? null : NormalizeRoutePairs(entry.Query, "query", method, path, result);
                var routeBodyMerge = entry.BodyMerge is null ? null : NormalizeRoutePairs(entry.BodyMerge, "bodyMerge", method, path, result);

                if (routeHeaders is { Count: 0 })
                {
                    routeHeaders = null;
                }

                if (routeQuery is { Count: 0 })
                {
                    routeQuery = null;
                }

                ProxyResponseMode? routeResponseMode = null;
                if (!string.IsNullOrWhiteSpace(entry.ResponseMode))
                {
                    if (Enum.TryParse<ProxyResponseMode>(entry.ResponseMode.Trim(), ignoreCase: true, out var parsedMode))
                    {
                        routeResponseMode = parsedMode;
                    }
                    else
                    {
                        result.Errors["routes"] = "A route responseMode must be 'All' or 'Select'.";
                        continue;
                    }
                }

                var routeResponseInclude = NormalizeRouteResponseInclude(entry.ResponseInclude, method, path, result);

                normalized.Add(new ProxyRouteConfig
                {
                    Method = method,
                    Path = path,
                    UpstreamPath = upstreamPath,
                    Headers = routeHeaders,
                    Query = routeQuery,
                    BodyMerge = routeBodyMerge,
                    ResponseMode = routeResponseMode,
                    ResponseInclude = routeResponseInclude,
                    Resilience = NormalizeResilience(
                        entry.Resilience, $"route '{method.Wire()} /{path}'", "routes", result),
                });
            }

            if (normalized.Count > ProxyRoutePath.MaxRoutes)
            {
                result.Errors["routes"] = "Too many routes.";
            }

            return normalized;
        }

        /// <summary>
        /// Applies the shared key/value rules to a route's pairs, but reports any failure against
        /// <c>routes</c> (naming the route) instead of the proxy-wide field the rule is borrowed from.
        /// </summary>
        private static List<ProxyKeyValue> NormalizeRoutePairs(
            IEnumerable<ProxyKeyValueInputDto> pairs, string field, HttpMethodType method, string path,
            ProxyConfigValidationResult result)
        {
            var scratch = new ProxyConfigValidationResult();
            var normalized = NormalizePairs(pairs, field, scratch);

            if (scratch.Errors.TryGetValue(field, out var reason))
            {
                result.Errors["routes"] = $"Route '{method.Wire()} /{path}': {reason}";
            }

            return normalized;
        }

        /// <summary>
        /// Trims, blank-drops and ordinal-dedupes a route's response paths, applying the same cap and path
        /// grammar as the proxy-wide list. <c>null</c> in ⇒ <c>null</c> out (inherit).
        /// </summary>
        private static List<string>? NormalizeRouteResponseInclude(
            IEnumerable<string>? responseInclude, HttpMethodType method, string path,
            ProxyConfigValidationResult result)
        {
            if (responseInclude is null)
            {
                return null;
            }

            var normalized = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in responseInclude)
            {
                var fieldPath = (raw ?? string.Empty).Trim();
                if (fieldPath.Length == 0 || !seen.Add(fieldPath))
                {
                    continue;
                }

                normalized.Add(fieldPath);
            }

            if (normalized.Count > ProxyResponsePath.MaxPaths)
            {
                result.Errors["routes"] = "Too many response fields on a route.";
                return normalized;
            }

            var invalid = normalized.FirstOrDefault(p => !ProxyResponsePath.IsValid(p));
            if (invalid is not null)
            {
                result.Errors["routes"] = $"Route '{method.Wire()} /{path}' has an invalid response field path '{invalid}'.";
            }

            return normalized;
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
                result.Errors["responseInclude"] = "Too many response fields.";
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
                result.Errors["name"] = "Name is required and must not be too long.";
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
                result.Errors["upstream"] = "Upstream URL is too long.";
                return;
            }

            // PS-11: a password in the URL would show in the masked view and in every log row.
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed) && !string.IsNullOrEmpty(parsed.UserInfo))
            {
                result.Errors["upstream"] =
                    "Do not put a user name or password in the URL. Send it as a header with a {{$VAR}} secret.";
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

        /// <summary>
        /// <c>true</c> when <paramref name="value"/> is an absolute <c>https://</c> URL with a host and no user
        /// info (PS-11: a credential in the URL is never accepted, on the proxy or on a per-method override).
        /// </summary>
        internal static bool IsAbsoluteHttps(string value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrEmpty(uri.Host)
            && string.IsNullOrEmpty(uri.UserInfo);

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

                var overrideHeaders = entry.Headers is null ? null : NormalizePairs(entry.Headers, "methodConfigs", result, areHeaders: true);
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
            IEnumerable<ProxyKeyValueInputDto>? pairs, string field, ProxyConfigValidationResult result,
            bool areHeaders = false)
        {
            areHeaders |= field == "headers";
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
                        $"Each {field} key is required, must not be too long, and must not have leading or trailing whitespace.";
                }
                else if (areHeaders && ProxyReservedHeaders.IsReserved(key))
                {
                    // PS-10: these steer where and how the request travels; Blocks sets them, the config may not.
                    result.Errors[field] =
                        $"The header \"{key.Trim()}\" cannot be set by a proxy. Not allowed: {ProxyReservedHeaders.Description}.";
                }
                else if (areHeaders && !seenKeys.Add(key.Trim()))
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
                    result.Errors[field] = $"Each {field} value is too long.";
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
