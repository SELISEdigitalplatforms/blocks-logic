using Blocks.Genesis;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Functions.DomainService.Queue;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlocksTemplate.Api.Controllers
{
    /// <summary>
    /// The public function endpoint (DECISIONS D5): <c>POST /api/fn/{functionId}</c>.
    /// <para>
    /// The tenant comes from <see cref="BlocksContext"/>, never from the route. Genesis'
    /// <c>TenantValidationMiddleware</c> guards every path under <c>api</c> — that prefix is in
    /// its default set, so this route is covered — resolving the tenant from the
    /// <c>x-blocks-key</c> header (or the query/form fallbacks in
    /// <c>TenantContextHelper.ResolveTenantIdFromHeaders</c>), validating it against the real
    /// tenant record, and calling <c>EnsureTenantContext</c> before any controller runs. So a
    /// caller must present a tenant key to reach this action at all, and that key is the only
    /// tenant identity the platform has actually verified.
    /// </para>
    /// <para>
    /// <b>Never take a tenant from the route, the query or the body.</b> The middleware has
    /// already authenticated one tenant — the header's — so a second, unvalidated tenant id in
    /// the path would let any valid <c>x-blocks-key</c> reach a <c>Public</c> function in
    /// whatever other tenant the caller could name, while every gate here was evaluated against
    /// that named tenant rather than the authenticated one. There is exactly one tenant in this
    /// request and it comes from <see cref="BlocksContext"/>.
    /// </para>
    /// <para>
    /// The invoke action itself carries no <c>[Authorize]</c>: a tenant key identifies a tenant
    /// but authenticates no user, and whether a caller may proceed is the function's own trigger
    /// configuration (public vs. token-protected), decided inside
    /// <c>FunctionInvocationService.InvokeHttpAsync</c> — this mirrors how the workflow webhook
    /// action is anonymous at the controller and authorizes itself internally per-trigger.
    /// </para>
    /// </summary>
    [ApiController]
    [Route("fn")]
    public class FunctionInvokeController : ControllerBase
    {
        private readonly IFunctionInvocationService _invocationService;
        private readonly IFunctionRunService _runService;

        public FunctionInvokeController(IFunctionInvocationService invocationService, IFunctionRunService runService)
        {
            _invocationService = invocationService;
            _runService = runService;
        }

        [HttpPost("{functionId}")]
        public async Task<IActionResult> Invoke(
            string functionId, [FromQuery] bool wait, [FromBody] object? body)
        {
            // Set by TenantValidationMiddleware from the caller's tenant key. Empty means the
            // middleware did not run, which should be impossible for a path under api — so fail
            // closed rather than guessing a tenant.
            var tenantId = BlocksContext.GetContext()?.TenantId;
            if (string.IsNullOrEmpty(tenantId))
            {
                return BadRequest(new { message = "no tenant in context; send a valid tenant key" });
            }

            var inputJson = body is null ? null : System.Text.Json.JsonSerializer.Serialize(body);
            var request = new InvokeFunctionRequestDto { InputJson = inputJson, Wait = ShouldWait(wait) };

            try
            {
                var result = await _invocationService.InvokeHttpAsync(tenantId, functionId, request);

                // 202 for the fire-and-forget shape (queued, or still running after Wait's
                // window lapsed); 200 once a terminal outcome is known.
                return FunctionQueueKeys.Wire.Queued == result.Status || FunctionQueueKeys.Wire.Running == result.Status
                    ? Accepted(result)
                    : Ok(result);
            }
            catch (FunctionNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (FunctionAuthorizationException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (FunctionValidationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// <c>?wait=true</c>, matching DECISIONS D5. A <c>Prefer: wait=&lt;sec&gt;</c> header is
        /// also accepted for parity with the spec's Sync/Fire/Poll language, treated the same
        /// as a plain <c>wait=true</c> — the actual wait budget is the function's own timeout
        /// plus a fixed grace, not whatever value the header names.
        /// </summary>
        private bool ShouldWait(bool queryWait)
        {
            if (queryWait) return true;
            var prefer = Request.Headers.TryGetValue("Prefer", out var value) ? value.ToString() : null;
            return prefer is not null && prefer.Contains("wait=", StringComparison.OrdinalIgnoreCase);
        }

        [Authorize]
        [HttpGet("runs/{runId}")]
        public async Task<RunDetailDto> GetRun(string runId)
        {
            var tenantId = BlocksContext.GetContext()?.TenantId ?? string.Empty;
            return await _runService.GetAsync(tenantId, runId);
        }
    }
}
