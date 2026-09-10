using Blocks.Genesis;
using DomainService.Workflow.Services;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Functions.DomainService.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Functions.DomainService.Services
{
    public interface IFunctionInvocationService
    {
        /// <summary>
        /// The public entry point behind <c>POST /api/fn/{functionId}</c>. Resolves the
        /// function's <b>active, deployed</b> version, authenticates the caller against that
        /// version's own trigger configuration (never the editable draft — see
        /// <c>FunctionAuthorizationServiceTests</c> in spirit: the deployed version's rules are
        /// authoritative), and queues a run.
        /// </summary>
        Task<InvokeResultDto> InvokeHttpAsync(
            string tenantId, string functionId, InvokeFunctionRequestDto request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs the function's <b>current editor source</b> (not a deployed version), building
        /// it first if needed. Uses the ambient, already-authenticated caller's own identity in
        /// <c>ctx.context</c> — Test simulates "does my code work", not "what would an anonymous
        /// caller see", which is what hitting the real HTTP endpoint is for.
        /// </summary>
        Task<InvokeResultDto> TestAsync(
            string tenantId, string functionId, TestFunctionRequestDto request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Re-runs a finished run's input as a brand-new run, against the function's
        /// <b>current live version</b> — DECISIONS.md's Replay note says "same input", and that
        /// is all this reproduces. Pinning the original run's version would make the same button
        /// behave differently depending on the age of the run, and break outright once that
        /// version fell outside the retained history.
        /// <para>
        /// The new run gets its own id and therefore its own freshly computed
        /// <c>{runId}-{attempt}</c> idempotency key: reusing the original key verbatim would let
        /// a receiving endpoint's own dedup logic silently no-op the very redelivery Replay
        /// exists to trigger. A function that was never deployed rebuilds its current source,
        /// which is what Test does anyway.
        /// </para>
        /// </summary>
        Task<InvokeResultDto> ReplayAsync(
            string tenantId, FunctionRunEntity originalRun, CancellationToken cancellationToken = default);

        /// <summary>
        /// Invocation from a workflow action step (<c>ActionFunctionNode</c>). The caller's
        /// own context is already established by the workflow engine — there is no bearer
        /// token here to validate, only the function's own roles/permissions requirement to
        /// check against whatever identity the workflow execution is already running as, via
        /// <see cref="IFunctionAuthorizationService.AuthorizeForWorkflow"/> rather than
        /// <see cref="IWorkflowAuthService"/>'s JWT path.
        /// </summary>
        Task<InvokeResultDto> InvokeFromWorkflowAsync(
            string tenantId, string functionId, string? inputJson, BlocksContext? callerContext,
            int? waitTimeoutSeconds, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Builds the envelope, admits and authorizes the caller, and queues a run — the write
    /// side of the runner contract (plan/PROTOCOL.md). The synchronous "wait" mode
    /// (DECISIONS D5) polls the run record this same service just created rather than using
    /// Redis pub/sub: <c>function:sync:{runId}</c> is a fire-and-forget notification with no
    /// delivery guarantee, and subscribing to it race-free from inside a single request would
    /// add real complexity for a feature whose own contract is "best-effort, up to 60 s, 202
    /// otherwise". A short poll of the record <c>FunctionResultConsumer</c> is about to write
    /// is simpler, cannot miss the update, and costs at most the poll interval in latency.
    /// </summary>
    public class FunctionInvocationService : IFunctionInvocationService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
        private const int SyncGraceSeconds = 5;

        private readonly IFunctionRepository _functionRepository;
        private readonly IFunctionVersionRepository _versionRepository;
        private readonly IFunctionRunRepository _runRepository;
        private readonly IFunctionRunStatsRepository _runStatsRepository;
        private readonly IFunctionAdmissionService _admissionService;
        private readonly IFunctionAuthorizationService _authorizationService;
        private readonly IFunctionBuildService _buildService;
        private readonly IWorkflowAuthService _workflowAuthService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ICacheClient _cache;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FunctionInvocationService> _logger;

        public FunctionInvocationService(
            IFunctionRepository functionRepository,
            IFunctionVersionRepository versionRepository,
            IFunctionRunRepository runRepository,
            IFunctionRunStatsRepository runStatsRepository,
            IFunctionAdmissionService admissionService,
            IFunctionAuthorizationService authorizationService,
            IFunctionBuildService buildService,
            IWorkflowAuthService workflowAuthService,
            IHttpContextAccessor httpContextAccessor,
            ICacheClient cache,
            IConfiguration configuration,
            ILogger<FunctionInvocationService> logger)
        {
            _functionRepository = functionRepository;
            _versionRepository = versionRepository;
            _runRepository = runRepository;
            _runStatsRepository = runStatsRepository;
            _admissionService = admissionService;
            _authorizationService = authorizationService;
            _buildService = buildService;
            _workflowAuthService = workflowAuthService;
            _httpContextAccessor = httpContextAccessor;
            _cache = cache;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<InvokeResultDto> InvokeHttpAsync(
            string tenantId, string functionId, InvokeFunctionRequestDto request, CancellationToken cancellationToken = default)
        {
            var function = await _functionRepository.GetByIdAsync(tenantId, functionId, cancellationToken)
                ?? throw new FunctionNotFoundException($"function '{functionId}' was not found");

            if (function.Status != FunctionStatus.Live || string.IsNullOrEmpty(function.ActiveVersionId))
            {
                throw new FunctionNotFoundException($"function '{functionId}' is not deployed");
            }

            var version = await _versionRepository.GetByIdAsync(tenantId, function.ActiveVersionId, cancellationToken)
                ?? throw new FunctionNotFoundException($"function '{functionId}' has no active version");

            if (!version.Trigger.HttpEnabled)
            {
                throw new FunctionAuthorizationException("this function does not accept HTTP invocations");
            }

            BlocksContext? context = null;
            if (version.Trigger.AuthMode == AuthMode.Token)
            {
                var httpRequest = _httpContextAccessor.HttpContext?.Request
                    ?? throw new InvalidOperationException("InvokeHttpAsync requires an active HTTP request");

                // Authentication only. The config carries no rules, and WorkflowAuthService's
                // own Satisfies() treats a null rule as "no requirement", so this call reduces
                // to: validate the bearer token against the tenant's certificate and build the
                // caller's BlocksContext. Reusing it for that half is the point — it is the
                // audited JWT path and re-implementing it here would be a second one.
                var authenticationOnly = new WorkflowAuthService.AuthorizationConfig(
                    OrganizationId: string.Empty,
                    Roles: null,
                    Permissions: null,
                    Mode: WorkflowAuthService.AuthorizationMode.RolesAndPermissions);

                var (isAuthenticated, resolvedContext) =
                    await _workflowAuthService.IsAuthorized(httpRequest, tenantId, authenticationOnly);
                if (!isAuthenticated)
                {
                    throw new FunctionAuthorizationException("authentication failed");
                }
                context = resolvedContext;
            }

            // The roles/permissions decision itself belongs to FunctionAuthorizationService, the
            // one implementation of that rule — the workflow path (AuthorizeForWorkflow) uses the
            // same core, so the two entry points cannot drift. It re-checks HttpEnabled, which
            // the guard above has already rejected; that guard stays because it must run before
            // any token work, so a disabled endpoint never triggers JWT validation.
            var authResult = _authorizationService.Authorize(function, version, context);
            if (!authResult.Allowed)
            {
                throw new FunctionAuthorizationException(authResult.Reason ?? "authorization failed");
            }

            return await InvokeCoreAsync(
                tenantId, function, version, version.ImageDigest, context,
                InvokedByType.Http, invokedById: null, request.InputJson, request.Wait, waitTimeoutSeconds: null,
                cancellationToken);
        }

        public async Task<InvokeResultDto> TestAsync(
            string tenantId, string functionId, TestFunctionRequestDto request, CancellationToken cancellationToken = default)
        {
            var function = await _functionRepository.GetByIdAsync(tenantId, functionId, cancellationToken)
                ?? throw new FunctionNotFoundException($"function '{functionId}' was not found");

            var build = await _buildService.EnsureImageAsync(tenantId, function, cancellationToken);
            if (build.Status != BuildStatus.Succeeded || string.IsNullOrEmpty(build.ImageDigest))
            {
                throw new FunctionValidationException(
                    build.Status is BuildStatus.Queued or BuildStatus.Building
                        ? "the build has not finished yet; try testing again shortly"
                        : $"the build failed: {build.ErrorMessage ?? "unknown error"}");
            }

            var context = BlocksContext.GetContext();

            return await InvokeCoreAsync(
                tenantId, function, version: null, build.ImageDigest, context,
                InvokedByType.Test, invokedById: null, request.InputJson, wait: true, request.WaitTimeoutSeconds,
                cancellationToken);
        }

        public async Task<InvokeResultDto> ReplayAsync(
            string tenantId, FunctionRunEntity originalRun, CancellationToken cancellationToken = default)
        {
            var function = await _functionRepository.GetByIdAsync(tenantId, originalRun.FunctionId, cancellationToken)
                ?? throw new FunctionNotFoundException($"function '{originalRun.FunctionId}' was not found");

            // The live version, exactly as a fresh invocation would use — not the version the
            // original run happened to execute. Replay means "send this input again", so it runs
            // the code that is deployed now; pinning the old version would make the button behave
            // differently depending on how old the run was, and fail outright once that version
            // fell outside the retained history.
            FunctionVersionEntity? version = null;
            string image;
            if (!string.IsNullOrEmpty(function.ActiveVersionId))
            {
                version = await _versionRepository.GetByIdAsync(tenantId, function.ActiveVersionId, cancellationToken);
            }

            if (version is not null)
            {
                image = version.ImageDigest;
            }
            else
            {
                // Never deployed — the original was a Test run, so rebuild the current source,
                // which is what testing does anyway.
                var build = await _buildService.EnsureImageAsync(tenantId, function, cancellationToken);
                if (build.Status != BuildStatus.Succeeded || string.IsNullOrEmpty(build.ImageDigest))
                {
                    throw new FunctionValidationException("could not rebuild the function's current source for replay");
                }
                image = build.ImageDigest;
            }

            var context = BlocksContext.GetContext();

            return await InvokeCoreAsync(
                tenantId, function, version, image, context,
                InvokedByType.Replay, invokedById: originalRun.ItemId, originalRun.Input,
                wait: false, waitTimeoutSeconds: null, cancellationToken);
        }

        public async Task<InvokeResultDto> InvokeFromWorkflowAsync(
            string tenantId, string functionId, string? inputJson, BlocksContext? callerContext,
            int? waitTimeoutSeconds, CancellationToken cancellationToken = default)
        {
            var function = await _functionRepository.GetByIdAsync(tenantId, functionId, cancellationToken)
                ?? throw new FunctionNotFoundException($"function '{functionId}' was not found");

            if (function.Status != FunctionStatus.Live || string.IsNullOrEmpty(function.ActiveVersionId))
            {
                throw new FunctionValidationException($"function '{functionId}' is not deployed");
            }

            var version = await _versionRepository.GetByIdAsync(tenantId, function.ActiveVersionId, cancellationToken)
                ?? throw new FunctionNotFoundException($"function '{functionId}' has no active version");

            var authResult = _authorizationService.AuthorizeForWorkflow(function, version, callerContext);
            if (!authResult.Allowed)
            {
                throw new FunctionAuthorizationException(authResult.Reason ?? "not authorized");
            }

            return await InvokeCoreAsync(
                tenantId, function, version, version.ImageDigest, callerContext,
                InvokedByType.Workflow, invokedById: null, inputJson, wait: true, waitTimeoutSeconds,
                cancellationToken);
        }

        private async Task<InvokeResultDto> InvokeCoreAsync(
            string tenantId,
            FunctionEntity function,
            FunctionVersionEntity? version,
            string image,
            BlocksContext? context,
            InvokedByType invokedBy,
            string? invokedById,
            string? inputJson,
            bool wait,
            int? waitTimeoutSeconds,
            CancellationToken cancellationToken)
        {
            var admission = await _admissionService.AdmitAsync(function, tenantId, inputJson, cancellationToken);
            if (!admission.IsAdmitted)
            {
                throw new FunctionValidationException(admission.Message ?? "the request was not admitted");
            }

            var limits = (version?.Limits ?? function.Limits).Clamp();
            var run = new FunctionRunEntity
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
                FunctionId = function.ItemId,
                VersionId = version?.ItemId,
                VersionNumber = version?.Number ?? 0,
                TenantId = tenantId,
                Status = RunStatus.Queued,
                InvokedBy = invokedBy,
                InvokedById = invokedById,
                Input = inputJson,
                Attempt = 1,
                MaxAttempts = Math.Max(1, (version?.Retry ?? function.Retry).Attempts),
            };
            run.IdempotencyKey = $"{run.ItemId}-{run.Attempt}";

            var envelopeJson = FunctionEnvelopeBuilder.Build(run, version, function, context, inputJson);

            await _runRepository.CreateAsync(tenantId, run, cancellationToken);
            await _runStatsRepository.RecordRunStartedAsync(tenantId, function.ItemId, run.CreatedDate, cancellationToken);

            await EnqueueAsync(tenantId, function, run, image, envelopeJson, limits, cancellationToken);

            if (!wait)
            {
                return new InvokeResultDto { RunId = run.ItemId, Status = FunctionQueueKeys.Wire.Queued };
            }

            var maxSyncWaitSeconds = _configuration.GetValue("Functions:SyncWaitMaxSeconds", 60);
            var effectiveWaitSeconds = Math.Min(
                maxSyncWaitSeconds, (waitTimeoutSeconds ?? limits.TimeoutSeconds) + SyncGraceSeconds);
            return await WaitForResultAsync(tenantId, run.ItemId, effectiveWaitSeconds, cancellationToken);
        }

        private async Task EnqueueAsync(
            string tenantId, FunctionEntity function, FunctionRunEntity run, string image, string envelopeJson,
            FunctionLimits limits, CancellationToken cancellationToken)
        {
            var database = _cache.CacheDatabase();
            var runKey = FunctionQueueKeys.Run(run.ItemId);

            await database.HashSetAsync(runKey,
            [
                new HashEntry("envelope", envelopeJson),
                new HashEntry("status", FunctionQueueKeys.Wire.Queued),
                new HashEntry("cpuMillicores", limits.CpuMillicores),
                new HashEntry("memoryBytes", (long)limits.MemoryMb * 1024 * 1024),
                new HashEntry("timeoutSeconds", limits.TimeoutSeconds),
                new HashEntry("concurrency", limits.Concurrency),
                new HashEntry("queuedAt", DateTimeOffset.UtcNow.ToString("O")),
            ]);
            await database.KeyExpireAsync(runKey, FunctionQueueKeys.RunTtl);

            await database.StreamAddAsync(FunctionQueueKeys.RunsStream,
            [
                new NameValueEntry("runId", run.ItemId),
                new NameValueEntry("functionId", function.ItemId),
                new NameValueEntry("versionId", run.VersionId ?? string.Empty),
                new NameValueEntry("tenantId", tenantId),
                new NameValueEntry("image", image),
                new NameValueEntry("attempt", run.Attempt),
                new NameValueEntry("protocol", FunctionQueueKeys.ProtocolVersion),
            ]);
        }

        private async Task<InvokeResultDto> WaitForResultAsync(
            string tenantId, string runId, int waitSeconds, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.AddSeconds(waitSeconds);

            while (DateTime.UtcNow < deadline)
            {
                var run = await _runRepository.GetByIdAsync(tenantId, runId, cancellationToken);
                if (run is not null && FunctionWireMapping.IsTerminal(run.Status))
                {
                    return new InvokeResultDto
                    {
                        RunId = runId,
                        Status = FunctionWireMapping.ToWire(run.Status),
                        Result = run.Result,
                        ErrorCode = run.ErrorCode == RunErrorCode.None ? null : run.ErrorCode.ToString(),
                        ErrorMessage = run.ErrorMessage,
                    };
                }

                await Task.Delay(PollInterval, cancellationToken);
            }

            // Still running after the wait window: the same 202 shape a non-waiting caller
            // gets, so a client that gives up on Wait can fall back to polling GetRun exactly
            // the way a fire-and-forget caller would.
            return new InvokeResultDto { RunId = runId, Status = FunctionQueueKeys.Wire.Running };
        }

        /// <summary>
        /// Maps the Functions module's independent role/permission match modes onto
        /// <see cref="WorkflowAuthService"/>'s single combined <c>AuthorizationMode</c>: both
        /// lists configured means both must pass (AND across lists; each list's own AND/OR is
        /// still governed by its own <see cref="MatchMode"/>).
        /// </summary>
    }
}
