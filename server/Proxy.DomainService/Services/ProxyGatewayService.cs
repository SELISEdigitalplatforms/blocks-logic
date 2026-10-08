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

        /// <summary>
        /// Hard cap on the inbound <em>request</em> body. 1 MB: the proxy carries API calls, not file uploads
        /// (PX-7, user decision 2026-10-07), and a public proxy must not let anyone park 10 MB per call in memory.
        /// </summary>
        private const long MaxBodyBytes = 1L * 1024 * 1024;

        /// <summary>
        /// Hard cap on the buffered upstream <em>response</em>: 5 MB, the same as
        /// <see cref="Utils.ProxyResponseProjector.MaxProjectableBytes"/>, so a body the gateway accepts can always be
        /// filtered (PX-7, 2026-10-07). Also set as the named client's
        /// <see cref="System.Net.Http.HttpClient.MaxResponseContentBufferSize"/> backstop.
        /// </summary>
        public const long MaxResponseBodyBytes = 5L * 1024 * 1024;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IProxyRepository _proxyRepository;
        private readonly IProxyExecutionRepository _executionRepository;
        private readonly IProxyVariableResolver _variableResolver;
        private readonly IProxyUpstreamGuard _upstreamGuard;
        private readonly IProxyStatsRecorder _statsRecorder;
        private readonly IProxyCircuitBreaker _breaker;
        private readonly ILogger<ProxyGatewayService> _logger;

        public ProxyGatewayService(
            IHttpClientFactory httpClientFactory,
            IProxyRepository proxyRepository,
            IProxyExecutionRepository executionRepository,
            IProxyVariableResolver variableResolver,
            IProxyUpstreamGuard upstreamGuard,
            IProxyStatsRecorder statsRecorder,
            ILogger<ProxyGatewayService> logger,
            // Optional so every existing construction of this service keeps compiling and behaving
            // identically: with no breaker configured on a proxy, this is never consulted anyway.
            IProxyCircuitBreaker? breaker = null)
        {
            _httpClientFactory = httpClientFactory;
            _proxyRepository = proxyRepository;
            _executionRepository = executionRepository;
            _variableResolver = variableResolver;
            _upstreamGuard = upstreamGuard;
            _statsRecorder = statsRecorder;
            _breaker = breaker ?? new ProxyCircuitBreaker();
            _logger = logger;
        }

        /// <summary>The hard inbound-body cap; also enforced by the controller before the body is read.</summary>
        public static long MaxRequestBodyBytes => MaxBodyBytes;

        public async Task<ProxyResolvedConfig?> ResolveAsync(string tenantId, string slug, CancellationToken cancellationToken = default)
        {
            var proxy = await _proxyRepository.GetBySlugAsync(tenantId, slug);
            return proxy is null ? null : ProxyResolvedConfig.FromEntity(proxy);
        }

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
                return await FinalizeAsync(request, config, null, BuildPreflight(
                    ProxyExecutionOutcome.ProxyNotFound, 404, startedAt));
            }

            // Access policy refusal, decided by the controller (it holds the HttpRequest; this forwarder also
            // serves in-process workflow calls that have none). Recorded like RouteNotAllowed: a row, no upstream.
            if (request.ForbiddenReason is not null)
            {
                _logger.LogWarning(
                    "Proxy gateway: caller {UserId} refused by the access policy of slug '{Slug}' (tenant {TenantId}); returning 403, no upstream call.",
                    request.UserId ?? "(anonymous)", config.Slug, request.TenantId);
                return await FinalizeAsync(request, config, null, BuildPreflight(
                    ProxyExecutionOutcome.Forbidden, 403, startedAt, errorMessage: request.ForbiddenReason));
            }

            var allowedWire = config.Methods.Select(m => m.Wire()).ToList();
            if (!HttpMethodTypeExtensions.TryParse(request.Method, out var requestedMethod)
                || !config.Methods.Contains(requestedMethod))
            {
                _logger.LogWarning(
                    "Proxy gateway: method {Method} not in [{Allowed}] for slug '{Slug}' (tenant {TenantId}); returning 405.",
                    request.Method, string.Join(", ", allowedWire), config.Slug, request.TenantId);
                return await FinalizeAsync(request, config, null, BuildPreflight(
                    ProxyExecutionOutcome.MethodNotAllowed, 405, startedAt, allowedMethods: allowedWire));
            }

            if (request.BodyTooLarge || (request.Body?.LongLength ?? 0) > MaxBodyBytes)
            {
                _logger.LogWarning(
                    "Proxy gateway: request body exceeds {Cap} bytes for slug '{Slug}' (tenant {TenantId}); returning 413, no upstream call.",
                    MaxBodyBytes, config.Slug, request.TenantId);
                return await FinalizeAsync(request, config, null, BuildPreflight(
                    ProxyExecutionOutcome.RequestTooLarge, 413, startedAt));
            }

            // Route allowlist. The caller never holds the upstream credential, so the set of endpoints that
            // credential can be pointed at has to be declared by the tenant rather than chosen by the caller.
            // An empty route list means "base path only", which keeps a proxy one-to-one with one endpoint.
            var route = ProxyRouteResolver.Resolve(config.Routes, requestedMethod, request.PathSuffix);

            if (route.Status == ProxyRouteStatus.MethodMismatch)
            {
                _logger.LogWarning(
                    "Proxy gateway: path '{Path}' on slug '{Slug}' (tenant {TenantId}) is declared for [{Allowed}], not {Method}; returning 405.",
                    request.PathSuffix, config.Slug, request.TenantId, string.Join(", ", route.AllowedMethods), request.Method);
                return await FinalizeAsync(request, config, route, BuildPreflight(
                    ProxyExecutionOutcome.MethodNotAllowed, 405, startedAt, allowedMethods: route.AllowedMethods));
            }

            if (route.Status == ProxyRouteStatus.NotAllowed)
            {
                _logger.LogWarning(
                    "Proxy gateway: path '{Path}' is not a declared route on slug '{Slug}' (tenant {TenantId}); returning 403, no upstream call.",
                    request.PathSuffix, config.Slug, request.TenantId);
                return await FinalizeAsync(request, config, route, BuildPreflight(
                    ProxyExecutionOutcome.RouteNotAllowed, 403, startedAt,
                    errorMessage: config.Routes.Count == 0
                        ? "This proxy accepts its base path only; no routes are configured."
                        : "The requested path is not a configured route on this proxy."));
            }

            // --- forward ---

            // Resolve the effective config for this call: the matched route wins field-by-field, then the
            // per-method override, then the shared value. With no routes and no MethodConfigs this is exactly
            // config.* (today's behaviour).
            var effective = ResolveEffective(config, requestedMethod, route.Route);
            var (effHeaders, effQuery, effUpstream) = (effective.Headers, effective.Query, effective.Upstream);

            // Collect every {{$VAR.name}} across the fields we will actually send and resolve them once,
            // batched, from Blocks Secrets. The resolved values are substituted into the outbound request in
            // memory only — never stored, logged, or returned. A resolution failure fails the call before any
            // upstream connection (an execution row is still written).
            var bodyMergeForVars = requestedMethod is HttpMethodType.Post or HttpMethodType.Put or HttpMethodType.Patch
                && effective.BodyMerge.Count > 0
                    ? effective.BodyMerge
                    : null;
            var varNames = ProxyVarRef.Names(effHeaders, effQuery, bodyMergeForVars).ToArray();

            IReadOnlyDictionary<string, string> vars = ProxyVariableResolver.EmptyMap;
            if (varNames.Length > 0)
            {
                try
                {
                    // Ids stored at save (PX-9): read by id at once, no search per call.
                    vars = await _variableResolver.ResolveAsync(varNames, request.TenantId, config.SecretIds, cancellationToken);
                }
                catch (ProxyVariableResolutionException ex)
                {
                    _logger.LogWarning(
                        "Proxy gateway: could not resolve configuration variable(s) [{Names}] for slug '{Slug}' (tenant {TenantId}); returning 502, no upstream call.",
                        string.Join(", ", ex.Names), config.Slug, request.TenantId);
                    return await FinalizeAsync(request, config, route, BuildPreflight(
                        ProxyExecutionOutcome.VariableResolutionFailed, 502, startedAt,
                        errorMessage: $"Could not resolve configuration variable(s): {string.Join(", ", ex.Names)}"));
                }
            }

            // Request-body merge: with BodyMerge configured and a body-bearing method, parse the client's JSON
            // body, set each configured key at the top level (overriding the client), re-serialise and forward
            // as application/json. Empty BodyMerge ⇒ the body is relayed byte-for-byte (effBody == request.Body).
            var effBody = request.Body;
            var effContentType = request.ContentType;

            var mergeApplies = effective.BodyMerge.Count > 0
                && requestedMethod is HttpMethodType.Post or HttpMethodType.Put or HttpMethodType.Patch;

            if (mergeApplies)
            {
                var merge = ProxyBodyMerger.Merge(request.Body, effective.BodyMerge, vars);
                if (merge.NotMergeable)
                {
                    _logger.LogWarning(
                        "Proxy gateway: request body for slug '{Slug}' (tenant {TenantId}) is not a JSON object; returning 422, no upstream call.",
                        config.Slug, request.TenantId);
                    return await FinalizeAsync(request, config, route, BuildPreflight(
                        ProxyExecutionOutcome.RequestBodyNotMergeable, 422, startedAt));
                }

                if ((merge.Body?.LongLength ?? 0) > MaxBodyBytes)
                {
                    _logger.LogWarning(
                        "Proxy gateway: merged request body exceeds {Cap} bytes for slug '{Slug}' (tenant {TenantId}); returning 413, no upstream call.",
                        MaxBodyBytes, config.Slug, request.TenantId);
                    return await FinalizeAsync(request, config, route, BuildPreflight(
                        ProxyExecutionOutcome.RequestTooLarge, 413, startedAt));
                }

                effBody = merge.Body;
                effContentType = "application/json";
            }

            // Every secret this forward knows it is sending (P-5 / PS-2): masked in the response the caller gets
            // and in everything the log row stores.
            var secrets = ProxySecretRedactor.CollectSecrets(vars, effHeaders, effQuery, effective.BodyMerge);

            var (storedUrl, outboundUrl, injectedQueryKeys) = BuildUrls(
                effUpstream, effQuery, route.UpstreamPathSuffix, request, vars);
            storedUrl = ProxySecretRedactor.RedactUrl(storedUrl, secrets);
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
                return await FinalizeAsync(request, config, route, BuildFailure(
                    ProxyExecutionOutcome.UpstreamBlocked, 502, startedAt, stopwatch, storedUrl, upstreamHost,
                    injectedHeaderKeys, injectedQueryKeys, "The upstream endpoint is not an allowed destination"));
            }

            var resilience = effective.Resilience;
            var breakerScope = BreakerScope(request, config);

            // Refused before anything is sent, and only when this proxy asked for a breaker. The code is
            // its own outcome so the caller can tell "we declined to call the upstream" from "the upstream
            // did not answer" — retrying the first immediately is pointless.
            if (resilience?.Breaker is { } breakerConfig
                && _breaker.IsOpen(breakerScope, upstreamHost, breakerConfig))
            {
                stopwatch.Stop();
                _logger.LogWarning(
                    "Proxy gateway: circuit open for upstream {Host} on slug '{Slug}' (tenant {TenantId}); "
                    + "returning 503 without calling it.",
                    upstreamHost, config.Slug, request.TenantId);
                return await FinalizeAsync(request, config, route, BuildFailure(
                    ProxyExecutionOutcome.UpstreamUnavailable, 503, startedAt, stopwatch, storedUrl, upstreamHost,
                    injectedHeaderKeys, injectedQueryKeys,
                    "The upstream endpoint is failing and calls to it are paused"));
            }

            HttpResponseMessage response;
            try
            {
                // Factory clients are pooled and cheap; do NOT dispose per call.
                var client = _httpClientFactory.CreateClient(UpstreamClientName);

                // One attempt unless the tenant configured retries, which is exactly what this did before.
                var attempts = resilience?.Retry?.Attempts ?? 1;
                response = null!;

                for (var attempt = 1; ; attempt++)
                {
                    using var message = BuildUpstreamRequest(
                        effHeaders, request, effBody, effContentType, outboundUrl, vars, out var attachedHeaderKeys);
                    injectedHeaderKeys = attachedHeaderKeys;

                    // The configured timeout is the budget for the whole forward, retries included — not
                    // per attempt. Without that, 30s × 3 attempts is a 90s hang and the caller gave up long
                    // ago. With nothing configured there is no linked source at all, so the named client's
                    // own timeout applies and the behaviour is unchanged.
                    CancellationTokenSource? timeoutSource = null;
                    if (resilience?.TimeoutSeconds is { } seconds)
                    {
                        // What is left of the budget after everything already spent, floored just above
                        // zero so the last attempt is a real attempt rather than an instant cancel.
                        var remaining = TimeSpan.FromSeconds(seconds) - stopwatch.Elapsed;
                        if (remaining < TimeSpan.FromMilliseconds(50)) remaining = TimeSpan.FromMilliseconds(50);

                        timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        timeoutSource.CancelAfter(remaining);
                    }

                    using var _timeoutScope = timeoutSource;
                    var sendToken = timeoutSource?.Token ?? cancellationToken;

                    try
                    {
                        response = await client.SendAsync(
                            message, HttpCompletionOption.ResponseContentRead, sendToken);

                        if (attempt >= attempts || !IsRetryableStatus(response.StatusCode)) break;

                        response.Dispose();
                    }
                    catch (Exception ex) when (
                        attempt < attempts
                        && !cancellationToken.IsCancellationRequested
                        && !IsUpstreamBlocked(ex)
                        && (ex is HttpRequestException || ex is OperationCanceledException))
                    {
                        // A transport failure or this attempt's timeout, with attempts left. The caller
                        // hanging up is never retried: that exception carries the caller's own token.
                        _logger.LogInformation(
                            "Proxy gateway: attempt {Attempt} of {Attempts} to {Host} failed ({Message}); retrying.",
                            attempt, attempts, upstreamHost, ex.Message);
                    }

                    await DelayBeforeRetryAsync(resilience!.Retry!, attempt, cancellationToken);
                }

                if (resilience?.Breaker is { } onSuccess)
                {
                    // Reached the host and it answered. Whatever the status, the circuit's question is
                    // "is this host responding", and it is.
                    _breaker.RecordSuccess(breakerScope, upstreamHost);
                    _ = onSuccess;
                }
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
                RecordUpstreamFailure(effective, breakerScope, upstreamHost);
                _logger.LogWarning(
                    "Proxy gateway: upstream {Host} did not respond within {Timeout}s for slug '{Slug}' "
                    + "(tenant {TenantId}); returning 504.",
                    upstreamHost, effective.Resilience?.TimeoutSeconds ?? 30, config.Slug, request.TenantId);
                return await FinalizeAsync(request, config, route, BuildFailure(
                    ProxyExecutionOutcome.Timeout, 504, startedAt, stopwatch, storedUrl, upstreamHost,
                    injectedHeaderKeys, injectedQueryKeys, "Upstream did not respond in time"));
            }
            catch (HttpRequestException ex) when (IsUpstreamBlocked(ex))
            {
                // The connect-time guard refused the address (DNS rebinding, or a name that now points
                // inward). Same outcome as the pre-send check; not a host failure, so the breaker is untouched.
                stopwatch.Stop();
                _logger.LogWarning(
                    "Proxy gateway: upstream {Host} for slug '{Slug}' (tenant {TenantId}) was refused at connect time (blocked address); returning 502.",
                    upstreamHost, config.Slug, request.TenantId);
                return await FinalizeAsync(request, config, route, BuildFailure(
                    ProxyExecutionOutcome.UpstreamBlocked, 502, startedAt, stopwatch, storedUrl, upstreamHost,
                    injectedHeaderKeys, injectedQueryKeys, "The upstream endpoint is not an allowed destination"));
            }
            catch (HttpRequestException ex) when (IsResponseTooLarge(ex))
            {
                stopwatch.Stop();
                _logger.LogWarning(
                    "Proxy gateway: upstream {Host} response exceeded the {Cap} byte cap for slug '{Slug}' (tenant {TenantId}); returning 502.",
                    upstreamHost, MaxResponseBodyBytes, config.Slug, request.TenantId);
                return await FinalizeAsync(request, config, route, BuildFailure(
                    ProxyExecutionOutcome.UpstreamResponseTooLarge, 502, startedAt, stopwatch, storedUrl, upstreamHost,
                    injectedHeaderKeys, injectedQueryKeys, "Upstream response exceeded the 5 MB limit"));
            }
            catch (HttpRequestException ex)
            {
                stopwatch.Stop();
                RecordUpstreamFailure(effective, breakerScope, upstreamHost);
                _logger.LogWarning(ex,
                    "Proxy gateway: upstream {Host} unreachable for slug '{Slug}' (tenant {TenantId}); returning 502.",
                    upstreamHost, config.Slug, request.TenantId);
                return await FinalizeAsync(request, config, route, BuildFailure(
                    ProxyExecutionOutcome.UpstreamUnreachable, 502, startedAt, stopwatch, storedUrl, upstreamHost,
                    injectedHeaderKeys, injectedQueryKeys, "Could not connect to the upstream endpoint"));
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex,
                    "Proxy gateway: unexpected forwarder error for slug '{Slug}' (tenant {TenantId}); returning 500.",
                    config.Slug, request.TenantId);
                return await FinalizeAsync(request, config, route, BuildFailure(
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
                    return await FinalizeAsync(request, config, route, BuildFailure(
                        ProxyExecutionOutcome.UpstreamResponseTooLarge, 502, startedAt, stopwatch, storedUrl, upstreamHost,
                        injectedHeaderKeys, injectedQueryKeys, "Upstream response exceeded the 5 MB limit"));
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
                    return await FinalizeAsync(request, config, route, BuildFailure(
                        ProxyExecutionOutcome.UpstreamResponseTooLarge, 502, startedAt, stopwatch, storedUrl, upstreamHost,
                        injectedHeaderKeys, injectedQueryKeys, "Upstream response exceeded the 5 MB limit"));
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
                    return await FinalizeAsync(request, config, route, BuildFailure(
                        ProxyExecutionOutcome.Timeout, 504, startedAt, stopwatch, storedUrl, upstreamHost,
                        injectedHeaderKeys, injectedQueryKeys, "Upstream did not respond in time"));
                }

                stopwatch.Stop();

                // Post-read guard for a chunked / under-declared body that slipped past both checks above.
                if (bytes.LongLength > MaxResponseBodyBytes)
                {
                    _logger.LogWarning(
                        "Proxy gateway: upstream {Host} returned {Bytes} B over the {Cap} byte cap for slug '{Slug}' (tenant {TenantId}); returning 502.",
                        upstreamHost, bytes.LongLength, MaxResponseBodyBytes, config.Slug, request.TenantId);
                    return await FinalizeAsync(request, config, route, BuildFailure(
                        ProxyExecutionOutcome.UpstreamResponseTooLarge, 502, startedAt, stopwatch, storedUrl, upstreamHost,
                        injectedHeaderKeys, injectedQueryKeys, "Upstream response exceeded the 5 MB limit"));
                }

                var contentType = response.Content.Headers.ContentType?.ToString();
                var status = (int)response.StatusCode;
                var finishedAt = startedAt.AddMilliseconds(stopwatch.Elapsed.TotalMilliseconds);

                // Response field filtering (SPEC "response field filtering" §2.2 / §4.7). With ResponseMode.All
                // the projector short-circuits at step 1 — same bytes, ResponseFilterNote null, zero behaviour
                // change on the default path. Under Select the policy is fail-closed: a non-2xx / non-JSON /
                // unparseable / oversized upstream fails the call with 502, relaying nothing.
                var (relayBytes, relayCt, filterNote, failReason) = ProxyResponseProjector.Project(
                    bytes, contentType, status, effective.ResponseMode, effective.ResponseInclude);

                if (filterNote == ProxyResponseFilterNote.Failed)
                {
                    _logger.LogWarning(
                        "Proxy gateway: response filter failed for slug '{Slug}' (tenant {TenantId}): {Reason}; returning 502.",
                        config.Slug, request.TenantId, failReason);
                    return await FinalizeAsync(request, config, route, BuildFailure(
                        ProxyExecutionOutcome.ResponseFilterFailed, 502, startedAt, stopwatch, storedUrl, upstreamHost,
                        injectedHeaderKeys, injectedQueryKeys,
                        failReason ?? "The upstream response could not be filtered",
                        upstreamStatusCode: status,
                        responseFilterNote: ProxyResponseFilterNote.Failed.ToString()));
                }

                var effectiveCt = relayCt ?? contentType;

                // A vendor that echoes a key back ("invalid key sk_live_…") must not hand it to the caller (PS-2).
                relayBytes = ProxySecretRedactor.MaskBody(relayBytes, effectiveCt, secrets);

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
                    ResponseBodyBytes = relayBytes?.LongLength ?? 0,
                    ResponseContentType = effectiveCt,
                    ResponseBytes = relayBytes,
                    ResponseFilterApplied = filterNote is ProxyResponseFilterNote.Applied or ProxyResponseFilterNote.EmptyResult,
                    ResponseFilterNote = filterNote == ProxyResponseFilterNote.NotConfigured ? null : filterNote.ToString(),
                    ErrorMessage = null,
                    StartedAtUtc = startedAt,
                    FinishedAtUtc = finishedAt,
                };

                var persisted = await FinalizeAsync(request, config, route, result);
                _logger.LogInformation(
                    "Proxy gateway: relayed {Method} {Host} slug '{Slug}' (tenant {TenantId}) -> {Status}, {Bytes} B in {LatencyMs} ms.",
                    request.Method, upstreamHost, config.Slug, request.TenantId, status, bytes.LongLength, result.LatencyMs);
                return persisted;
            }
        }

        /// <summary>
        /// Counts one failure against the upstream host, but only where this proxy asked for a breaker.
        /// Timeouts and connection failures are what it counts; a 4xx or 5xx answer is not, because the
        /// host is plainly alive and the breaker's question is only whether it is reachable.
        /// </summary>
        private void RecordUpstreamFailure(EffectiveConfig effective, string breakerScope, string host)
        {
            if (effective.Resilience?.Breaker is { } breaker)
            {
                _breaker.RecordFailure(breakerScope, host, breaker);
            }
        }

        /// <summary>
        /// Who shares one circuit (PS-8, 2026-10-07): tenant + proxy + caller type. An anonymous caller (public
        /// proxy, no identity) can make calls slow on purpose — a huge AI prompt times out — so anonymous calls
        /// get their own circuit: tripping it never blocks signed-in callers, workflow steps, Test, or the
        /// tenant's other proxies to the same vendor. An unsaved draft (Test) has no id and uses its slug.
        /// </summary>
        internal static string BreakerScope(ProxyForwardRequest request, ProxyResolvedConfig config)
        {
            var proxy = config.ProxyId is { Length: > 0 } id ? id : "draft:" + config.Slug;
            var caller = request.IsTest ? "test"
                : request.CallerKind == ProxyCallerKind.Workflow ? "workflow"
                : string.IsNullOrEmpty(request.UserId) ? "anonymous"
                : "user";
            return $"{request.TenantId}|{proxy}|{caller}";
        }

        /// <summary>
        /// Status codes worth sending again. Deliberately narrow: a 4xx other than 429 will fail the same
        /// way every time, and retrying it only multiplies the load on an upstream that already said no.
        /// </summary>
        private static bool IsRetryableStatus(System.Net.HttpStatusCode status) =>
            status is System.Net.HttpStatusCode.TooManyRequests
                or System.Net.HttpStatusCode.BadGateway
                or System.Net.HttpStatusCode.ServiceUnavailable
                or System.Net.HttpStatusCode.GatewayTimeout;

        /// <summary>
        /// Waits between attempts, as the tenant configured. Jittered so a vendor recovering from an
        /// outage does not take the whole fleet's retries in one synchronised burst.
        /// </summary>
        private static async Task DelayBeforeRetryAsync(
            ProxyRetryConfig retry, int attempt, CancellationToken cancellationToken)
        {
            if (retry.Backoff == ProxyBackoffKind.None) return;

            var seconds = retry.Backoff == ProxyBackoffKind.Fixed
                ? retry.InitialDelaySeconds
                : retry.InitialDelaySeconds * Math.Pow(2, attempt - 1);

            var delay = TimeSpan.FromSeconds(Math.Min(seconds, ProxyConfigValidator.MaxRetryDelaySeconds));
            var jittered = delay * (0.8 + (Random.Shared.NextDouble() * 0.4));

            await Task.Delay(jittered, cancellationToken);
        }

        /// <summary>The configuration one forward actually runs against, after route and method overrides.</summary>
        private sealed record EffectiveConfig(
            IReadOnlyList<ProxyKeyValue> Headers,
            IReadOnlyList<ProxyKeyValue> Query,
            string Upstream,
            IReadOnlyList<ProxyKeyValue> BodyMerge,
            ProxyResponseMode ResponseMode,
            IReadOnlyList<string> ResponseInclude,
            ProxyResilienceConfig? Resilience);

        /// <summary>
        /// Resolves the configuration for this call. Precedence is route &rarr; per-method override &rarr;
        /// shared, field by field; a <c>null</c> member inherits the next level down. Headers and query are the
        /// exception: they merge across layers rather than replace (see <see cref="Merge"/>). The route layer is what
        /// lets two POST endpoints on one proxy carry different body-merge fields and response shapes.
        /// <para>
        /// An explicitly empty <c>BodyMerge</c> / <c>ResponseInclude</c> on a route is an override, not an
        /// inherit: a route can opt out of a proxy-wide body merge that does not belong in its payload.
        /// </para>
        /// </summary>
        private static EffectiveConfig ResolveEffective(
            ProxyResolvedConfig config, HttpMethodType method, ProxyRouteConfig? route)
        {
            var over = config.MethodConfigs.FirstOrDefault(c => c.Method == method);
            return new EffectiveConfig(
                // Headers and query are additive: the connection's rows always go (that is where the
                // credential lives), and a route adds its own on top, winning on a same-name key. Replace
                // semantics would force every route that adds one header to re-declare the credential.
                Merge(StringComparer.OrdinalIgnoreCase, config.Headers, over?.Headers, route?.Headers),
                Merge(StringComparer.Ordinal, config.Query, over?.Query, route?.Query),
                over?.Upstream ?? config.Upstream,
                route?.BodyMerge ?? config.BodyMerge,
                route?.ResponseMode ?? config.ResponseMode,
                route?.ResponseInclude ?? config.ResponseInclude,
                // Whole-object inherit, not field by field: a route that configures resilience states its
                // own policy, and silently blending half of it with the proxy's would produce a timeout
                // and a retry count nobody chose together.
                route?.Resilience ?? config.Resilience);
        }

        /// <summary>
        /// Layers key/value rows lowest-precedence first. Every row from every layer is kept; a later layer
        /// with the same key replaces the earlier row in place, so order stays stable and the credential
        /// declared on the connection reaches the vendor unless a route deliberately re-declares it.
        /// </summary>
        private static IReadOnlyList<ProxyKeyValue> Merge(
            StringComparer keyComparer, params IReadOnlyList<ProxyKeyValue>?[] layers)
        {
            var merged = new List<ProxyKeyValue>();
            foreach (var layer in layers)
            {
                if (layer is null) continue;
                foreach (var row in layer)
                {
                    var existing = merged.FindIndex(m => keyComparer.Equals(m.Key, row.Key));
                    if (existing >= 0) merged[existing] = row;
                    else merged.Add(row);
                }
            }
            return merged;
        }

        private static HttpRequestMessage BuildUpstreamRequest(
            IReadOnlyList<ProxyKeyValue> headers, ProxyForwardRequest request, byte[]? body, string? contentType,
            string outboundUrl, IReadOnlyDictionary<string, string> variables, out List<string> attachedHeaderKeys)
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

            // Attach ONLY the configured headers ({{$VAR.name}} tokens substituted with their resolved values).
            // Track the keys that actually attached so the audit list on the execution row is truthful.
            attachedHeaderKeys = new List<string>(headers.Count);
            foreach (var header in headers)
            {
                // PS-10: refused on save; skipped here too for configs saved before that rule.
                if (ProxyReservedHeaders.IsReserved(header.Key))
                {
                    continue;
                }

                var value = ProxyVarRef.Substitute(header.Value, variables);
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
        /// Query keys Blocks itself reads to resolve the tenant (same list as <c>EndpointAccessAuthorizer</c>
        /// TenantResolutionKeys and Genesis <c>TenantContextHelper</c>). Any case: ASP.NET query lookup ignores case,
        /// so "?X-Blocks-Key=" is read by Blocks too. Never forwarded from a client caller; a configured query key
        /// with the same name is still sent.
        /// </summary>
        internal static readonly IReadOnlySet<string> BlocksQueryKeys =
            new HashSet<string>(["x-blocks-key", "tenant_id"], StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Rebuilds the target URL. Returns the URL to STORE (a query value carrying a <c>{{$VAR.name}}</c>
        /// token keeps the raw token), the URL to CALL (tokens substituted with their resolved values), and
        /// the list of query keys Blocks injected.
        /// </summary>
        private static (string StoredUrl, string OutboundUrl, List<string> InjectedQueryKeys) BuildUrls(
            string upstream, IReadOnlyList<ProxyKeyValue> query, string pathSuffix, ProxyForwardRequest request,
            IReadOnlyDictionary<string, string> variables)
        {
            var target = upstream.TrimEnd('/');
            if (!string.IsNullOrEmpty(pathSuffix))
            {
                // The suffix is the matched route's upstream template with its parameters substituted, so it
                // is already known to be free of dot segments. {**path} arrives URL-decoded from routing, so
                // re-encode per segment: a space / unicode / reserved char then yields a valid URI (symmetric
                // with the query handling below) instead of throwing in the HttpRequestMessage ctor and being
                // misreported as 500 with a row.
                // Limitation: a literal %2F in the inbound raw path has already been decoded to '/' by
                // routing, so encoded slashes cannot be round-tripped from the action arg alone.
                var encodedSuffix = string.Join('/', pathSuffix
                    .Split('/')
                    .Select(Uri.EscapeDataString));
                target += "/" + encodedSuffix.TrimStart('/');
            }

            // Case-insensitive (PS-5): a caller's "?Project=x" must not slip past a configured "project=abc" — a
            // vendor that reads names without case and takes the first value would use the caller's.
            var configuredKeys = new HashSet<string>(query.Select(q => q.Key), StringComparer.OrdinalIgnoreCase);

            // Incoming params: keep every key that a configured Query entry does NOT override (H7), in any case. They were
            // URL-decoded by the parser, so they are re-encoded on the way back out via Uri.EscapeDataString
            // ('+' -> %20, sub-delims encoded). Signed-query-string upstreams (OAuth1, some HMAC schemes) that
            // depend on byte-exact query preservation are therefore unsupported in this phase.
            var storedParts = new List<string>();
            var outboundParts = new List<string>();
            // Blocks' own tenant keys (x-blocks-key, tenant_id) are read by the gateway to pick the tenant; they are
            // never the vendor's business. A workflow step's query is written by its author on purpose (Blocks reads
            // no tenant from it), so only client and Test calls lose them.
            var stripBlocksKeys = !string.Equals(request.CallerKind, ProxyCallerKind.Workflow, StringComparison.Ordinal);
            foreach (var pair in QueryHelpers.ParseQuery(request.IncomingQuery ?? string.Empty))
            {
                if (configuredKeys.Contains(pair.Key)
                    || (stripBlocksKeys && BlocksQueryKeys.Contains(pair.Key)))
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

            // Configured params override on collision and go FIRST, so even a vendor that takes the first of two
            // values ignoring case reads the configured one. A value carrying a {{$VAR.name}} token is sent with the
            // token substituted but NOT URL-encoded (so upstreams that sign the query string still work for a
            // bare token), and is STORED with the raw token, never a resolved value (C8 / C9).
            var injectedQueryKeys = new List<string>();
            var configuredStored = new List<string>();
            var configuredOutbound = new List<string>();
            foreach (var configured in query)
            {
                injectedQueryKeys.Add(configured.Key);
                var key = Uri.EscapeDataString(configured.Key);
                var hasToken = ProxyVarRef.ContainsRef(configured.Value);
                var outboundValue = ProxyVarRef.Substitute(configured.Value, variables);
                configuredOutbound.Add(hasToken
                    ? $"{key}={outboundValue}"
                    : $"{key}={Uri.EscapeDataString(outboundValue)}");
                configuredStored.Add(hasToken
                    ? $"{key}={configured.Value}"
                    : $"{key}={Uri.EscapeDataString(outboundValue)}");
            }

            storedParts = configuredStored.Concat(storedParts).ToList();
            outboundParts = configuredOutbound.Concat(outboundParts).ToList();

            var storedUrl = storedParts.Count == 0 ? target : target + "?" + string.Join("&", storedParts);
            var outboundUrl = outboundParts.Count == 0 ? target : target + "?" + string.Join("&", outboundParts);
            return (storedUrl, outboundUrl, injectedQueryKeys);
        }

        private static string SafeHost(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;

        /// <summary><c>true</c> when the connect-time guard refused the target (wrapped by the handler).</summary>
        private static bool IsUpstreamBlocked(Exception ex)
        {
            for (var e = ex; e is not null; e = e.InnerException)
            {
                if (e is ProxyUpstreamBlockedException)
                {
                    return true;
                }
            }

            return false;
        }

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
            string outcome, int statusCode, DateTime startedAt, IReadOnlyList<string>? allowedMethods = null,
            string? errorMessage = null) => new()
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
            ErrorMessage = errorMessage,
            AllowedMethods = allowedMethods ?? Array.Empty<string>(),
            StartedAtUtc = startedAt,
            FinishedAtUtc = startedAt,
        };

        private static ProxyForwardResult BuildFailure(
            string outcome, int statusCode, DateTime startedAt, Stopwatch stopwatch,
            string storedUrl, string upstreamHost,
            IReadOnlyList<string> injectedHeaderKeys, IReadOnlyList<string> injectedQueryKeys,
            string errorMessage, int? upstreamStatusCode = null, string? responseFilterNote = null) => new()
        {
            StatusCode = statusCode,
            Outcome = outcome,
            UpstreamStatusCode = upstreamStatusCode,
            UpstreamUrl = storedUrl,
            UpstreamHost = upstreamHost,
            InjectedHeaderKeys = injectedHeaderKeys,
            InjectedQueryKeys = injectedQueryKeys,
            LatencyMs = (int)stopwatch.ElapsedMilliseconds,
            ResponseBody = null,
            ResponseBodyBytes = 0,
            ResponseContentType = null,
            ResponseBytes = null,
            ResponseFilterApplied = false,
            ResponseFilterNote = responseFilterNote,
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
            ProxyForwardRequest request, ProxyResolvedConfig? config, ProxyRouteResolution? route,
            ProxyForwardResult result)
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
                CallerKind = request.CallerKind,
                CallerUserName = request.UserName,
                CallerImpersonated = request.CallerImpersonated,
                CallerImpersonationSessionId = request.CallerImpersonated ? request.CallerImpersonationSessionId : null,
                CallerIp = request.CallerIp,
                CallerUserAgent = request.CallerUserAgent,
                CallerOrigin = request.CallerOrigin,
                CorrelationId = request.CorrelationId,
                WorkflowId = request.WorkflowId,
                WorkflowRunId = request.WorkflowRunId,
                WorkflowNodeId = request.WorkflowNodeId,
                RoutePath = route?.Route is null ? null : ProxyRoutePath.Normalize(route.Route.Path),
                RouteUpstreamPath = route?.Route?.UpstreamPath is null
                    ? null
                    : ProxyRoutePath.Normalize(route.Route.UpstreamPath),
                RequestMethod = request.Method,
                RequestPath = request.RequestPath,
                // Caller-sent credentials (?api_key=…) are never stored (P-5).
                RequestQuery = ProxySecretRedactor.RedactQuery(request.IncomingQuery),
                UpstreamUrl = result.UpstreamUrl,
                UpstreamHost = result.UpstreamHost,
                InjectedHeaderKeys = result.InjectedHeaderKeys.ToList(),
                InjectedQueryKeys = result.InjectedQueryKeys.ToList(),
                StatusCode = result.StatusCode,
                UpstreamStatusCode = result.UpstreamStatusCode,
                Outcome = result.Outcome,
                LatencyMs = result.LatencyMs,
                ResponseBodyBytes = result.ResponseBodyBytes,
                // Never a request or response body: it is customer data (user decision 2026-10-07).
                ResponseContentType = result.ResponseContentType,
                ResponseFilterApplied = result.ResponseFilterApplied,
                ResponseFilterNote = result.ResponseFilterNote,
                ErrorMessage = result.ErrorMessage,
                StartedAtUtc = result.StartedAtUtc,
                FinishedAtUtc = result.FinishedAtUtc,
                CreatedDate = result.StartedAtUtc,
                LastUpdatedDate = result.FinishedAtUtc,
                CreatedBy = request.UserId,
                LastUpdatedBy = request.UserId,
            };

            // Buffered in memory and flushed on a timer, so the tiles cost this request nothing and the
            // Overview never aggregates the executions collection. Recorded before the insert because the
            // counters are independent of whether the row persists.
            if (entity.ProxyId.Length > 0)
            {
                _statsRecorder.Record(
                    request.TenantId, entity.ProxyId, result.StatusCode, result.LatencyMs, result.StartedAtUtc);
            }

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
