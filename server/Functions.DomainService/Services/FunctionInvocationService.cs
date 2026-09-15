using Blocks.Genesis;
using Common.InternalService.Access;
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
        /// The public entry point behind <c>{METHOD} /api/fn/{functionId}/{**path}</c>. Resolves
        /// the function's <b>active, deployed</b> version, enforces that version's own "Who can
        /// call it" (never the editable draft's — the deployed rules are authoritative) through the
        /// shared <see cref="IEndpointAccessAuthorizer"/>, shapes the call into the handler's
        /// <c>input</c> with <see cref="FunctionHttpInputBuilder"/>, and queues a run.
        /// <para>
        /// Refusals are typed so the controller can answer like the proxy gateway does: no usable
        /// credentials → <see cref="FunctionAuthorizationException"/> (401); authenticated but
        /// failing the rules, or HTTP switched off → <see cref="FunctionForbiddenException"/> (403);
        /// unknown or undeployed function → <see cref="FunctionNotFoundException"/> (404), but
        /// only to a caller who could have reached it — an anonymous caller gets 401 first, so the
        /// route cannot be used to discover which ids exist; the other method →
        /// <see cref="FunctionMethodNotAllowedException"/> (405); body over the ceiling →
        /// <see cref="FunctionRequestTooLargeException"/> (413).
        /// </para>
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
        /// <para>
        /// <paramref name="workflowExecutionId"/> is recorded as the run's <c>InvokedById</c> and
        /// reaches the sandbox as <c>ctx.run.invokedBy.id</c> (spec §41), so a function run can be
        /// traced back to the workflow execution that caused it — without it, "Triggered by
        /// workflow" names no workflow at all.
        /// </para>
        /// </summary>
        Task<InvokeResultDto> InvokeFromWorkflowAsync(
            string tenantId, string functionId, string? inputJson, BlocksContext? callerContext,
            int? waitTimeoutSeconds, string? workflowExecutionId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Builds the envelope, admits and authorizes the caller, and queues a run — the write
    /// side of the runner contract (plan/PROTOCOL.md). The synchronous "wait" mode
    /// (DECISIONS D5) polls the run record this same service just created rather than using
    /// Redis pub/sub: <c>function:sync:{runId}</c> is a fire-and-forget notification with no
    /// delivery guarantee, and subscribing to it race-free from inside a single request would
    /// add real complexity for a feature whose own contract is "best-effort, up to the configured ceiling — 180 s by default — 202
    /// otherwise". A short poll of the record <c>FunctionResultConsumer</c> is about to write
    /// is simpler, cannot miss the update, and costs at most the poll interval in latency.
    /// </summary>
    public class FunctionInvocationService : IFunctionInvocationService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
        private const int SyncGraceSeconds = 5;

        /// <summary>
        /// Ceiling on a synchronous wait when <c>Functions:SyncWaitMaxSeconds</c> is not configured.
        /// Sized above the largest function timeout (<see cref="FunctionLimits.Ceiling.TimeoutSeconds"/>
        /// + <see cref="SyncGraceSeconds"/>) so a function allowed to run for its full limit can still
        /// be awaited to completion rather than reported as "still running". Mirrored by the workflow
        /// Function step's editor maximum (client: FUNCTION_STEP_MAX_WAIT_SECONDS).
        /// </summary>
        private const int DefaultSyncWaitMaxSeconds = 180;

        private readonly IFunctionRepository _functionRepository;
        private readonly IFunctionVersionRepository _versionRepository;
        private readonly IFunctionRunRepository _runRepository;
        private readonly IFunctionRunStatsRepository _runStatsRepository;
        private readonly IFunctionAdmissionService _admissionService;
        private readonly IFunctionAuthorizationService _authorizationService;
        private readonly IFunctionBuildService _buildService;
        private readonly ISecretResolver _secretResolver;
        private readonly IEndpointAccessAuthorizer _accessAuthorizer;
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
            ISecretResolver secretResolver,
            IEndpointAccessAuthorizer accessAuthorizer,
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
            _secretResolver = secretResolver;
            _accessAuthorizer = accessAuthorizer;
            _httpContextAccessor = httpContextAccessor;
            _cache = cache;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<InvokeResultDto> InvokeHttpAsync(
            string tenantId, string functionId, InvokeFunctionRequestDto request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var httpRequest = _httpContextAccessor.HttpContext?.Request
                ?? throw new InvalidOperationException("InvokeHttpAsync requires an active HTTP request");

            var function = await _functionRepository.GetByIdAsync(tenantId, functionId, cancellationToken);
            FunctionVersionEntity? version = null;
            if (function is { Status: FunctionStatus.Live } && !string.IsNullOrEmpty(function.ActiveVersionId))
            {
                version = await _versionRepository.GetByIdAsync(tenantId, function.ActiveVersionId, cancellationToken);
            }

            if (function is null || version is null)
            {
                // 401 before 404, as the proxy gateway does for an unknown slug: an anonymous
                // caller is checked against the default (token required) policy and refused
                // there, so the route cannot be walked to learn which ids exist or are deployed.
                await RequireAuthenticatedCallerAsync(httpRequest, tenantId, cancellationToken);
                throw new FunctionNotFoundException(function is null
                    ? $"function '{functionId}' was not found"
                    : $"function '{functionId}' is not deployed");
            }

            if (!version.Trigger.HttpEnabled)
            {
                // Same reasoning: only a caller who could have invoked it learns that HTTP is off.
                await RequireAuthenticatedCallerAsync(httpRequest, tenantId, cancellationToken);
                throw new FunctionForbiddenException("this function does not accept HTTP invocations");
            }

            // The deployed version's own policy, enforced by the same authorizer that guards every
            // proxy gateway route: public passes without looking at credentials; otherwise the
            // bearer token is validated against the tenant's certificate and the rules evaluated.
            // The Genesis bearer handler never ran on this [AllowAnonymous] action, so this is the
            // one place the token is checked, and the resulting BlocksContext is the identity the
            // sandbox sees (null for public — the envelope builder strips it regardless).
            var policy = TriggerAccessPolicy.From(version.Trigger);
            var decision = await _accessAuthorizer.AuthorizeAsync(httpRequest, tenantId, policy, cancellationToken);
            switch (decision.Status)
            {
                case EndpointAccessStatus.Unauthenticated:
                    throw new FunctionAuthorizationException(decision.Reason ?? "a valid Blocks token is required");
                case EndpointAccessStatus.Forbidden:
                    throw new FunctionForbiddenException(
                        decision.Reason ?? "the caller does not hold the required roles or permissions");
                default:
                    break;
            }

            // The trigger answers one method. Checked after authorization, as the gateway checks
            // a route's methods, so which verb a function takes is not learnable anonymously.
            var verb = FunctionHttpInputBuilder.Verb(version.Trigger.HttpMethod);
            if (!string.Equals(request.Method, verb, StringComparison.OrdinalIgnoreCase))
            {
                throw new FunctionMethodNotAllowedException(verb);
            }

            // After authorization, like the gateway's own body cap, so the ceiling is not a probe
            // an unauthorized caller can use. The controller has already stopped reading.
            if (request.BodyTooLarge)
            {
                throw new FunctionRequestTooLargeException(
                    $"the request body exceeds the {FunctionHttpInputBuilder.MaxBodyBytes} byte limit");
            }

            var inputJson = FunctionHttpInputBuilder.Build(request);

            return await InvokeCoreAsync(
                tenantId, function, version, version.ImageDigest, decision.Context,
                InvokedByType.Http, invokedById: null, inputJson, request.Wait, waitTimeoutSeconds: null,
                cancellationToken);
        }

        /// <summary>
        /// Refuses with 401 unless the request carries a token that validates for
        /// <paramref name="tenantId"/>. Used on the paths that would otherwise leak whether a
        /// function exists, is deployed or accepts HTTP.
        /// </summary>
        private async Task RequireAuthenticatedCallerAsync(
            HttpRequest httpRequest, string tenantId, CancellationToken cancellationToken)
        {
            var decision = await _accessAuthorizer.AuthorizeAsync(
                httpRequest, tenantId, EndpointAccessPolicy.RequireToken(), cancellationToken);
            if (decision.Status == EndpointAccessStatus.Unauthenticated)
            {
                throw new FunctionAuthorizationException(decision.Reason ?? "a valid Blocks token is required");
            }
        }

        public async Task<InvokeResultDto> TestAsync(
            string tenantId, string functionId, TestFunctionRequestDto request, CancellationToken cancellationToken = default)
        {
            var function = await _functionRepository.GetByIdAsync(tenantId, functionId, cancellationToken)
                ?? throw new FunctionNotFoundException($"function '{functionId}' was not found");

            // A short wait, not the deploy-length one: a first build takes minutes, and holding the
            // editor's request open for all of it only to answer "not finished" is worse than
            // handing back the build id — the client polls it and shows the build's own progress.
            var testWaitSeconds = _configuration.GetValue("Functions:TestBuildWaitSeconds", 5);
            var build = await _buildService.EnsureImageAsync(
                tenantId, function, cancellationToken, testWaitSeconds, request.Rebuild);

            if (build.Status is BuildStatus.Queued or BuildStatus.Building)
            {
                // Status is what a caller reads first, so it says what is actually happening rather
                // than coming back empty: "Building"/"Queued" is the build's own state, and there
                // is no run to report one for yet.
                return new InvokeResultDto
                {
                    RunId = string.Empty,
                    Status = build.Status.ToString(),
                    BuildId = build.ItemId,
                    BuildStatus = build.Status.ToString(),
                };
            }

            if (build.Status != BuildStatus.Succeeded || string.IsNullOrEmpty(build.ImageDigest))
            {
                throw new FunctionValidationException(
                    $"the build failed: {build.ErrorMessage ?? "unknown error"}");
            }

            var context = BlocksContext.GetContext();

            // The editor's payload becomes input.body of a POST to the root, so the handler code a
            // tenant tests is the code that runs behind the public route — no "works in Test,
            // input is undefined in production" surprise.
            var inputJson = FunctionHttpInputBuilder.ForTest(request.InputJson, function.Trigger.HttpMethod);

            return await InvokeCoreAsync(
                tenantId, function, version: null, build.ImageDigest, context,
                InvokedByType.Test, invokedById: null, inputJson, wait: true, request.WaitTimeoutSeconds,
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
            int? waitTimeoutSeconds, string? workflowExecutionId, CancellationToken cancellationToken = default)
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
                InvokedByType.Workflow, invokedById: workflowExecutionId, inputJson, wait: true,
                waitTimeoutSeconds, cancellationToken);
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
                ImageDigest = image,
                TenantId = tenantId,
                Status = RunStatus.Queued,
                InvokedBy = invokedBy,
                InvokedById = invokedById,
                Input = inputJson,
                Attempt = 1,
                MaxAttempts = Math.Max(1, (version?.Retry ?? function.Retry).Attempts),
            };
            run.IdempotencyKey = $"{run.ItemId}-{run.Attempt}";

            // Secrets bound to variables are resolved here, at invoke, and never at deploy: a
            // version snapshot keeps the `{{secret.<id>}}` reference, so rotating a secret takes
            // effect on the next run rather than needing a redeploy, and a stored snapshot never
            // holds plaintext. This runs on the request thread, where IDelegatedTokenProvider has
            // a grant to redeem — the Worker's result consumer does not, which is exactly the
            // limitation documented on BlocksOsHttpSecretResolver.
            var secretIds = FunctionEnvelopeBuilder.CollectSecretIds(version, function);
            IReadOnlyDictionary<string, string>? secrets = null;
            if (secretIds.Count > 0)
            {
                secrets = await _secretResolver.ResolveAsync(secretIds, tenantId, cancellationToken);
            }

            string envelopeJson;
            try
            {
                envelopeJson = FunctionEnvelopeBuilder.Build(run, version, function, context, inputJson, secrets);
            }
            catch (FunctionEnvelopeBuilder.UnresolvedSecretException ex)
            {
                // The run is never created: there is nothing the sandbox could usefully do with a
                // half-built environment, and the author needs to hear about the broken reference
                // rather than debug a 401 from whatever the function was calling.
                // The resolver reports "absent", never why, so the run is refused with the ids and
                // the resolver that was asked — the pair needed to tell "no such secret" from
                // "this environment has no working secret store" without reading two log files.
                _logger.LogWarning(
                    "Refusing to invoke {FunctionId} via resolver {Resolver}: {Message}",
                    function.ItemId, _secretResolver.GetType().Name, ex.Message);
                throw new FunctionValidationException(ex.Message);
            }

            await _runRepository.CreateAsync(tenantId, run, cancellationToken);
            await _runStatsRepository.RecordRunStartedAsync(tenantId, function.ItemId, run.CreatedDate, cancellationToken);

            await EnqueueAsync(tenantId, function, run, image, envelopeJson, limits, cancellationToken);

            if (!wait)
            {
                return new InvokeResultDto { RunId = run.ItemId, Status = FunctionQueueKeys.Wire.Queued };
            }

            var maxSyncWaitSeconds = _configuration.GetValue("Functions:SyncWaitMaxSeconds", DefaultSyncWaitMaxSeconds);
            var executionWaitSeconds = Math.Min(
                maxSyncWaitSeconds, (waitTimeoutSeconds ?? limits.TimeoutSeconds) + SyncGraceSeconds);
            return await WaitForResultAsync(
                tenantId, run.ItemId, executionWaitSeconds, maxSyncWaitSeconds, cancellationToken);
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

        /// <summary>
        /// Waits for a run to finish, giving it <paramref name="executionWaitSeconds"/> of actually
        /// running before giving up, and never more than <paramref name="absoluteMaxSeconds"/> in total.
        /// <para>
        /// The two budgets exist because a run does not start when it is enqueued. Concurrency and
        /// host admission both queue rather than reject (by design — nothing is ever refused for
        /// volume), so under load a run can sit <c>QUEUED</c> for a while. Spending the caller's
        /// whole allowance on that wait meant a synchronous caller was handed an unfinished answer
        /// for a run that then went on to succeed, and the busier the platform the more often it
        /// happened — exactly when a caller can least afford to re-poll. So queued time does not
        /// consume the execution budget; it is bounded by the absolute cap instead, which is what
        /// stops a caller being held forever behind a long queue.
        /// </para>
        /// </summary>
        private async Task<InvokeResultDto> WaitForResultAsync(
            string tenantId,
            string runId,
            int executionWaitSeconds,
            int absoluteMaxSeconds,
            CancellationToken cancellationToken)
        {
            var hardDeadline = DateTime.UtcNow.AddSeconds(absoluteMaxSeconds);
            var deadline = DateTime.UtcNow.AddSeconds(executionWaitSeconds);
            if (deadline > hardDeadline) deadline = hardDeadline;

            var lastStatus = RunStatus.Queued;
            var hasLeftTheQueue = false;

            while (DateTime.UtcNow < deadline)
            {
                var run = await _runRepository.GetByIdAsync(tenantId, runId, cancellationToken);
                if (run is not null)
                {
                    lastStatus = run.Status;

                    if (FunctionWireMapping.IsTerminal(run.Status))
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

                    // Still queued: the run has not begun spending its own timeout, so neither
                    // should the caller. Slide the execution budget forward, but only ever up to
                    // the absolute cap — a run that never gets picked up must still return.
                    if (!hasLeftTheQueue && run.Status == RunStatus.Queued)
                    {
                        deadline = DateTime.UtcNow.AddSeconds(executionWaitSeconds);
                        if (deadline > hardDeadline) deadline = hardDeadline;
                    }
                    else
                    {
                        // Anchored once, on the first observation that it has started. After this
                        // the deadline stops moving, so the budget measures execution only.
                        hasLeftTheQueue = true;
                    }
                }

                await Task.Delay(PollInterval, cancellationToken);
            }

            // Not finished inside the window: the same 202 shape a non-waiting caller gets, so a
            // client that gives up on Wait can fall back to polling GetRun exactly the way a
            // fire-and-forget caller would. The status is the one last actually observed —
            // reporting RUNNING for a run still sitting in the queue would misdescribe it.
            return new InvokeResultDto
            {
                RunId = runId,
                Status = FunctionWireMapping.ToWire(lastStatus),
            };
        }

    }
}
