using Blocks.Genesis;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Microsoft.AspNetCore.Mvc;

namespace BlocksTemplate.Api.Controllers
{
    /// <summary>
    /// The authenticated management surface: everything a tenant does to their own functions
    /// in the Blocks Studio. The public invocation endpoint lives separately in
    /// <see cref="FunctionInvokeController"/> — it is reached by callers who are not
    /// necessarily Blocks users at all, and carries none of the assumptions this controller
    /// makes about an authenticated, already-authorized caller.
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
        public async Task<BaseResponse> Delete([FromQuery] string functionId)
        {
            var deleted = await _functionService.DeleteAsync(GetTenantId(), functionId, GetUserId(), GetEmail());
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
