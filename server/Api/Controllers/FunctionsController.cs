using Blocks.Genesis;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Queue;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlocksTemplate.Api.Controllers
{
    /// <summary>
    /// Every HTTP entry point for functions, in two parts that do not share a security model.
    /// <list type="bullet">
    /// <item>
    /// The <b>management surface</b> — everything a tenant does to their own functions in the
    /// Blocks Studio. Routed by convention to <c>/api/functions/{action}</c>, and every action
    /// carries its own <c>[ProtectedEndPoint]</c>: an authenticated, already-authorized caller
    /// is assumed throughout.
    /// </item>
    /// <item>
    /// The <b>public invocation surface</b> at the bottom of this file — <c>/api/fn/…</c>,
    /// reached by callers who are not necessarily Blocks users at all. Its routes are absolute
    /// (<c>~/api/…</c>) because they do not follow the controller's template, and its entry
    /// action is deliberately anonymous.
    /// </item>
    /// </list>
    /// <para>
    /// <b>Do not put a class-level <c>[Authorize]</c> or <c>[ProtectedEndPoint]</c> on this
    /// type.</b> It would shut off public invocation for every tenant, and the failure would
    /// show up as callers' 401s rather than as anything failing here. Authorization on this
    /// controller is per-action, on purpose — a new action gets nothing by default, so give it
    /// the attribute it needs.
    /// </para>
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class FunctionsController : ControllerBase
    {
        private readonly IFunctionService _functionService;
        private readonly IFunctionDeploymentService _deploymentService;
        private readonly IFunctionInvocationService _invocationService;
        private readonly IFunctionRunService _runService;
        private readonly IFunctionBuildService _buildService;
        private readonly IFunctionAuditService _auditService;

        public FunctionsController(
            IFunctionService functionService,
            IFunctionDeploymentService deploymentService,
            IFunctionInvocationService invocationService,
            IFunctionRunService runService,
            IFunctionBuildService buildService,
            IFunctionAuditService auditService)
        {
            _functionService = functionService;
            _deploymentService = deploymentService;
            _invocationService = invocationService;
            _runService = runService;
            _buildService = buildService;
            _auditService = auditService;
        }

        // --------------------------------------------------------------- functions ----

        [HttpPost]
        [ProtectedEndPoint("blocks-logic::function::manage")]
        public async Task<FunctionDetailDto> Create([FromBody] CreateFunctionRequestDto request)
            => await _functionService.CreateAsync(GetTenantId(), request, GetUserId(), GetEmail());

        [HttpPost]
        [ProtectedEndPoint("blocks-logic::function::manage")]
        public async Task<FunctionDetailDto> Update([FromBody] UpdateFunctionRequestDto request)
            => await _functionService.UpdateAsync(GetTenantId(), request, GetUserId(), GetEmail());

        [HttpPost]
        [ProtectedEndPoint("blocks-logic::function::manage")]
        public async Task<FunctionDetailDto> Save([FromBody] SaveFunctionRequestDto request)
            => await _functionService.SaveAsync(GetTenantId(), request, GetUserId(), GetEmail());

        [HttpDelete]
        [ProtectedEndPoint("blocks-logic::function::manage")]
        public async Task<BaseResponse> Delete([FromQuery] string functionId, [FromQuery] bool force = false)
        {
            // force: delete even though workflows still have a step pointing at this function.
            // Those steps then fail at their next run, which is the caller's decision to make.
            var deleted = await _functionService.DeleteAsync(
                GetTenantId(), functionId, GetUserId(), GetEmail(), force);
            return new BaseResponse { IsSuccess = deleted };
        }

        [HttpPost]
        [ProtectedEndPoint("blocks-logic::function::read")]
        public async Task<BaseQueryListResponse<List<FunctionSummaryDto>>> GetAll([FromBody] GetFunctionsRequestDto request)
        {
            var (items, totalCount) = await _functionService.GetAllAsync(GetTenantId(), request);
            return new BaseQueryListResponse<List<FunctionSummaryDto>> { Data = items.ToList(), TotalCount = totalCount };
        }

        [HttpGet]
        [ProtectedEndPoint("blocks-logic::function::read")]
        public async Task<FunctionDetailDto> Get([FromQuery] string functionId)
            => await _functionService.GetAsync(GetTenantId(), functionId);

        [HttpGet]
        [ProtectedEndPoint("blocks-logic::function::read")]
        public async Task<FunctionLimitsOptionsDto> GetLimits() => await _functionService.GetLimitsOptionsAsync();

        // -------------------------------------------------------- test / deploy ----

        [HttpPost]
        [ProtectedEndPoint("blocks-logic::function::manage")]
        public async Task<InvokeResultDto> Test([FromBody] TestFunctionRequestDto request)
            => await _invocationService.TestAsync(GetTenantId(), request.FunctionId, request);

        [HttpPost]
        [ProtectedEndPoint("blocks-logic::function::manage")]
        public async Task<FunctionVersionSummaryDto> Deploy([FromBody] DeployFunctionRequestDto request)
            => await _deploymentService.DeployAsync(GetTenantId(), request, GetUserId(), GetEmail());

        [HttpPost]
        [ProtectedEndPoint("blocks-logic::function::manage")]
        public async Task<FunctionVersionSummaryDto> Rollback([FromBody] RollbackFunctionRequestDto request)
            => await _deploymentService.RollbackAsync(GetTenantId(), request, GetUserId(), GetEmail());

        [HttpGet]
        [ProtectedEndPoint("blocks-logic::function::read")]
        public async Task<BaseQueryListResponse<List<FunctionVersionSummaryDto>>> GetVersions(
            [FromQuery] string functionId, [FromQuery] int pageNumber = 0, [FromQuery] int pageSize = 20)
        {
            var (items, totalCount) = await _functionService.GetVersionsAsync(GetTenantId(), functionId, pageNumber, pageSize);
            return new BaseQueryListResponse<List<FunctionVersionSummaryDto>> { Data = items.ToList(), TotalCount = totalCount };
        }

        [HttpGet]
        [ProtectedEndPoint("blocks-logic::function::read")]
        public async Task<Functions.DomainService.Models.FunctionSource> GetVersionSource(
            [FromQuery] string functionId, [FromQuery] string versionId)
            => await _functionService.GetVersionSourceAsync(GetTenantId(), functionId, versionId);

        [HttpGet]
        [ProtectedEndPoint("blocks-logic::function::read")]
        public async Task<FunctionBuildEntitySummary> GetBuild([FromQuery] string buildId)
        {
            var build = await _buildService.GetAsync(GetTenantId(), buildId);
            return FunctionBuildEntitySummary.From(build);
        }

        // -------------------------------------------------------------------- runs ----

        [HttpGet]
        [ProtectedEndPoint("blocks-logic::function::read")]
        public async Task<BaseQueryListResponse<List<RunSummaryDto>>> GetRuns([FromQuery] GetRunsRequestDto request)
        {
            var (items, totalCount) = await _runService.GetAllAsync(GetTenantId(), request);
            return new BaseQueryListResponse<List<RunSummaryDto>> { Data = items.ToList(), TotalCount = totalCount };
        }

        [HttpGet]
        [ProtectedEndPoint("blocks-logic::function::read")]
        public async Task<RunDetailDto> GetRun([FromQuery] string runId) => await _runService.GetAsync(GetTenantId(), runId);

        [HttpGet]
        [ProtectedEndPoint("blocks-logic::function::read")]
        public async Task<BaseQueryListResponse<List<RunLogLineDto>>> GetRunLogs(
            [FromQuery] string runId, [FromQuery] int pageNumber = 0, [FromQuery] int pageSize = 200)
        {
            var (items, totalCount) = await _runService.GetLogsAsync(GetTenantId(), runId, pageNumber, pageSize);
            return new BaseQueryListResponse<List<RunLogLineDto>> { Data = items.ToList(), TotalCount = totalCount };
        }

        [HttpPost]
        [ProtectedEndPoint("blocks-logic::function::manage")]
        public async Task<InvokeResultDto> ReplayRun([FromBody] RunIdRequestDto request)
            => await _runService.ReplayAsync(GetTenantId(), request.RunId, GetUserId(), GetEmail());

        [HttpPost]
        [ProtectedEndPoint("blocks-logic::function::manage")]
        public async Task<BaseResponse> CancelRun([FromBody] RunIdRequestDto request)
        {
            await _runService.CancelAsync(GetTenantId(), request.RunId, GetUserId(), GetEmail());
            return new BaseResponse { IsSuccess = true };
        }

        // ------------------------------------------------------------------ audit ----

        [HttpGet]
        [ProtectedEndPoint("blocks-logic::function::read")]
        public async Task<BaseQueryListResponse<List<FunctionAuditEventDto>>> GetAuditLog(
            [FromQuery] string functionId, [FromQuery] int pageNumber = 0, [FromQuery] int pageSize = 50)
        {
            var (items, totalCount) = await _auditService.GetAsync(GetTenantId(), functionId, pageNumber, pageSize);
            return new BaseQueryListResponse<List<FunctionAuditEventDto>>
            {
                Data = items.Select(FunctionAuditEventDto.From).ToList(),
                TotalCount = totalCount,
            };
        }

        // ------------------------------------------------ public invocation (D5) ----
        // Everything below is the public surface: absolute routes under /api/fn, no assumption
        // that the caller is a Blocks user, and authorization decided per function rather than
        // by an attribute. It was its own controller until these two files were merged; the
        // separation that kept the two security models apart is now this comment and the
        // per-action attributes, so read the class summary before adding anything here.

        /// <summary>
        /// The public function endpoint (DECISIONS D5): <c>POST /api/fn/{functionId}</c>.
        /// <para>
        /// The tenant comes from <see cref="BlocksContext"/>, never from the route. Genesis'
        /// <c>TenantValidationMiddleware</c> guards every path under <c>api</c> — that prefix is
        /// in its default set, so this route is covered — resolving the tenant from the
        /// <c>x-blocks-key</c> header (or the query/form fallbacks in
        /// <c>TenantContextHelper.ResolveTenantIdFromHeaders</c>), validating it against the real
        /// tenant record, and calling <c>EnsureTenantContext</c> before any controller runs. So a
        /// caller must present a tenant key to reach this action at all, and that key is the only
        /// tenant identity the platform has actually verified.
        /// </para>
        /// <para>
        /// <b>Never take a tenant from the route, the query or the body.</b> The middleware has
        /// already authenticated one tenant — the header's — so a second, unvalidated tenant id
        /// in the path would let any valid <c>x-blocks-key</c> reach a <c>Public</c> function in
        /// whatever other tenant the caller could name, while every gate here was evaluated
        /// against that named tenant rather than the authenticated one. There is exactly one
        /// tenant in this request and it comes from <see cref="BlocksContext"/>.
        /// </para>
        /// <para>
        /// The route is absolute so that it stays <c>/api/fn/{functionId}</c> rather than
        /// following this controller's <c>[controller]/[action]</c> template. That also puts it
        /// outside <c>GlobalApiRoutePrefixConvention</c>, which only prefixes controller-level
        /// templates — hence the <c>api</c> segment written out here.
        /// </para>
        /// <para>
        /// <see cref="AllowAnonymousAttribute"/> rather than merely omitting <c>[Authorize]</c>:
        /// a tenant key identifies a tenant but authenticates no user, and whether a caller may
        /// proceed is the function's own trigger configuration (public vs. token-protected),
        /// decided inside <c>FunctionInvocationService.InvokeHttpAsync</c> — this mirrors how the
        /// workflow webhook action is anonymous at the controller and authorizes itself
        /// internally per-trigger. Saying it explicitly also keeps the endpoint public if this
        /// class ever does acquire a type-level authorization attribute.
        /// </para>
        /// </summary>
        [AllowAnonymous]
        [HttpPost("~/api/fn/{functionId}")]
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

        /// <summary>
        /// The poll half of Fire/Poll: <c>GET /api/fn/runs/{runId}</c>, for a caller that took a
        /// 202 from <see cref="Invoke"/>. Distinct from <see cref="GetRun"/> — same service call,
        /// but a path parameter on the public route and a signed-in Blocks user rather than a
        /// permission-gated Studio request.
        /// </summary>
        [Authorize]
        [HttpGet("~/api/fn/runs/{runId}")]
        public async Task<RunDetailDto> PollRun(string runId)
            => await _runService.GetAsync(GetTenantId(), runId);

        // ----------------------------------------------------------------- helpers ----

        private static string GetTenantId() => BlocksContext.GetContext()?.TenantId ?? string.Empty;
        private static string? GetUserId() => BlocksContext.GetContext()?.UserId;
        private static string? GetEmail() => BlocksContext.GetContext()?.Email;
    }

    public sealed class RunIdRequestDto
    {
        public string RunId { get; set; } = string.Empty;
    }

    public sealed class FunctionBuildEntitySummary
    {
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? ImageDigest { get; set; }
        public string? Packages { get; set; }
        public string? Log { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? CompletedAt { get; set; }

        public static FunctionBuildEntitySummary From(Functions.DomainService.Entities.FunctionBuildEntity build) => new()
        {
            Id = build.ItemId,
            Status = build.Status.ToString(),
            ImageDigest = build.ImageDigest,
            Packages = build.Packages,
            Log = build.Log,
            ErrorMessage = build.ErrorMessage,
            CreatedDate = build.CreatedDate,
            CompletedAt = build.CompletedAt,
        };
    }

    public sealed class FunctionAuditEventDto
    {
        public string Action { get; set; } = string.Empty;
        public string? ActorId { get; set; }
        public string? ActorEmail { get; set; }
        public string? Detail { get; set; }
        public DateTime CreatedDate { get; set; }

        public static FunctionAuditEventDto From(Functions.DomainService.Entities.FunctionAuditEventEntity entity) => new()
        {
            Action = entity.Action,
            ActorId = entity.ActorId,
            ActorEmail = entity.ActorEmail,
            Detail = entity.Detail,
            CreatedDate = entity.CreatedDate,
        };
    }
}
