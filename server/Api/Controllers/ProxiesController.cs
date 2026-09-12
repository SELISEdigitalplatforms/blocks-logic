using Blocks.Genesis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Services;
using Proxy.DomainService.Utils;

namespace Utilities.Api.Controllers
{
    /// <summary>
    /// Both proxy planes behind one controller:
    /// <list type="bullet">
    /// <item><b>Control plane</b> (Phase 1 / SPEC3) — a resource-oriented surface rooted at
    /// <c>/api/Proxies</c>: the collection itself, one proxy at <c>/{proxyId}</c>, and its
    /// <c>/versions</c>, <c>/executions</c> and <c>/overview</c> sub-resources. The controller segment still
    /// comes from <c>[controller]</c> plus the global <c>api</c> prefix
    /// (<see cref="BlocksTemplate.Api.GlobalApiRoutePrefixConvention"/>); only the sub-paths are explicit.
    /// Every action requires a bearer token and the tenant is taken from <see cref="BlocksContext"/>.
    /// Where a service result carries its own <c>HttpStatus</c> the controller honours it, so the service
    /// stays the single source of truth for the SPEC §3.4 error contract.</item>
    /// <item><b>Data plane</b> (Phase 2) — <see cref="Gateway"/> at
    /// <c>{METHOD} /api/proxy/gateway/{slug}/{**path}</c>, the path a tenant's client calls instead of the
    /// vendor. That route is pinned absolutely rather than derived from the convention, because it is a
    /// published contract (see the remarks on <see cref="Gateway"/>). It is
    /// <see cref="AllowAnonymousAttribute"/> at the framework level because the credential is the
    /// <c>X-Blocks-Key</c> header rather than the standard scheme.</item>
    /// </list>
    /// <para>
    /// A route id always wins over the same id supplied in the body or query string: the URL identifies the
    /// resource, the payload only describes it.
    /// </para>
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    public sealed class ProxiesController : ControllerBase
    {
        /// <summary>
        /// Kestrel-level backstop on the gateway body, deliberately ABOVE
        /// <see cref="ProxyGatewayService.MaxRequestBodyBytes"/> (10 MB) rather than equal to it.
        /// <para>
        /// The 10 MB cap has to stay in application code: exceeding it is a documented outcome
        /// (<c>RequestTooLarge</c>) that answers 413 with the Blocks error body and still records an execution
        /// row. A framework-level limit set to the same value would pre-empt that and return a bare Kestrel
        /// 413 with no row and no <c>code</c>. This one only catches a client streaming far past the point
        /// where the answer is already decided, so the contract is unchanged and the ceiling is bounded.
        /// </para>
        /// </summary>
        private const long GatewayHardBodyLimitBytes = 12L * 1024 * 1024;

        private readonly IProxyService _proxyService;
        private readonly IProxyVersionService _proxyVersionService;
        private readonly IProxyTestService _proxyTestService;
        private readonly IProxyExecutionService _proxyExecutionService;
        private readonly IProxyGatewayAuthService _gatewayAuthService;
        private readonly IProxyGatewayService _gatewayService;
        private readonly ILogger<ProxiesController> _logger;

        /// <summary>Takes the four control-plane services plus the two data-plane gateway services.</summary>
        public ProxiesController(
            IProxyService proxyService,
            IProxyVersionService proxyVersionService,
            IProxyTestService proxyTestService,
            IProxyExecutionService proxyExecutionService,
            IProxyGatewayAuthService gatewayAuthService,
            IProxyGatewayService gatewayService,
            ILogger<ProxiesController> logger)
        {
            _proxyService = proxyService;
            _proxyVersionService = proxyVersionService;
            _proxyTestService = proxyTestService;
            _proxyExecutionService = proxyExecutionService;
            _gatewayAuthService = gatewayAuthService;
            _gatewayService = gatewayService;
            _logger = logger;
        }

        /// <summary>
        /// <c>GET /api/Proxies</c> — the console list page: the tenant's proxies newest-created first,
        /// optionally narrowed by a name / slug search term and by enabled state. A list projection: full
        /// upstream and header rows are not included.
        /// </summary>
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] ProxyGetAllRequestDto dto)
        {
            var result = await _proxyService.GetAllAsync(GetTenantId(), dto);
            return Ok(result);
        }

        /// <summary>
        /// <c>POST /api/Proxies</c> — creates a proxy and its first <c>ProxyVersions</c> row. 201 on success;
        /// 409 <c>PROXY_SLUG_CONFLICT</c> when the slug derived from the name already exists for the tenant.
        /// </summary>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ProxyCreateRequestDto dto)
        {
            var result = await _proxyService.CreateAsync(GetTenantId(), dto);
            return StatusCode(result.HttpStatus, result);
        }

        /// <summary>
        /// <c>GET /api/Proxies/{proxyId}</c> — one proxy and its full configuration. An unknown id, or another
        /// tenant's proxy, returns 200 with a null payload rather than 404.
        /// </summary>
        [Authorize]
        [HttpGet("{proxyId}")]
        public async Task<IActionResult> Get(string proxyId)
        {
            var result = await _proxyService.GetAsync(GetTenantId(), new ProxyGetRequestDto { ItemId = proxyId });
            return Ok(result);
        }

        /// <summary>
        /// <c>PUT /api/Proxies/{proxyId}</c> — rewrites the proxy's configuration and appends the per-field
        /// change set as a new version row. The slug is immutable and is never recomputed from a changed name.
        /// </summary>
        [Authorize]
        [HttpPut("{proxyId}")]
        public async Task<IActionResult> Update(string proxyId, [FromBody] ProxyUpdateRequestDto dto)
        {
            dto.ItemId = proxyId;
            var result = await _proxyService.UpdateAsync(GetTenantId(), dto);
            return StatusCode(result.HttpStatus, result);
        }

        /// <summary>
        /// <c>PATCH /api/Proxies/{proxyId}</c> — a partial update; <c>enabled</c> is the only field honoured.
        /// While disabled the proxy's <see cref="Gateway"/> route answers 404
        /// <c>PROXY_GATEWAY_PROXYNOTFOUND</c>, exactly as an unknown slug does.
        /// </summary>
        [Authorize]
        [HttpPatch("{proxyId}")]
        public async Task<IActionResult> SetEnabled(string proxyId, [FromBody] ProxyToggleRequestDto dto)
        {
            dto.ItemId = proxyId;
            var result = await _proxyService.ToggleAsync(GetTenantId(), dto);
            return StatusCode(result.HttpStatus, result);
        }

        /// <summary>
        /// <c>DELETE /api/Proxies/{proxyId}</c> — hard-deletes the proxy document. Its <c>ProxyVersions</c> and
        /// <c>ProxyExecutions</c> rows are deliberately retained, so Change history and Request logs keep
        /// working afterwards.
        /// </summary>
        [Authorize]
        [HttpDelete("{proxyId}")]
        public async Task<IActionResult> Delete(string proxyId)
        {
            var result = await _proxyService.DeleteAsync(GetTenantId(), new ProxyDeleteRequestDto { ItemId = proxyId });
            return StatusCode(result.HttpStatus, result);
        }

        /// <summary>
        /// <c>GET /api/Proxies/{proxyId}/versions</c> — the <em>Change history</em> tab: the append-only
        /// version rows for one proxy, newest first, each carrying its per-field change set and the effective
        /// config snapshot after that change.
        /// </summary>
        [Authorize]
        [HttpGet("{proxyId}/versions")]
        public async Task<IActionResult> ListVersions(string proxyId, [FromQuery] ProxyGetVersionsRequestDto dto)
        {
            dto.ProxyId = proxyId;
            var result = await _proxyVersionService.GetVersionsAsync(GetTenantId(), dto);
            return StatusCode(result.HttpStatus, result);
        }

        /// <summary>
        /// <c>POST /api/Proxies/{proxyId}/versions/{versionId}/revert</c> — restores the configuration
        /// captured by that version and records the restore as a new <c>Revert</c> version row. A controlled
        /// action rather than a resource, so it stays a verb on a POST. History is never rewritten or
        /// truncated; the version number moves forward, not back.
        /// </summary>
        [Authorize]
        [HttpPost("{proxyId}/versions/{versionId}/revert")]
        public async Task<IActionResult> Revert(string proxyId, string versionId)
        {
            var dto = new ProxyRevertRequestDto { ProxyId = proxyId, VersionId = versionId };
            var result = await _proxyVersionService.RevertAsync(GetTenantId(), dto);
            return StatusCode(result.HttpStatus, result);
        }

        /// <summary>
        /// <c>POST /api/Proxies/test</c> — console-only: run the data-plane forward pipeline against a saved
        /// proxy or an unsaved draft and return the result WITHOUT recording a <c>ProxyExecutions</c> row and
        /// WITHOUT creating or mutating any <c>Proxies</c> / <c>ProxyVersions</c> data (SPEC §3.3). It sits on
        /// the collection, not under <c>{proxyId}</c>, precisely because the draft it tests need not exist yet.
        /// The tenant is taken from <see cref="BlocksContext"/>; no <c>X-Blocks-Key</c> is required here.
        /// </summary>
        [Authorize]
        [HttpPost("test")]
        public async Task<IActionResult> Test([FromBody] ProxyTestRequestDto dto)
        {
            var context = BlocksContext.GetContext();
            var outcome = await _proxyTestService.TestAsync(
                context?.TenantId ?? string.Empty, context?.UserId, dto);

            return outcome.IsSuccess
                ? Ok(outcome.Result)
                : StatusCode(outcome.HttpStatus, new { code = outcome.Code, errors = outcome.Errors });
        }

        /// <summary>
        /// <c>GET /api/Proxies/{proxyId}/executions</c> — SPEC3 §3.1, the <em>Request logs</em> list for one
        /// proxy: last 24h, filtered by <c>statusClass</c>, newest-first, paged, or a Live tail when
        /// <c>afterId</c> is supplied. Returns 400 <c>PROXY_VALIDATION</c> on a bad field and 404
        /// <c>PROXY_NOT_FOUND</c> for a genuinely unknown proxy id (a deleted proxy whose rows remain still
        /// works).
        /// </summary>
        [Authorize]
        [HttpGet("{proxyId}/executions")]
        public async Task<IActionResult> ListExecutions(string proxyId, [FromQuery] ProxyGetExecutionsRequestDto dto)
        {
            dto.ProxyId = proxyId;
            var result = await _proxyExecutionService.GetExecutionsAsync(GetTenantId(), dto);
            return StatusCode(result.HttpStatus, result);
        }

        /// <summary>
        /// <c>GET /api/Proxies/{proxyId}/executions/export</c> — SPEC3 §3.4, the filtered last-24h log as a
        /// UTF-8 (BOM) RFC 4180 CSV attachment, newest first, capped at 50,000 rows
        /// (<c>X-Proxy-Export-Truncated: true</c> when more matched). Response bodies are never included.
        /// <para>
        /// Declared before <see cref="GetExecution"/> for readability only — the literal <c>export</c> segment
        /// outranks the <c>{executionId}</c> parameter in route precedence regardless of order.
        /// </para>
        /// </summary>
        [Authorize]
        [HttpGet("{proxyId}/executions/export")]
        public async Task<IActionResult> ExportExecutionsCsv(string proxyId, [FromQuery] ProxyExportExecutionsRequestDto dto)
        {
            dto.ProxyId = proxyId;
            var result = await _proxyExecutionService.ExportExecutionsCsvAsync(GetTenantId(), dto);
            if (!result.IsSuccess)
            {
                return StatusCode(result.HttpStatus, new { code = result.Code, message = result.Message, errors = result.Errors });
            }

            if (result.Truncated)
            {
                Response.Headers["X-Proxy-Export-Truncated"] = "true";
            }

            return File(result.Content, "text/csv; charset=utf-8", result.FileName);
        }

        /// <summary>
        /// <c>GET /api/Proxies/{proxyId}/executions/{executionId}</c> — SPEC3 §3.2, one expanded request-log
        /// row including the full stored response body, clipped to 64 KB for transport (the stored row is
        /// untouched). An unknown id, an id whose proxy differs, or another tenant's row all return 200 with
        /// <c>data: null</c>.
        /// </summary>
        [Authorize]
        [HttpGet("{proxyId}/executions/{executionId}")]
        public async Task<IActionResult> GetExecution(string proxyId, string executionId)
        {
            var dto = new ProxyGetExecutionRequestDto { ProxyId = proxyId, ItemId = executionId };
            var result = await _proxyExecutionService.GetExecutionAsync(GetTenantId(), dto);
            return StatusCode(result.HttpStatus, result);
        }

        /// <summary>
        /// <c>GET /api/Proxies/{proxyId}/overview</c> — SPEC3 §3.3, the Overview tiles for one proxy (calls /
        /// avg latency / error rate over the rolling 24h window) plus the Configuration-panel convenience
        /// fields. 404 <c>PROXY_NOT_FOUND</c> only when the id neither exists for the tenant nor has any
        /// execution rows.
        /// </summary>
        [Authorize]
        [HttpGet("{proxyId}/overview")]
        public async Task<IActionResult> GetOverview(string proxyId)
        {
            var dto = new ProxyGetOverviewRequestDto { ProxyId = proxyId };
            var result = await _proxyExecutionService.GetOverviewAsync(GetTenantId(), dto);
            return StatusCode(result.HttpStatus, result);
        }

        /// <summary>
        /// Data plane (Phase 2). A tenant's client calls <c>{METHOD} /api/proxy/gateway/{slug}/{**path}</c>
        /// instead of the vendor; Blocks authenticates the caller with the <c>X-Blocks-Key</c> tenant header +
        /// a bearer valid for that tenant, rebuilds the request against the stored upstream, attaches ONLY the
        /// configured headers / query params, calls the third party server-side, relays the response, and
        /// records one <see cref="ProxyExecutionEntity"/> per attempt.
        /// <para>
        /// Unlike the control-plane actions above, this route is <b>pinned absolutely</b> (<c>~/</c>) instead of
        /// being derived from <c>[controller]</c>. The path is a published contract that third-party clients
        /// hard-code, so it must not change if this method or its controller is ever renamed, and the
        /// registered template must be the exact lowercase spelling we document rather than a case-insensitive
        /// match for it. The leading <c>~/</c> also makes it skip
        /// <see cref="BlocksTemplate.Api.GlobalApiRoutePrefixConvention"/>, so the <c>api</c> segment is written
        /// here once and never doubled.
        /// </para>
        /// </summary>
        [AllowAnonymous]
        [RequestSizeLimit(GatewayHardBodyLimitBytes)]
        [AcceptVerbs("GET", "POST", "PUT", "PATCH", "DELETE", Route = "~/api/proxy/gateway/{slug}/{**path}")]
        public async Task<IActionResult> Gateway(string slug, string? path)
        {
            var method = Request.Method.ToUpperInvariant();
            var requestPath = Request.Path.Value ?? $"/api/proxy/gateway/{slug}/{path}";

            // Step 1 — authenticate. A failure returns 401 and writes NO execution row (C1).
            var auth = await _gatewayAuthService.AuthenticateAsync(Request);
            if (!auth.IsAuthenticated)
            {
                _logger.LogWarning("Proxy gateway: rejected unauthenticated {Method} {Path}.", method, requestPath);
                return GatewayError(ProxyExecutionOutcome.Unauthorized, 401, requestPath);
            }

            // Step 2 — buffer the body under the 10 MB cap before any upstream connection (C4 / C10).
            var (body, tooLarge) = await ReadBodyAsync(HttpContext.RequestAborted);

            var result = await _gatewayService.ForwardAsync(new ProxyForwardRequest
            {
                TenantId = auth.TenantId,
                UserId = auth.UserId,
                Slug = slug,
                Method = method,
                PathSuffix = path ?? string.Empty,
                IncomingQuery = Request.QueryString.HasValue ? Request.QueryString.Value!.TrimStart('?') : string.Empty,
                RequestPath = requestPath,
                Body = body,
                BodyTooLarge = tooLarge,
                ContentType = string.IsNullOrWhiteSpace(Request.ContentType) ? null : Request.ContentType,
                IsTest = false,
            }, HttpContext.RequestAborted);

            if (result.Ok)
            {
                return Relay(result);
            }

            if (result.Outcome == ProxyExecutionOutcome.MethodNotAllowed)
            {
                Response.Headers.Allow = string.Join(", ", result.AllowedMethods);
            }

            return GatewayError(result.Outcome, result.StatusCode, requestPath, result.UpstreamStatusCode);
        }

        /// <summary>Relays the upstream status, body, and Content-Type byte-for-byte. No other header is relayed.</summary>
        private IActionResult Relay(ProxyForwardResult result)
        {
            Response.StatusCode = result.StatusCode;
            if (!string.IsNullOrEmpty(result.ResponseContentType))
            {
                Response.ContentType = result.ResponseContentType;
            }

            return result.ResponseBytes is { Length: > 0 }
                ? new FileContentResult(result.ResponseBytes, result.ResponseContentType ?? "application/octet-stream")
                : new EmptyResult();
        }

        /// <summary>
        /// Blocks-generated error body (SPEC §3.4): <c>{ code: "PROXY_GATEWAY_&lt;OUTCOME&gt;", message, instance }</c>.
        /// </summary>
        private IActionResult GatewayError(string outcome, int statusCode, string instance, int? upstreamStatusCode = null)
        {
            object body = upstreamStatusCode is { } upstream
                ? new
                {
                    code = ProxyGatewayErrorCodes.ForOutcome(outcome),
                    message = SafeMessage(outcome),
                    instance,
                    upstreamStatusCode = upstream,
                }
                : new
                {
                    code = ProxyGatewayErrorCodes.ForOutcome(outcome),
                    message = SafeMessage(outcome),
                    instance,
                };
            return StatusCode(statusCode, body);
        }

        private static string SafeMessage(string outcome) => outcome switch
        {
            ProxyExecutionOutcome.Unauthorized => "Missing or invalid credentials for this tenant.",
            ProxyExecutionOutcome.ProxyNotFound => "No enabled proxy is configured for this path.",
            ProxyExecutionOutcome.MethodNotAllowed => "This HTTP method is not allowed for this proxy.",
            ProxyExecutionOutcome.RequestTooLarge => "The request body exceeds the 10 MB limit.",
            ProxyExecutionOutcome.Timeout => "The upstream endpoint did not respond within 30 seconds.",
            ProxyExecutionOutcome.UpstreamUnreachable => "The upstream endpoint could not be reached.",
            ProxyExecutionOutcome.UpstreamBlocked => "The upstream endpoint is not an allowed destination.",
            ProxyExecutionOutcome.UpstreamResponseTooLarge => "The upstream response exceeds the 10 MB limit.",
            ProxyExecutionOutcome.VariableResolutionFailed => "A configured configuration variable could not be resolved.",
            ProxyExecutionOutcome.ResponseFilterFailed => "The upstream response could not be filtered to the configured fields.",
            _ => "The proxy could not complete the request.",
        };

        private async Task<(byte[]? Body, bool TooLarge)> ReadBodyAsync(CancellationToken cancellationToken)
        {
            var cap = ProxyGatewayService.MaxRequestBodyBytes;

            if (Request.ContentLength is { } declared && declared > cap)
            {
                return (null, true);
            }

            if (!Request.Body.CanRead)
            {
                return (null, false);
            }

            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            long total = 0;
            int read;
            while ((read = await Request.Body.ReadAsync(chunk, cancellationToken)) > 0)
            {
                total += read;
                if (total > cap)
                {
                    return (null, true);
                }

                buffer.Write(chunk, 0, read);
            }

            return (buffer.Length == 0 ? null : buffer.ToArray(), false);
        }

        private static string GetTenantId()
        {
            var context = BlocksContext.GetContext();
            return context?.TenantId ?? string.Empty;
        }
    }
}
