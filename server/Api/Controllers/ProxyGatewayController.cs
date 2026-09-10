using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Services;
using Proxy.DomainService.Utils;

namespace Utilities.Api.Controllers
{
    /// <summary>
    /// Data-plane forwarder (Phase 2). A tenant's client calls
    /// <c>{METHOD} /api/proxy/gateway/{slug}/{**path}</c> instead of the vendor; Blocks authenticates the
    /// caller with the <c>X-Blocks-Key</c> tenant header + a bearer valid for that tenant, rebuilds the
    /// request against the stored upstream, attaches ONLY the configured headers / query params, calls the
    /// third party server-side, relays the response, and records one <see cref="ProxyExecutionEntity"/> per
    /// attempt.
    /// <para>
    /// The route is the literal <c>api/proxy/gateway</c> (NOT the <c>[controller]/[action]</c> convention);
    /// <see cref="BlocksTemplate.Api.GlobalApiRoutePrefixConvention"/> skips it so the effective path is
    /// exactly <c>/api/proxy/gateway/...</c> and never <c>/api/api/...</c>. It is <see cref="AllowAnonymousAttribute"/>
    /// at the framework level because the credential is the <c>X-Blocks-Key</c> header, not the standard
    /// scheme; auth is done here via <see cref="IProxyGatewayAuthService"/>.
    /// </para>
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("api/proxy/gateway")]
    public sealed class ProxyGatewayController : ControllerBase
    {
        private readonly IProxyGatewayAuthService _authService;
        private readonly IProxyGatewayService _gatewayService;
        private readonly ILogger<ProxyGatewayController> _logger;

        public ProxyGatewayController(
            IProxyGatewayAuthService authService,
            IProxyGatewayService gatewayService,
            ILogger<ProxyGatewayController> logger)
        {
            _authService = authService;
            _gatewayService = gatewayService;
            _logger = logger;
        }

        [HttpGet("{slug}/{**path}")]
        public Task<IActionResult> Get(string slug, string? path) => ForwardAsync(slug, path);

        [HttpPost("{slug}/{**path}")]
        public Task<IActionResult> Post(string slug, string? path) => ForwardAsync(slug, path);

        [HttpPut("{slug}/{**path}")]
        public Task<IActionResult> Put(string slug, string? path) => ForwardAsync(slug, path);

        [HttpPatch("{slug}/{**path}")]
        public Task<IActionResult> Patch(string slug, string? path) => ForwardAsync(slug, path);

        [HttpDelete("{slug}/{**path}")]
        public Task<IActionResult> Delete(string slug, string? path) => ForwardAsync(slug, path);

        private async Task<IActionResult> ForwardAsync(string slug, string? path)
        {
            var method = Request.Method.ToUpperInvariant();
            var requestPath = Request.Path.Value ?? $"/api/proxy/gateway/{slug}/{path}";

            // Step 1 — authenticate. A failure returns 401 and writes NO execution row (C1).
            var auth = await _authService.AuthenticateAsync(Request);
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
        /// Blocks-generated error body (SPEC &sect;3.4): <c>{ code: "PROXY_GATEWAY_&lt;OUTCOME&gt;", message, instance }</c>.
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
    }
}
