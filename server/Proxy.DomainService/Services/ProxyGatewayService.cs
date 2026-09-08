using System.Diagnostics;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Repositories;
using Proxy.DomainService.Utils;

namespace Proxy.DomainService.Services
{
    /// <summary>
    /// Buffered, non-streaming forwarder. Correctness and a fully-audited single round-trip take priority over
    /// throughput: one request in, one <see cref="ProxyExecutionEntity"/> written (for data-plane calls), one
    /// response relayed. See <see cref="IProxyGatewayService"/> and SPEC &sect;2A for the pipeline.
    /// </summary>
    public sealed class ProxyGatewayService : IProxyGatewayService
    {
        /// <summary>Named <see cref="HttpClient"/> configured in <c>AddProxyServices()</c> (30 s timeout, no redirects).</summary>
        public const string UpstreamClientName = "proxy-upstream";

        private const long MaxBodyBytes = 10L * 1024 * 1024;

        /// <summary>
        /// Hard cap on the buffered upstream <em>response</em>. Mirrors <see cref="MaxBodyBytes"/>; also set as
        /// the named client's <see cref="System.Net.Http.HttpClient.MaxResponseContentBufferSize"/> backstop.
        /// </summary>
        public const long MaxResponseBodyBytes = 10L * 1024 * 1024;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IProxyRepository _proxyRepository;
        private readonly IProxyExecutionRepository _executionRepository;
        private readonly IProxySecretResolver _secretResolver;
        private readonly IProxyUpstreamGuard _upstreamGuard;
        private readonly ILogger<ProxyGatewayService> _logger;

        public ProxyGatewayService(
            IHttpClientFactory httpClientFactory,
            IProxyRepository proxyRepository,
            IProxyExecutionRepository executionRepository,
            IProxySecretResolver secretResolver,
            IProxyUpstreamGuard upstreamGuard,
            ILogger<ProxyGatewayService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _proxyRepository = proxyRepository;
            _executionRepository = executionRepository;
            _secretResolver = secretResolver;
            _upstreamGuard = upstreamGuard;
            _logger = logger;
        }

        /// <summary>The hard inbound-body cap; also enforced by the controller before the body is read.</summary>
        public static long MaxRequestBodyBytes => MaxBodyBytes;

        public async Task<ProxyForwardResult> ForwardAsync(ProxyForwardRequest request, CancellationToken cancellationToken = default)
        {
            var startedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "Proxy gateway: {Mode} {Method} slug '{Slug}' path '{Path}' for tenant {TenantId} (user {UserId}).",
                request.IsTest ? "test" : "forward", request.Method, request.Slug, request.PathSuffix,
                request.TenantId, request.UserId ?? "(anonymous)");

            var config = request.ResolvedConfig;
            if (config is null)
            {
                var proxy = await _proxyRepository.GetBySlugAsync(request.TenantId, request.Slug);
                if (proxy is not null)
                {
                    config = ProxyResolvedConfig.FromEntity(proxy);
                }
            }

            // --- pre-flight rejections: a row is written but no upstream call is made ---

            if (config is null || !config.Enabled)
            {
                _logger.LogWarning(
                    "Proxy gateway: slug '{Slug}' not found or disabled for tenant {TenantId}; returning 404.",
                    request.Slug, request.TenantId);
                return await FinalizeAsync(request, config, BuildPreflight(
                    ProxyExecutionOutcome.ProxyNotFound, 404, startedAt));
            }

            var allowedWire = config.Methods.Select(m => m.Wire()).ToList();
            if (!HttpMethodTypeExtensions.TryParse(request.Method, out var requestedMethod)
                || !config.Methods.Contains(requestedMethod))
            {
                _logger.LogWarning(
                    "Proxy gateway: method {Method} not in [{Allowed}] for slug '{Slug}' (tenant {TenantId}); returning 405.",
                    request.Method, string.Join(", ", allowedWire), config.Slug, request.TenantId);
                return await FinalizeAsync(request, config, BuildPreflight(
                    ProxyExecutionOutcome.MethodNotAllowed, 405, startedAt, allowedMethods: allowedWire));
            }

            if (request.BodyTooLarge || (request.Body?.LongLength ?? 0) > MaxBodyBytes)
            {
                _logger.LogWarning(
                    "Proxy gateway: request body exceeds {Cap} bytes for slug '{Slug}' (tenant {TenantId}); returning 413, no upstream call.",
                    MaxBodyBytes, config.Slug, request.TenantId);
                return await FinalizeAsync(request, config, BuildPreflight(
                    ProxyExecutionOutcome.RequestTooLarge, 413, startedAt));
            }

            // --- forward ---

            // Resolve the effective config for this method: a per-method override wins field-by-field, a null
            // member inherits the shared value. With MethodConfigs empty this is exactly config.* (today).
            var (effHeaders, effQuery, effUpstream) = ResolveEffective(config, requestedMethod);

            // Request-body merge: with BodyMerge configured and a body-bearing method, parse the client's JSON
            // body, set each configured key at the top level (overriding the client), re-serialise and forward
            // as application/json. Empty BodyMerge ⇒ the body is relayed byte-for-byte (effBody == request.Body).
            var effBody = request.Body;
            var effContentType = request.ContentType;

            var mergeApplies = config.BodyMerge.Count > 0
                && requestedMethod is HttpMethodType.Post or HttpMethodType.Put or HttpMethodType.Patch;

            if (mergeApplies)
            {
                var merge = ProxyBodyMerger.Merge(request.Body, config.BodyMerge, _secretResolver);
                if (merge.NotMergeable)
                {
                    _logger.LogWarning(
                        "Proxy gateway: request body for slug '{Slug}' (tenant {TenantId}) is not a JSON object; returning 422, no upstream call.",
                        config.Slug, request.TenantId);
                    return await FinalizeAsync(request, config, BuildPreflight(
                        ProxyExecutionOutcome.RequestBodyNotMergeable, 422, startedAt));
                }

                if ((merge.Body?.LongLength ?? 0) > MaxBodyBytes)
                {
                    _logger.LogWarning(
                        "Proxy gateway: merged request body exceeds {Cap} bytes for slug '{Slug}' (tenant {TenantId}); returning 413, no upstream call.",
                        MaxBodyBytes, config.Slug, request.TenantId);
                    return await FinalizeAsync(request, config, BuildPreflight(
                        ProxyExecutionOutcome.RequestTooLarge, 413, startedAt));
                }

                effBody = merge.Body;
                effContentType = "application/json";
            }

            var (storedUrl, outboundUrl, injectedQueryKeys) = BuildUrls(effUpstream, effQuery, request);
            var upstreamHost = SafeHost(outboundUrl);
            // Best-effort list for failure rows; replaced with the keys that actually attached once the
            // request message is built (a malformed key is dropped rather than sent).
            IReadOnlyList<string> injectedHeaderKeys = effHeaders.Select(h => h.Key).ToList();

            var stopwatch = Stopwatch.StartNew();

            // SSRF re-check just before the send: a hostname that validated public at config-write time may
            // now resolve to a private / loopback / link-local address (DNS rebinding). Refuse the call.
            if (await _upstreamGuard.IsTargetBlockedAsync(outboundUrl, cancellationToken))
            {
                stopwatch.Stop();
                _logger.LogWarning(
                    "Proxy gateway: upstream {Host} for slug '{Slug}' (tenant {TenantId}) resolves to a blocked address; returning 502.",
                    upstreamHost, config.Slug, request.TenantId);
                return await FinalizeAsync(request, config, BuildFailure(
                    ProxyExecutionOutcome.UpstreamBlocked, 502, startedAt, stopwatch, storedUrl, upstreamHost,
                    injectedHeaderKeys, injectedQueryKeys, "The upstream endpoint is not an allowed destination"));
            }

            HttpResponseMessage response;
            try
            {
                // Factory clients are pooled and cheap; do NOT dispose per call.
                var client = _httpClientFactory.CreateClient(UpstreamClientName);
                using var message = BuildUpstreamRequest(
                    effHeaders, request, effBody, effContentType, outboundUrl, out var attachedHeaderKeys);
                injectedHeaderKeys = attachedHeaderKeys;
                response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                _logger.LogInformation(
                    "Proxy gateway: client aborted {Method} slug '{Slug}' (tenant {TenantId}); no row written.",
                    request.Method, config.Slug, request.TenantId);
                throw; // let the framework drop the aborted request; do NOT persist a row
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                _logger.LogWarning(
                    "Proxy gateway: upstream {Host} did not respond within 30s for slug '{Slug}' (tenant {TenantId}); returning 504.",
                    upstreamHost, config.Slug, request.TenantId);
                return await FinalizeAsync(request, config, BuildFailure(
                    ProxyExecutionOutcome.Timeout, 504, startedAt, stopwatch, storedUrl, upstreamHost,
                    injectedHeaderKeys, injectedQueryKeys, "Upstream did not respond within 30s"));
            }
            catch (HttpRequestException ex) when (IsResponseTooLarge(ex))
            {
                stopwatch.Stop();
                _logger.LogWarning(
                    "Proxy gateway: upstream {Host} response exceeded the {Cap} byte cap for slug '{Slug}' (tenant {TenantId}); returning 502.",
                    upstreamHost, MaxResponseBodyBytes, config.Slug, request.TenantId);
                return await FinalizeAsync(request, config, BuildFailure(
                    ProxyExecutionOutcome.UpstreamResponseTooLarge, 502, startedAt, stopwatch, storedUrl, upstreamHost,
                    injectedHeaderKeys, injectedQueryKeys, "Upstream response exceeded the 10 MB limit"));
            }
            catch (HttpRequestException ex)
            {
                stopwatch.Stop();
                _logger.LogWarning(ex,
                    "Proxy gateway: upstream {Host} unreachable for slug '{Slug}' (tenant {TenantId}); returning 502.",
                    upstreamHost, config.Slug, request.TenantId);
                return await FinalizeAsync(request, config, BuildFailure(
                    ProxyExecutionOutcome.UpstreamUnreachable, 502, startedAt, stopwatch, storedUrl, upstreamHost,
                    injectedHeaderKeys, injectedQueryKeys, "Could not connect to the upstream endpoint"));
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex,
                    "Proxy gateway: unexpected forwarder error for slug '{Slug}' (tenant {TenantId}); returning 500.",
                    config.Slug, request.TenantId);
                return await FinalizeAsync(request, config, BuildFailure(
                    ProxyExecutionOutcome.InternalError, 500, startedAt, stopwatch, storedUrl, upstreamHost,
                    injectedHeaderKeys, injectedQueryKeys, "The proxy forwarder encountered an unexpected error"));
            }

            using (response)
            {
                // Cheap pre-read guard: a declared Content-Length over the cap is refused before any
                // large allocation. Chunked / under-declared bodies are still caught after the read (and by
                // the client's MaxResponseContentBufferSize backstop, mapped in the catch above).
                if (response.Content.Headers.ContentLength is > MaxResponseBodyBytes)
                {
                    stopwatch.Stop();
                    _logger.LogWarning(
                        "Proxy gateway: upstream {Host} declared a {Declared} byte body over the {Cap} byte cap for slug '{Slug}' (tenant {TenantId}); returning 502.",
                        upstreamHost, response.Content.Headers.ContentLength, MaxResponseBodyBytes, config.Slug, request.TenantId);
                    return await FinalizeAsync(request, config, BuildFailure(
                        ProxyExecutionOutcome.UpstreamResponseTooLarge, 502, startedAt, stopwatch, storedUrl, upstreamHost,
                        injectedHeaderKeys, injectedQueryKeys, "Upstream response exceeded the 10 MB limit"));
                }

                byte[] bytes;
                try
                {
                    bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                }
                catch (HttpRequestException ex) when (IsResponseTooLarge(ex))
                {
                    stopwatch.Stop();
                    _logger.LogWarning(
                        "Proxy gateway: upstream {Host} body passed the {Cap} byte cap for slug '{Slug}' (tenant {TenantId}); returning 502.",
                        upstreamHost, MaxResponseBodyBytes, config.Slug, request.TenantId);
                    return await FinalizeAsync(request, config, BuildFailure(
                        ProxyExecutionOutcome.UpstreamResponseTooLarge, 502, startedAt, stopwatch, storedUrl, upstreamHost,
                        injectedHeaderKeys, injectedQueryKeys, "Upstream response exceeded the 10 MB limit"));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    stopwatch.Stop();
                    _logger.LogInformation(
                        "Proxy gateway: client aborted {Method} slug '{Slug}' (tenant {TenantId}) while the upstream body was read; no row written.",
                        request.Method, config.Slug, request.TenantId);
                    throw; // let the framework drop the aborted request; do NOT persist a row
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    stopwatch.Stop();
                    _logger.LogWarning(
                        "Proxy gateway: reading the upstream body from {Host} timed out for slug '{Slug}' (tenant {TenantId}); returning 504.",
                        upstreamHost, config.Slug, request.TenantId);
                    return await FinalizeAsync(request, config, BuildFailure(
                        ProxyExecutionOutcome.Timeout, 504, startedAt, stopwatch, storedUrl, upstreamHost,
                        injectedHeaderKeys, injectedQueryKeys, "Upstream did not respond within 30s"));
                }

                stopwatch.Stop();

                // Post-read guard for a chunked / under-declared body that slipped past both checks above.
                if (bytes.LongLength > MaxResponseBodyBytes)
                {
                    _logger.LogWarning(
                        "Proxy gateway: upstream {Host} returned {Bytes} B over the {Cap} byte cap for slug '{Slug}' (tenant {TenantId}); returning 502.",
                        upstreamHost, bytes.LongLength, MaxResponseBodyBytes, config.Slug, request.TenantId);
                    return await FinalizeAsync(request, config, BuildFailure(
                        ProxyExecutionOutcome.UpstreamResponseTooLarge, 502, startedAt, stopwatch, storedUrl, upstreamHost,
                        injectedHeaderKeys, injectedQueryKeys, "Upstream response exceeded the 10 MB limit"));
                }

                var contentType = response.Content.Headers.ContentType?.ToString();
                var status = (int)response.StatusCode;
                var finishedAt = startedAt.AddMilliseconds(stopwatch.Elapsed.TotalMilliseconds);

                var result = new ProxyForwardResult
                {
                    StatusCode = status,
                    Outcome = ProxyExecutionOutcome.Success,
                    UpstreamStatusCode = status,
                    UpstreamUrl = storedUrl,
                    UpstreamHost = upstreamHost,
                    InjectedHeaderKeys = injectedHeaderKeys,
                    InjectedQueryKeys = injectedQueryKeys,
                    LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                    ResponseBody = ExecutionBodyStore.Capture(bytes, contentType),
                    ResponseBodyBytes = bytes.LongLength,
                    ResponseContentType = contentType,
                    ResponseBytes = bytes,
                    ErrorMessage = null,
                    StartedAtUtc = startedAt,
                    FinishedAtUtc = finishedAt,
                };

                var persisted = await FinalizeAsync(request, config, result);
                _logger.LogInformation(
                    "Proxy gateway: relayed {Method} {Host} slug '{Slug}' (tenant {TenantId}) -> {Status}, {Bytes} B in {LatencyMs} ms.",
                    request.Method, upstreamHost, config.Slug, request.TenantId, status, bytes.LongLength, result.LatencyMs);
                return persisted;
            }
        }

        /// <summary>Resolves the effective headers / query / upstream for <paramref name="method"/>: a
        /// per-method override wins field-by-field, a <c>null</c> member inherits the shared value.</summary>
        private static (IReadOnlyList<ProxyKeyValue> Headers, IReadOnlyList<ProxyKeyValue> Query, string Upstream) ResolveEffective(
            ProxyResolvedConfig config, HttpMethodType method)
        {
            var over = config.MethodConfigs.FirstOrDefault(c => c.Method == method);
            return (
                over?.Headers ?? config.Headers,
                over?.Query ?? config.Query,
                over?.Upstream ?? config.Upstream);
        }

        private HttpRequestMessage BuildUpstreamRequest(
            IReadOnlyList<ProxyKeyValue> headers, ProxyForwardRequest request, byte[]? body, string? contentType,
            string outboundUrl, out List<string> attachedHeaderKeys)
        {
            var message = new HttpRequestMessage(new HttpMethod(request.Method), outboundUrl);

            // note: a zero-length body is delivered as body == null (ReadBodyAsync collapses "empty" to null,
            // and a body-merge that produced nothing never gets here), so a deliberate POST/PUT with an empty
            // body forwards with no Content-Type / Content-Length: 0. Threading an "empty vs absent" signal out
            // of ReadBodyAsync is deferred until a real upstream needs it.
            if (body is { Length: > 0 })
            {
                var content = new ByteArrayContent(body);
                if (!string.IsNullOrWhiteSpace(contentType))
                {
                    if (MediaTypeHeaderValue.TryParse(contentType, out var parsed))
                    {
                        content.Headers.ContentType = parsed;
                    }
                    else
                    {
                        // Malformed Content-Type (e.g. "application/json; charset="): relay it verbatim
                        // rather than dropping it, so the upstream still sees the caller's declared type.
                        content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                    }
                }

                message.Content = content;
            }

            // Attach ONLY the configured headers (values materialised through the secret-resolver seam).
            // Track the keys that actually attached so the audit list on the execution row is truthful.
            attachedHeaderKeys = new List<string>(headers.Count);
            foreach (var header in headers)
            {
                var value = _secretResolver.Resolve(header.Value);
                if (message.Headers.TryAddWithoutValidation(header.Key, value)
                    || (message.Content is not null
                        && message.Content.Headers.TryAddWithoutValidation(header.Key, value)))
                {
                    attachedHeaderKeys.Add(header.Key);
                }
            }

            return message;
        }

        /// <summary>
        /// Rebuilds the target URL. Returns the URL to STORE (secret-ref query values keep their
        /// <c>${SECRET.NAME}</c> token), the URL to CALL (values materialised through the resolver), and the
        /// list of query keys Blocks injected.
        /// </summary>
        private (string StoredUrl, string OutboundUrl, List<string> InjectedQueryKeys) BuildUrls(
            string upstream, IReadOnlyList<ProxyKeyValue> query, ProxyForwardRequest request)
        {
            var target = upstream.TrimEnd('/');
            if (!string.IsNullOrEmpty(request.PathSuffix))
            {
                // {**path} arrives URL-decoded from routing. Re-encode per segment so a space / unicode /
                // reserved char yields a valid URI (symmetric with the query handling below) instead of
                // throwing in the HttpRequestMessage ctor and being misreported as 500 with a row.
                // Limitation: a literal %2F in the inbound raw path has already been decoded to '/' by
                // routing, so encoded slashes cannot be round-tripped from the action arg alone.
                var encodedSuffix = string.Join('/', request.PathSuffix
                    .Split('/')
                    .Select(Uri.EscapeDataString));
                target += "/" + encodedSuffix.TrimStart('/');
            }

            var configuredKeys = new HashSet<string>(query.Select(q => q.Key), StringComparer.Ordinal);

            // Incoming params: keep every key that a configured Query entry does NOT override (H7). They were
            // URL-decoded by the parser, so they are re-encoded on the way back out via Uri.EscapeDataString
            // ('+' -> %20, sub-delims encoded). Signed-query-string upstreams (OAuth1, some HMAC schemes) that
            // depend on byte-exact query preservation are therefore unsupported in this phase.
            var storedParts = new List<string>();
            var outboundParts = new List<string>();
            foreach (var pair in QueryHelpers.ParseQuery(request.IncomingQuery ?? string.Empty))
            {
                if (configuredKeys.Contains(pair.Key))
                {
                    continue;
                }

                foreach (var value in (StringValues)pair.Value)
                {
                    var encoded = $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(value ?? string.Empty)}";
                    storedParts.Add(encoded);
                    outboundParts.Add(encoded);
                }
            }

            // Configured params override on collision. Phase 2 sends values verbatim (identity resolver) and
            // stores secret-ref entries with their ${SECRET.NAME} token, never a resolved value (C7).
            var injectedQueryKeys = new List<string>();
            foreach (var configured in query)
            {
                injectedQueryKeys.Add(configured.Key);
                var key = Uri.EscapeDataString(configured.Key);
                var outboundValue = _secretResolver.Resolve(configured.Value);
                outboundParts.Add(configured.IsSecretRef
                    ? $"{key}={outboundValue}"
                    : $"{key}={Uri.EscapeDataString(outboundValue)}");
                storedParts.Add(configured.IsSecretRef
                    ? $"{key}={configured.Value}"
                    : $"{key}={Uri.EscapeDataString(outboundValue)}");
            }

            var storedUrl = storedParts.Count == 0 ? target : target + "?" + string.Join("&", storedParts);
            var outboundUrl = outboundParts.Count == 0 ? target : target + "?" + string.Join("&", outboundParts);
            return (storedUrl, outboundUrl, injectedQueryKeys);
        }

        private static string SafeHost(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;

        /// <summary>
        /// <c>true</c> when <paramref name="ex"/> is the <see cref="HttpClient.MaxResponseContentBufferSize"/>
        /// overflow (message: "Cannot write more bytes to the buffer than the configured maximum buffer
        /// size"), as opposed to a genuine connect / transport failure.
        /// </summary>
        private static bool IsResponseTooLarge(Exception ex)
        {
            for (var e = ex; e is not null; e = e.InnerException)
            {
                if (e.Message.Contains("maximum buffer size", StringComparison.OrdinalIgnoreCase)
                    || e.Message.Contains("Cannot write more bytes to the buffer", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static ProxyForwardResult BuildPreflight(
            string outcome, int statusCode, DateTime startedAt, IReadOnlyList<string>? allowedMethods = null) => new()
        {
            StatusCode = statusCode,
            Outcome = outcome,
            UpstreamStatusCode = null,
            UpstreamUrl = string.Empty,
            UpstreamHost = string.Empty,
            InjectedHeaderKeys = Array.Empty<string>(),
            InjectedQueryKeys = Array.Empty<string>(),
            LatencyMs = 0,
            ResponseBody = null,
            ResponseBodyBytes = 0,
            ResponseContentType = null,
            ResponseBytes = null,
            ErrorMessage = null,
            AllowedMethods = allowedMethods ?? Array.Empty<string>(),
            StartedAtUtc = startedAt,
            FinishedAtUtc = startedAt,
        };

        private static ProxyForwardResult BuildFailure(
            string outcome, int statusCode, DateTime startedAt, Stopwatch stopwatch,
            string storedUrl, string upstreamHost,
            IReadOnlyList<string> injectedHeaderKeys, IReadOnlyList<string> injectedQueryKeys,
            string errorMessage) => new()
        {
            StatusCode = statusCode,
            Outcome = outcome,
            UpstreamStatusCode = null,
            UpstreamUrl = storedUrl,
            UpstreamHost = upstreamHost,
            InjectedHeaderKeys = injectedHeaderKeys,
            InjectedQueryKeys = injectedQueryKeys,
            LatencyMs = (int)stopwatch.ElapsedMilliseconds,
            ResponseBody = null,
            ResponseBodyBytes = 0,
            ResponseContentType = null,
            ResponseBytes = null,
            ErrorMessage = errorMessage,
            StartedAtUtc = startedAt,
            FinishedAtUtc = startedAt.AddMilliseconds(stopwatch.Elapsed.TotalMilliseconds),
        };

        /// <summary>
        /// Persists exactly one execution row for a data-plane call (never for a test), then returns
        /// <paramref name="result"/> unchanged. A persistence failure is logged and swallowed so the client
        /// round-trip is never sacrificed to logging (C9).
        /// </summary>
        private async Task<ProxyForwardResult> FinalizeAsync(
            ProxyForwardRequest request, ProxyResolvedConfig? config, ProxyForwardResult result)
        {
            if (request.IsTest)
            {
                return result;
            }

            var entity = new ProxyExecutionEntity
            {
                ItemId = Guid.NewGuid().ToString("N"),
                TenantId = request.TenantId,
                ProxyId = config?.ProxyId ?? string.Empty,
                ProxySlug = config?.Slug is { Length: > 0 } slug ? slug : request.Slug,
                IsTest = false,
                RequestMethod = request.Method,
                RequestPath = request.RequestPath,
                RequestQuery = request.IncomingQuery ?? string.Empty,
                UpstreamUrl = result.UpstreamUrl,
                UpstreamHost = result.UpstreamHost,
                InjectedHeaderKeys = result.InjectedHeaderKeys.ToList(),
                InjectedQueryKeys = result.InjectedQueryKeys.ToList(),
                StatusCode = result.StatusCode,
                UpstreamStatusCode = result.UpstreamStatusCode,
                Outcome = result.Outcome,
                LatencyMs = result.LatencyMs,
                ResponseBodyBytes = result.ResponseBodyBytes,
                ResponseBody = result.ResponseBody,
                ResponseContentType = result.ResponseContentType,
                ErrorMessage = result.ErrorMessage,
                StartedAtUtc = result.StartedAtUtc,
                FinishedAtUtc = result.FinishedAtUtc,
                CreatedDate = result.StartedAtUtc,
                LastUpdatedDate = result.FinishedAtUtc,
                CreatedBy = request.UserId,
                LastUpdatedBy = request.UserId,
            };

            try
            {
                await _executionRepository.InsertAsync(entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Proxy gateway: failed to persist the execution row for slug '{Slug}' (tenant {TenantId}); the upstream response is still relayed.",
                    entity.ProxySlug, request.TenantId);
            }

            return result;
        }
    }
}
