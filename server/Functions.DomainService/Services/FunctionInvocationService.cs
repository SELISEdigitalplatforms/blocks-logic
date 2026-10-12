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
    /// side of the runner contract (plan/PROTOCOL.md).
    /// <para>
    /// <b>Enqueue is compensated.</b> The run record is written before the run is queued, so a
    /// Redis failure in between used to leave a record QUEUED for ever with nothing behind it.
    /// Now the payload is withdrawn, the record is closed FAILED / <see cref="RunErrorCode.EnqueueFailed"/>
    /// and the caller gets <see cref="FunctionUnavailableException"/> (503). Once the record
    /// exists, nothing — not even the caller disconnecting — can abandon the enqueue half-way.
    /// </para>
    /// <para>
    /// <b>The synchronous "wait" mode</b> (DECISIONS D5) is woken by <c>function:sync:{runId}</c>
    /// and re-reads the record then; because that notification is fire-and-forget, the record is
    /// also re-read on a backing-off timer, so a missed message costs latency, never
    /// correctness. HTTP-facing waits (the public route and Test) are capped by
    /// <c>Functions:HttpSyncWaitMaxSeconds</c> (default <see cref="DefaultHttpSyncWaitMaxSeconds"/> s,
    /// under typical 60 s ingress timeouts); the workflow step, which runs in the Worker and has
    /// no ingress in front of it, keeps <c>Functions:SyncWaitMaxSeconds</c>. After the cap the
    /// caller gets the 202 shape with the run id to poll.
    /// </para>
    /// </summary>
    public class FunctionInvocationService : IFunctionInvocationService
    {
        private const int SyncGraceSeconds = 5;

        /// <summary>
        /// First re-read of the record while waiting, and the backoff ceiling. Without a working
        /// subscription the poll is all there is, so it starts fast and caps at 1 s; with one, the
        /// notification carries the latency and the timer is only the net for a missed message.
        /// </summary>
        internal static readonly TimeSpan PollInitial = TimeSpan.FromMilliseconds(200);
        internal static readonly TimeSpan PollCap = TimeSpan.FromSeconds(1);
        internal static readonly TimeSpan SubscribedPollInitial = TimeSpan.FromSeconds(1);
        internal static readonly TimeSpan SubscribedPollCap = TimeSpan.FromSeconds(3);

        /// <summary>Below this much time left, the wait ends instead of sleeping once more.</summary>
        internal static readonly TimeSpan MinWaitSlice = TimeSpan.FromMilliseconds(10);

        /// <summary>
        /// Ceiling on a wait that a public HTTP caller (or the editor's Test) holds open, when
        /// <c>Functions:HttpSyncWaitMaxSeconds</c> is not configured. Well under the 60 s idle
        /// timeout typical of ingress controllers and load balancers, so the caller gets a clean
        /// 202 with a run id instead of a gateway 504 that hides whether the run was accepted.
        /// </summary>
        internal const int DefaultHttpSyncWaitMaxSeconds = 30;

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
        private readonly IFunctionAdmissionService _admissionService;
        private readonly IFunctionAuthorizationService _authorizationService;
        private readonly IFunctionBuildService _buildService;
        private readonly IEndpointAccessAuthorizer _accessAuthorizer;
        private readonly IFunctionDelegationService _delegation;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly Storage.IFunctionArtifactStore _artifacts;
        private readonly ICacheClient _cache;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FunctionInvocationService> _logger;

        /// <summary>Default for <c>Functions:MaxConcurrentSyncWaits</c>.</summary>
        internal const int DefaultMaxConcurrentSyncWaits = 256;

        /// <summary>
        /// Slots for synchronous HTTP waits held open at once, per process (the service is a
        /// singleton). Sized from <c>Functions:MaxConcurrentSyncWaits</c> at start-up.
        /// </summary>
        private readonly SemaphoreSlim _syncWaits;

        /// <summary>Active version documents, by version id (see <see cref="FunctionVersionCache"/>).</summary>
        private readonly FunctionVersionCache _versions = new();

        public FunctionInvocationService(
            IFunctionRepository functionRepository,
            IFunctionVersionRepository versionRepository,
            IFunctionRunRepository runRepository,
            IFunctionAdmissionService admissionService,
            IFunctionAuthorizationService authorizationService,
            IFunctionBuildService buildService,
            IEndpointAccessAuthorizer accessAuthorizer,
            IFunctionDelegationService delegation,
            IHttpContextAccessor httpContextAccessor,
            Storage.IFunctionArtifactStore artifacts,
            ICacheClient cache,
            IConfiguration configuration,
            ILogger<FunctionInvocationService> logger)
        {
            _functionRepository = functionRepository;
            _versionRepository = versionRepository;
            _runRepository = runRepository;
            _admissionService = admissionService;
            _authorizationService = authorizationService;
            _buildService = buildService;
            _accessAuthorizer = accessAuthorizer;
            _delegation = delegation;
            _httpContextAccessor = httpContextAccessor;
            _artifacts = artifacts;
            _cache = cache;
            _configuration = configuration;
            _logger = logger;

            var maxSyncWaits = Math.Max(1, configuration.GetValue("Functions:MaxConcurrentSyncWaits", DefaultMaxConcurrentSyncWaits));
            _syncWaits = new SemaphoreSlim(maxSyncWaits, maxSyncWaits);
        }

        private Task<FunctionVersionEntity?> ActiveVersionAsync(string tenantId, string versionId, CancellationToken cancellationToken)
            => _versions.GetOrReadAsync(tenantId, versionId,
                () => _versionRepository.GetByIdAsync(tenantId, versionId, cancellationToken));

        public async Task<InvokeResultDto> InvokeHttpAsync(
            string tenantId, string functionId, InvokeFunctionRequestDto request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var httpRequest = _httpContextAccessor.HttpContext?.Request
                ?? throw new InvalidOperationException("InvokeHttpAsync requires an active HTTP request");

            // Where this call's time goes, logged once at the end (StepTimer).
            var timer = new StepTimer();
            StepTimer.Current.Value = timer;
            string? timedRunId = null;
            try
            {

            var function = await _functionRepository.GetByIdAsync(tenantId, functionId, cancellationToken);
            timer.Mark("function");
            FunctionVersionEntity? version = null;
            if (function is { Status: FunctionStatus.Live } && !string.IsNullOrEmpty(function.ActiveVersionId))
            {
                version = await ActiveVersionAsync(tenantId, function.ActiveVersionId, cancellationToken);
                timer.Mark("version");
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
            timer.Mark("auth");
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

            // The trigger answers its listed verbs (or the one legacy method when it lists none).
            // Checked after authorization, as the gateway checks a route's methods, so which verbs
            // a function takes is not learnable anonymously.
            var allowed = FunctionHttpInputBuilder.AllowedVerbs(version.Trigger);
            if (!allowed.Contains(request.Method ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                throw new FunctionMethodNotAllowedException(string.Join(", ", allowed));
            }

            // After authorization, like the gateway's own body cap, so the ceiling is not a probe
            // an unauthorized caller can use. The controller has already stopped reading.
            if (request.BodyTooLarge)
            {
                throw new FunctionRequestTooLargeException(
                    $"the request body exceeds the {FunctionHttpInputBuilder.MaxBodyBytes} byte limit");
            }

            var inputJson = FunctionHttpInputBuilder.Build(request);

            var syncWait = SyncWaitSeconds(version.Trigger, request);

            // Process-wide bound on requests held open. Each sync wait holds a connection and a
            // subscription; without a ceiling a burst of slow calls to a sync function could hold
            // every connection the Api has. Full → this call is simply async: 202 + poll token,
            // which the caller already has to handle for a run that outlasts the wait.
            var holdsSlot = syncWait is not null && _syncWaits.Wait(0);
            if (!holdsSlot) syncWait = null;

            try
            {
                var result = await InvokeCoreAsync(
                    tenantId, function, version, FunctionRunImage.For(version), decision.Context,
                    InvokedByType.Http, invokedById: null, inputJson, request.Wait || syncWait is not null,
                    waitTimeoutSeconds: null, cancellationToken, httpWaitSeconds: syncWait,
                    finishedOnlyWithoutRetry: syncWait is not null);
                timedRunId = result.RunId;

                if (syncWait is null) return result;

                // Streaming (F-5): the controller answers with the pieces as they come.
                if (result.Streaming) return result;

                var status = FunctionWireMapping.ToRunStatus(result.Status, out var recognised);
                if (!recognised || !FunctionWireMapping.IsTerminal(status))
                {
                    // Not finished inside the window (or about to be retried): exactly the 202 an
                    // async caller gets — same run id, same QUEUED status, poll token added by the
                    // controller. Reporting RUNNING here would make the two 202s differ.
                    return new InvokeResultDto { RunId = result.RunId, Status = FunctionQueueKeys.Wire.Queued };
                }

                // Only the controller reads this, to answer with the function's own response.
                // It is never serialized.
                result.RespondSynchronously = true;
                return result;
            }
            finally
            {
                if (holdsSlot) _syncWaits.Release();
            }

            }
            finally
            {
                _logger.LogInformation(
                    "Function {FunctionId} HTTP call {RunId} took {TotalMs} ms in the Api: {Steps}",
                    functionId, timedRunId ?? "-", timer.ElapsedMs, timer.ToString());
                StepTimer.Current.Value = null;
            }
        }

        /// <summary>
        /// How long a public HTTP call holds its request open for the function's answer, or null
        /// for today's fire-and-forget 202 (sandbox/REUSE.md, "Control plane").
        /// <list type="bullet">
        /// <item>the deployed trigger is not <c>sync</c> → null: exactly the behaviour before sync
        /// existed, whatever the caller's <c>Prefer</c> says.</item>
        /// <item><c>Prefer: respond-async</c> → null.</item>
        /// <item><c>Prefer: wait=N</c> → N seconds (N ≤ 0 means "do not wait" → null).</item>
        /// <item>otherwise → the HTTP cap.</item>
        /// </list>
        /// Always bounded by <c>Functions:HttpSyncWaitMaxSeconds</c> (default
        /// <see cref="DefaultHttpSyncWaitMaxSeconds"/>, under typical 60 s ingress timeouts), so the
        /// caller gets a clean 202 before any gateway would cut the connection.
        /// </summary>
        internal int? SyncWaitSeconds(TriggerConfig trigger, InvokeFunctionRequestDto request)
        {
            // Only a function deployed as sync ever holds a request. Prefer: wait= on an async
            // function is ignored, exactly as before sync existed — existing clients that send it
            // keep getting their 202 and never a raw answer they did not ask the tenant for.
            if (request.PreferAsync || !TriggerConfig.IsSync(trigger.ResponseMode)) return null;

            var cap = Math.Max(1, _configuration.GetValue("Functions:HttpSyncWaitMaxSeconds", DefaultHttpSyncWaitMaxSeconds));
            if (request.PreferWaitSeconds is { } asked)
            {
                return asked <= 0 ? null : Math.Min(asked, cap);
            }

            return cap;
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

            // One test per function per window. Refused here rather than queued, because the
            // developer is watching: being told "wait a moment" beats a run that sits on a stream
            // and surfaces minutes later. The cost being limited is real — a test builds and runs
            // on a host that is also serving deployed functions.
            var rateKey = FunctionQueueKeys.TestRate(functionId);
            var database = _cache.CacheDatabase();
            if (!await database.StringSetAsync(
                    rateKey, DateTimeOffset.UtcNow.ToString("O"),
                    FunctionQueueKeys.TestRateWindow, When.NotExists).ConfigureAwait(false))
            {
                var remaining = await database.KeyTimeToLiveAsync(rateKey).ConfigureAwait(false);
                var seconds = (int)Math.Ceiling((remaining ?? FunctionQueueKeys.TestRateWindow).TotalSeconds);
                throw new FunctionRateLimitedException(
                    $"this function was tested less than {FunctionQueueKeys.TestRateWindow.TotalSeconds:0} seconds ago; "
                    + $"try again in {seconds}s",
                    seconds);
            }

            // One test per function at a time. Clicking Test again used to leave the previous one
            // building and running to completion: two sandboxes, two images and two sets of logs
            // for a question the tenant has already moved on from, all of it drawing on the
            // fleet's budget. The previous run is asked to stop before this one is queued.
            await SupersedePreviousTestAsync(tenantId, functionId, cancellationToken);

            // Every test builds its own image, on the runner that then runs it, and that image is
            // deleted when the run ends. Nothing is cached or reused: a cached image lived in one
            // host's registry, so a run claimed by any other host could not find it.
            var (build, sourceKey) = await _buildService.CreateTestBuildAsync(tenantId, function, cancellationToken);

            var context = BlocksContext.GetContext();

            // The editor's payload becomes input.body of a POST to the root, so the handler code a
            // tenant tests is the code that runs behind the public route — no "works in Test,
            // input is undefined in production" surprise.
            var inputJson = FunctionHttpInputBuilder.ForTest(request.InputJson, FunctionHttpInputBuilder.TestVerb(function.Trigger));

            var result = await InvokeCoreAsync(
                tenantId, function, version: null, TestImageRef(build.ItemId), context,
                InvokedByType.Test, invokedById: null, inputJson, wait: true, request.WaitTimeoutSeconds,
                cancellationToken, new TestBuild(build.ItemId, sourceKey));
            result.BuildId ??= build.ItemId;
            await RecordCurrentTestAsync(functionId, result.RunId);
            return result;
        }

        /// <summary>A test run's own build, carried on its entry in <see cref="FunctionQueueKeys.TestsStream"/>.</summary>
        private sealed record TestBuild(string BuildId, string SourceKey);

        /// <summary>
        /// Asks the function's previous test run, if any, to stop.
        /// <para>
        /// Only the cancel flag is set: whichever runner holds that run — which need not be the
        /// one that takes the new test — checks it before building, after building and while the
        /// sandbox runs, and reports the run cancelled itself. A run that has already finished
        /// never reads it, and the key expires on its own.
        /// </para>
        /// <para>
        /// Never allowed to fail the new test. The worst case is a superseded run finishing
        /// anyway, which is exactly the behaviour this replaces.
        /// </para>
        /// </summary>
        private async Task SupersedePreviousTestAsync(
            string tenantId, string functionId, CancellationToken cancellationToken)
        {
            try
            {
                var database = _cache.CacheDatabase();
                var previous = await database.StringGetAsync(FunctionQueueKeys.CurrentTest(functionId));
                if (previous.IsNullOrEmpty) return;

                var previousRunId = previous.ToString();
                var run = await _runRepository.GetByIdAsync(tenantId, previousRunId, cancellationToken);
                if (run is null || FunctionWireMapping.IsTerminal(run.Status)) return;

                await database.StringSetAsync(
                    FunctionQueueKeys.Cancel(previousRunId), "1", FunctionQueueKeys.CancelTtl);

                _logger.LogInformation(
                    "Superseding test run {RunId} of function {FunctionId}: a newer test was started",
                    previousRunId, functionId);
            }
            catch (Exception ex) when (ex is StackExchange.Redis.RedisException or TimeoutException)
            {
                _logger.LogWarning(
                    "Could not supersede the previous test of function {FunctionId}: {Message}",
                    functionId, ex.Message);
            }
        }

        /// <summary>Records which test is in flight, so the next one knows what to supersede.</summary>
        private async Task RecordCurrentTestAsync(string functionId, string? runId)
        {
            if (string.IsNullOrEmpty(runId)) return;

            try
            {
                await _cache.CacheDatabase().StringSetAsync(
                    FunctionQueueKeys.CurrentTest(functionId), runId, FunctionQueueKeys.RunTtl);
            }
            catch (Exception ex) when (ex is StackExchange.Redis.RedisException or TimeoutException)
            {
                _logger.LogWarning(
                    "Could not record the current test of function {FunctionId}: {Message}",
                    functionId, ex.Message);
            }
        }

        /// <summary>
        /// The local name the runner gives a test build's image. Mirrors
        /// <c>TestConsumerService.TestImageRef</c>; recorded on the run for display only.
        /// </summary>
        internal static string TestImageRef(string buildId) => $"blocks-test/{buildId.ToLowerInvariant()}:local";

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
                version = await ActiveVersionAsync(tenantId, function.ActiveVersionId, cancellationToken);
            }

            TestBuild? test = null;
            if (version is not null)
            {
                image = FunctionRunImage.For(version);
            }
            else
            {
                // Never deployed — the original was a Test run. It is replayed the way a test runs:
                // a fresh build of the current source, run and deleted on one runner.
                var (build, sourceKey) = await _buildService.CreateTestBuildAsync(tenantId, function, cancellationToken);
                test = new TestBuild(build.ItemId, sourceKey);
                image = TestImageRef(build.ItemId);
            }

            var context = BlocksContext.GetContext();

            var result = await InvokeCoreAsync(
                tenantId, function, version, image, context,
                InvokedByType.Replay, invokedById: originalRun.ItemId, originalRun.Input,
                wait: false, waitTimeoutSeconds: null, cancellationToken, test);
            if (test is not null) result.BuildId ??= test.BuildId;
            return result;
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

            var version = await ActiveVersionAsync(tenantId, function.ActiveVersionId, cancellationToken)
                ?? throw new FunctionNotFoundException($"function '{functionId}' has no active version");

            var authResult = _authorizationService.AuthorizeForWorkflow(function, version, callerContext);
            if (!authResult.Allowed)
            {
                throw new FunctionAuthorizationException(authResult.Reason ?? "not authorized");
            }

            return await InvokeCoreAsync(
                tenantId, function, version, FunctionRunImage.For(version), callerContext,
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
            CancellationToken cancellationToken,
            TestBuild? test = null,
            int? httpWaitSeconds = null,
            bool finishedOnlyWithoutRetry = false)
        {
            var admission = await _admissionService.AdmitAsync(function, tenantId, inputJson, invokedBy, version, cancellationToken);
            StepTimer.Current.Value?.Mark("admission");
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
                // A test is one build and one run; its image is gone afterwards, so there is
                // nothing a retry could run. A workflow step is never retried here either: the step
                // has already reported attempt 1 and moved on, so a background retry would run the
                // function again (side effects included) with nobody reading its result.
                MaxAttempts = test is not null || invokedBy == InvokedByType.Workflow
                    ? 1
                    : Math.Max(1, (version?.Retry ?? function.Retry).Attempts),
            };
            run.IdempotencyKey = $"{run.ItemId}-{run.Attempt}";

            // Warm sandboxes serve every run of a deployed version, whoever calls it — HTTP, a
            // workflow step, a schedule, a replay (user, 2026-10-07: "it must not matter"). The
            // runner's RUNNER__SandboxReuse still gates it per host. Never a test (own build path).
            run.ReuseRequested = test is null && version is not null;

            // Secret-bound variables are NOT resolved here. The envelope keeps each one as its
            // `{{secret.<id>}}` reference because it is written to the Redis run record (and reused
            // verbatim by a retry), and plaintext must never sit in the queue. The runner resolves
            // the references for this tenant immediately before it starts the sandbox, fails the
            // run as SecretUnresolved (naming the variable) when one cannot be read, and as the
            // retryable SecretStoreUnavailable when the store itself is down. A version snapshot
            // keeps the reference too, so rotating a secret takes effect on the next run.
            var envelopeJson = FunctionEnvelopeBuilder.Build(run, version, function, context, inputJson);

            // The caller's identity for ctx.blocks.accessToken, as a delegation grant id the runner
            // redeems right before the sandbox starts — never a token. Written now, while the
            // caller's validated token is still in scope; null for a public trigger, an
            // unauthenticated caller, and anything without a user. An impersonating caller DOES
            // get one: the grant carries the session, and IAM mints a token that still says it is
            // impersonated.
            //
            // Only AuthMode reaches this. The trigger's Roles/Permissions decide who may invoke;
            // they do not narrow the token, so the function runs with everything this user can do.
            //
            // A Test is never anonymous: the endpoint needs a signed-in user, and ctx.context already
            // carries that user whatever the trigger says. A Public trigger describes who may call the
            // deployed route, so it must not leave the editor's own run without a token.
            var grantAuthMode = invokedBy == InvokedByType.Test
                ? AuthMode.Token
                : (version?.Trigger ?? function.Trigger).AuthMode;
            // No grant for a deployed version whose code never names the token: nothing would read
            // it, and redeeming it is an IAM round trip on every call (FunctionTokenUse). A test
            // always gets one — it runs the draft, not this version.
            var mayReadToken = test is not null || version is null || FunctionTokenUse.MayRead(version.Source);

            // Signing the artifact's download URL is a call to the tenant's storage (~170 ms
            // measured), independent of everything below, so it runs beside the grant, the insert
            // and the counter instead of after them. It never throws (null = go by image), so if
            // the call stops before the enqueue the task just finishes unread.
            var artifactUrl = test is null && version is not null && !string.IsNullOrEmpty(version.ArtifactId)
                ? SignArtifactQuietlyAsync(tenantId, function, version, run)
                : null;

            var delegationGrantId = mayReadToken
                ? await _delegation.CreateGrantAsync(tenantId, context, grantAuthMode)
                : null;
            StepTimer.Current.Value?.Mark(mayReadToken ? "grant" : "no-grant");

            // Last point at which the caller going away may stop anything. From the insert on, the
            // record exists, and a cancelled enqueue would strand it QUEUED with nothing queued —
            // so the insert, the counter and the enqueue all run to completion regardless.
            if (cancellationToken.IsCancellationRequested)
            {
                await _delegation.DeleteGrantAsync(delegationGrantId);
                cancellationToken.ThrowIfCancellationRequested();
            }
            await _runRepository.CreateAsync(tenantId, run, CancellationToken.None);
            StepTimer.Current.Value?.Mark("insert");

            // Counted whether or not the enqueue succeeds: the record exists either way and shows
            // in the runs list, so the counter and the list agree. Beside the enqueue rather than
            // before it — a separate document, nothing the queue waits on — and a statistic, so a
            // failure is logged, never a reason to fail a call whose run is already recorded.
            var counter = RecordRunStartedQuietlyAsync(tenantId, function.ItemId, run);

            try
            {
                await EnqueueAsync(tenantId, function, version, run, image, envelopeJson, delegationGrantId, limits, test,
                    artifactUrl);
                await counter;
                StepTimer.Current.Value?.Mark("enqueue+counter");
            }
            catch (Exception ex)
            {
                await counter;
                await CompensateFailedEnqueueAsync(tenantId, run, ex);
                await _delegation.DeleteGrantAsync(delegationGrantId);
                throw new FunctionUnavailableException(
                    "the function could not be queued right now; nothing was executed — retry shortly", run.ItemId);
            }

            if (!wait)
            {
                return new InvokeResultDto { RunId = run.ItemId, Status = FunctionQueueKeys.Wire.Queued };
            }

            var maxSyncWaitSeconds = Math.Max(1, _configuration.GetValue("Functions:SyncWaitMaxSeconds", DefaultSyncWaitMaxSeconds));
            if (invokedBy != InvokedByType.Workflow)
            {
                // Behind an ingress: the lower HTTP cap applies, and never more than the absolute one.
                var httpCap = Math.Max(1, _configuration.GetValue("Functions:HttpSyncWaitMaxSeconds", DefaultHttpSyncWaitMaxSeconds));
                maxSyncWaitSeconds = Math.Min(maxSyncWaitSeconds, httpCap);
            }
            if (httpWaitSeconds is { } asked)
            {
                // A public caller's own bound (Prefer: wait=N, or the sync trigger's cap). Total,
                // not execution-only: it is what the caller agreed to hold the connection for.
                maxSyncWaitSeconds = Math.Max(1, Math.Min(maxSyncWaitSeconds, asked));
            }
            var executionWaitSeconds = Math.Min(
                maxSyncWaitSeconds, (waitTimeoutSeconds ?? limits.TimeoutSeconds) + SyncGraceSeconds);
            // A sync HTTP caller of a deployed version with no output actions can be answered from
            // the runner's own report, without waiting for the Worker to record it (~0.4 s measured
            // 2026-10-06): a success is never retried, and with no output actions there is nothing
            // after it that the answer would have waited for. Everything else waits for the record.
            var answerFromRunner = finishedOnlyWithoutRetry && test is null && version is not null
                && (version.OutputActions?.Count ?? 0) == 0;
            return await WaitForResultAsync(
                tenantId, run.ItemId, executionWaitSeconds, maxSyncWaitSeconds, cancellationToken, finishedOnlyWithoutRetry,
                answerFromRunner, allowStream: httpWaitSeconds is not null && test is null);
        }

        /// <summary>
        /// The answer of a run the runner reported as SUCCEEDED, read from what the runner wrote to
        /// Redis before it said so: the run hash's status and the result key. Null when either is
        /// not what a success leaves (or Redis cannot be read) — the caller then waits for the
        /// record exactly as before.
        /// </summary>
        private async Task<InvokeResultDto?> TryReadRunnerSuccessAsync(string runId)
        {
            try
            {
                var database = _cache.CacheDatabase();
                var statusTask = database.HashGetAsync(FunctionQueueKeys.Run(runId), "status");
                var resultTask = database.StringGetAsync(FunctionQueueKeys.Result(runId));
                await Task.WhenAll(statusTask, resultTask);
                if (statusTask.Result != FunctionQueueKeys.Wire.Succeeded) return null;
                return new InvokeResultDto
                {
                    RunId = runId,
                    Status = FunctionQueueKeys.Wire.Succeeded,
                    Result = resultTask.Result.IsNullOrEmpty ? null : (string?)resultTask.Result,
                };
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                return null;
            }
        }

        /// <summary>
        /// Undoes a run whose enqueue failed: withdraws its payload, so a stream entry that did
        /// land despite the error (a timeout after the write) finds nothing to run, then closes the
        /// record. Each step is best-effort and logged — if Mongo is down too, the stale-run
        /// sweeper closes the record once it finds the payload missing.
        /// </summary>
        private async Task CompensateFailedEnqueueAsync(string tenantId, FunctionRunEntity run, Exception enqueueError)
        {
            _logger.LogError(enqueueError,
                "Could not enqueue run {RunId} of {FunctionId}; closing it as {Code}",
                run.ItemId, run.FunctionId, nameof(RunErrorCode.EnqueueFailed));

            try
            {
                await _cache.CacheDatabase().KeyDeleteAsync(FunctionQueueKeys.Run(run.ItemId));
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not withdraw the payload of run {RunId}: {Message}", run.ItemId, ex.Message);
            }

            try
            {
                await _runRepository.FailIfNotTerminalAsync(
                    tenantId, run.ItemId, RunErrorCode.EnqueueFailed,
                    $"the platform could not queue this run ({enqueueError.GetType().Name}); nothing was executed and it is safe to retry",
                    DateTime.UtcNow, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Could not close run {RunId} after its enqueue failed; the stale-run sweeper will", run.ItemId);
            }
        }

        /// <summary>
        /// How long a run's artifact URL stays valid. It only has to outlive the run's time in the
        /// queue plus its download, and a shorter window means a leaked entry stops being a read
        /// capability sooner.
        /// </summary>
        internal static readonly TimeSpan ArtifactDownloadWindow = TimeSpan.FromHours(1);

        private async Task EnqueueAsync(
            string tenantId, FunctionEntity function, FunctionVersionEntity? version,
            FunctionRunEntity run, string image, string envelopeJson,
            string? delegationGrantId, FunctionLimits limits, TestBuild? test = null,
            Task<string?>? signedArtifactUrl = null)
        {
            var database = _cache.CacheDatabase();
            var runKey = FunctionQueueKeys.Run(run.ItemId);

            // Beside the envelope, not in it: a retry rewrites only `envelope` and `status`, so the
            // grant stays with the run for every attempt, and each attempt redeems it afresh.
            HashEntry[] delegation = string.IsNullOrEmpty(delegationGrantId)
                ? []
                : [new HashEntry(FunctionQueueKeys.RunDelegationField, delegationGrantId)];

            // The payload and its expiry are sent back to back on the one connection (so Redis
            // applies them in that order) and waited for as one round trip; both land before the
            // stream entry below, as before, so a runner never claims a run with no payload.
            var payload = database.HashSetAsync(runKey,
            [
                .. delegation,
                new HashEntry("envelope", envelopeJson),
                new HashEntry("status", FunctionQueueKeys.Wire.Queued),
                new HashEntry("cpuMillicores", limits.CpuMillicores),
                new HashEntry("memoryBytes", (long)limits.MemoryMb * 1024 * 1024),
                new HashEntry("timeoutSeconds", limits.TimeoutSeconds),
                new HashEntry("concurrency", limits.Concurrency),
                new HashEntry("queuedAt", DateTimeOffset.UtcNow.ToString("O")),
            ]);
            var expiry = database.KeyExpireAsync(runKey, FunctionQueueKeys.RunTtl);
            await Task.WhenAll(payload, expiry);

            if (test is not null)
            {
                // One job, one host: the runner that claims this builds the source, runs it and
                // deletes the image. No image reference crosses hosts, so no host can miss it.
                await database.StreamAddAsync(FunctionQueueKeys.TestsStream,
                [
                    new NameValueEntry("runId", run.ItemId),
                    new NameValueEntry("buildId", test.BuildId),
                    new NameValueEntry("functionId", function.ItemId),
                    new NameValueEntry("tenantId", tenantId),
                    new NameValueEntry("sourceKey", test.SourceKey),
                    new NameValueEntry("allowScripts", function.Source.AllowInstallScripts ? "true" : "false"),
                    new NameValueEntry("attempt", run.Attempt),
                    new NameValueEntry("protocol", FunctionQueueKeys.RunProtocolVersion),
                ]);
                return;
            }

            var entry = new List<NameValueEntry>
            {
                new("runId", run.ItemId),
                new("functionId", function.ItemId),
                new("versionId", run.VersionId ?? string.Empty),
                new("tenantId", tenantId),
                new("image", image),
                new("attempt", run.Attempt),
                // The run entry's own version: 2 = env carries secret references for the runner
                // to resolve. An older runner dead-letters it instead of running it.
                new("protocol", FunctionQueueKeys.RunProtocolVersion),
            };

            // Signed per run, not per version: the window only has to cover this run reaching a host
            // that does not have the image yet. A runner that does not understand these fields
            // ignores them and pulls the image as before, so both paths coexist during the cutover.
            if (version is not null && !string.IsNullOrEmpty(version.ArtifactId))
            {
                var artifactUrl = signedArtifactUrl is not null
                    ? await signedArtifactUrl.ConfigureAwait(false)
                    : await ArtifactUrlOrNullAsync(tenantId, function, version, run).ConfigureAwait(false);
                StepTimer.Current.Value?.Mark("artifact-url");

                if (artifactUrl is not null)
                {
                    entry.Add(new NameValueEntry(FunctionQueueKeys.RunArtifactUrlField, artifactUrl));
                    entry.Add(new NameValueEntry(
                        FunctionQueueKeys.RunArtifactSha256Field, version.ArtifactSha256 ?? string.Empty));
                }
            }

            // Warm-sandbox opt-in, decided in InvokeCoreAsync from the deployed version only — never
            // the editable function, so flipping the switch changes nothing until a deploy. Added
            // only when on, so every other run entry stays exactly what it was.
            if (run.ReuseRequested)
            {
                entry.Add(new NameValueEntry(FunctionQueueKeys.RunReuseField, "1"));
            }

            // This request's span, so the runner's and the Worker's spans join the call's trace.
            if (Common.InternalService.Tracing.RunTracing.CurrentTraceParent() is { } traceParent)
            {
                entry.Add(new NameValueEntry(FunctionQueueKeys.TraceParentField, traceParent));
            }

            // This call's Api steps so far, for the run's Timing group (carried by the runner).
            if (StepTimer.Current.Value is { } timer)
            {
                entry.Add(new NameValueEntry(FunctionQueueKeys.RunApiTimingsField, timer.ToCompact()));
            }

            await database.StreamAddAsync(FunctionQueueKeys.RunsStream, [.. entry]);
            StreamWakeup.Publish(database, FunctionQueueKeys.RunsNudgeChannel);
        }

        /// <summary><see cref="ArtifactUrlOrNullAsync"/>, with any failure logged and read as "go by image".</summary>
        private async Task<string?> SignArtifactQuietlyAsync(
            string tenantId, FunctionEntity function, FunctionVersionEntity version, FunctionRunEntity run)
        {
            try
            {
                return await ArtifactUrlOrNullAsync(tenantId, function, version, run).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError("Run {RunId} falls back to the image reference: signing its artifact URL failed ({ExceptionType})",
                    run.ItemId, ex.GetType().Name);
                return null;
            }
        }

        /// <summary>The run counter, best effort: a failure is logged, never thrown.</summary>
        private async Task RecordRunStartedQuietlyAsync(string tenantId, string functionId, FunctionRunEntity run)
        {
            try
            {
                await _functionRepository.RecordRunStartedAsync(tenantId, functionId, run.CreatedDate, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not count run {RunId} on function {FunctionId} ({ExceptionType})",
                    run.ItemId, functionId, ex.GetType().Name);
            }
        }

        /// <summary>
        /// A signed download URL for the version's artifact, or <c>null</c> when the run has to go by
        /// image reference instead — the artifact is gone, or the store is off or misconfigured.
        /// <para>
        /// Tenant storage that cannot serve the artifact is treated like a missing artifact, not as a
        /// reason to refuse: a run may be a workflow step or a public call, and the image path still
        /// exists.
        /// </para>
        /// </summary>
        private async Task<string?> ArtifactUrlOrNullAsync(
            string tenantId, FunctionEntity function, FunctionVersionEntity version, FunctionRunEntity run)
        {
            string? artifactUrl;
            try
            {
                artifactUrl = await _artifacts
                    // Not cancellable, like the rest of the enqueue: the run record already exists.
                    .CreateDownloadUrlAsync(tenantId, version.ArtifactId!, ArtifactDownloadWindow, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Storage.FunctionArtifactStoreUnavailableException ex)
            {
                _logger.LogError(
                    "Run {RunId} for function {FunctionId} falls back to the image reference: {Reason}",
                    run.ItemId, function.ItemId, ex.Message);
                return null;
            }

            if (string.IsNullOrEmpty(artifactUrl))
            {
                // The artifact is gone from the store. Saying so here is better than queueing a
                // run that fails on a host with a 404 it cannot explain.
                _logger.LogError(
                    "Artifact {ArtifactId} for function {FunctionId} version {VersionId} is missing "
                    + "from the store; the run will fall back to the image reference",
                    version.ArtifactId, function.ItemId, run.VersionId);
                return null;
            }

            return artifactUrl;
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
        /// stops a caller being held forever behind a long queue. Whether the run has left the
        /// queue is read from the runner's own status on the Redis payload — the Mongo record
        /// stays QUEUED until the result is applied.
        /// </para>
        /// <para>
        /// The record is re-read when <c>function:sync:{runId}</c> fires (the runner publishes on
        /// completion, the Worker again once the result is written) and otherwise on a timer that
        /// backs off, so a long wait costs a few reads rather than five a second. Cancellation
        /// (the caller disconnecting) ends the wait at once.
        /// </para>
        /// </summary>
        private async Task<InvokeResultDto> WaitForResultAsync(
            string tenantId,
            string runId,
            int executionWaitSeconds,
            int absoluteMaxSeconds,
            CancellationToken cancellationToken,
            bool finishedOnlyWithoutRetry = false,
            bool answerFromRunner = false,
            bool allowStream = false)
        {
            var hardDeadline = DateTime.UtcNow.AddSeconds(absoluteMaxSeconds);
            var deadline = DateTime.UtcNow.AddSeconds(executionWaitSeconds);
            if (deadline > hardDeadline) deadline = hardDeadline;

            var lastStatus = RunStatus.Queued;
            var hasLeftTheQueue = false;

            // Capacity 1: any number of notifications between two reads mean one re-read.
            using var signal = new SemaphoreSlim(0, 1);
            var channel = RedisChannel.Literal(FunctionQueueKeys.SyncChannel(runId));
            // Set when any notification said the run SUCCEEDED. The runner publishes its final
            // status here right after writing the result to Redis (the Worker publishes again once
            // the record holds it), so by the time this is seen the result can be read.
            var succeeded = 0;
            Action<RedisChannel, RedisValue> onNotified = (_, value) =>
            {
                if (value == FunctionQueueKeys.Wire.Succeeded) Volatile.Write(ref succeeded, 1);
                try { signal.Release(); }
                catch (SemaphoreFullException) { /* a re-read is already due */ }
                catch (ObjectDisposedException) { /* the wait already ended */ }
            };

            // A streamed answer (F-5): the first piece hands the caller over to the stream reader,
            // which then owns the connection until the run's end. Only for a sync HTTP caller.
            var streamed = 0;
            var streamChannel = RedisChannel.Literal(FunctionQueueKeys.StreamChannel(runId));
            Action<RedisChannel, RedisValue> onStreamed = (_, _) =>
            {
                Volatile.Write(ref streamed, 1);
                try { signal.Release(); }
                catch (SemaphoreFullException) { /* a re-check is already due */ }
                catch (ObjectDisposedException) { /* the wait already ended */ }
            };
            var streamSubscriber = allowStream ? await TrySubscribeAsync(streamChannel, onStreamed) : null;

            // Subscribed before the first read, so a completion between the read and the
            // subscription cannot be missed.
            var subscriber = await TrySubscribeAsync(channel, onNotified);
            var timer = StepTimer.Current.Value;
            timer?.Mark("subscribe");
            var reads = 0;
            var delay = subscriber is null ? PollInitial : SubscribedPollInitial;
            var cap = subscriber is null ? PollCap : SubscribedPollCap;

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Checked first: a stream that has started — or already ended — is the
                    // answer, so a fast stream is not answered as a plain result.
                    if (Volatile.Read(ref streamed) == 1)
                    {
                        timer?.Mark("streaming");
                        return new InvokeResultDto { RunId = runId, Status = FunctionQueueKeys.Wire.Running, Streaming = true };
                    }

                    var run = await _runRepository.GetByIdAsync(tenantId, runId, cancellationToken);
                    reads++;
                    if (run is not null && FunctionWireMapping.IsTerminal(run.Status))
                    {
                        timer?.Mark($"wait+read#{reads}");
                    }
                    if (run is not null)
                    {
                        lastStatus = run.Status;

                        if (FunctionWireMapping.IsTerminal(run.Status)
                            && finishedOnlyWithoutRetry && FunctionWireMapping.WillBeRetried(run))
                        {
                            // A synchronous HTTP caller must not be answered with a failure the
                            // result consumer is about to replace with another attempt: that
                            // caller gets the 202 and polls the run to its real outcome.
                            return new InvokeResultDto { RunId = runId, Status = FunctionQueueKeys.Wire.Queued };
                        }

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

                        if (!hasLeftTheQueue && run.Status == RunStatus.Queued)
                        {
                            var runnerStatus = await TryReadRunnerStatusAsync(runId);
                            if (runnerStatus is { } started)
                            {
                                // Anchored once, on the first observation that it has started.
                                // After this the deadline stops moving, so the budget measures
                                // execution only.
                                hasLeftTheQueue = true;
                                lastStatus = started;
                            }
                            else
                            {
                                // Still queued: the run has not begun spending its own timeout, so
                                // neither should the caller. Slide the execution budget forward,
                                // but only ever up to the absolute cap.
                                deadline = DateTime.UtcNow.AddSeconds(executionWaitSeconds);
                                if (deadline > hardDeadline) deadline = hardDeadline;
                            }
                        }
                        else
                        {
                            hasLeftTheQueue = true;
                            if (run.Status == RunStatus.Queued) lastStatus = RunStatus.Running;
                        }
                    }

                    var remaining = deadline - DateTime.UtcNow;
                    // Not `<= Zero`: WaitAsync truncates a sub-millisecond timeout to 0 ms and
                    // returns at once, so the last millisecond before the deadline turned into a
                    // tight loop of record reads. The record was just read; stopping this close
                    // to the deadline loses nothing.
                    if (remaining < MinWaitSlice) break;

                    var notified = await signal.WaitAsync(remaining < delay ? remaining : delay, cancellationToken);
                    if (notified) timer?.Mark("notified");

                    if (answerFromRunner && Volatile.Read(ref succeeded) == 1 && Volatile.Read(ref streamed) == 0)
                    {
                        var fromRunner = await TryReadRunnerSuccessAsync(runId);
                        if (fromRunner is not null)
                        {
                            timer?.Mark("runner-result");
                            return fromRunner;
                        }
                        // Not readable (expired, Redis blip): the record is the answer, as before.
                    }
                    delay = notified ? delay : TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, cap.Ticks));
                }
            }
            finally
            {
                if (streamSubscriber is not null)
                {
                    try { await streamSubscriber.UnsubscribeAsync(streamChannel, onStreamed); }
                    catch (Exception ex) { _logger.LogDebug("Could not unsubscribe from {Channel}: {Message}", streamChannel, ex.Message); }
                }
                if (subscriber is not null)
                {
                    try
                    {
                        await subscriber.UnsubscribeAsync(channel, onNotified);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("Could not unsubscribe from {Channel}: {Message}", channel, ex.Message);
                    }
                }
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

        /// <summary>The subscriber, or null when pub/sub is unavailable — the wait then polls alone.</summary>
        private async Task<ISubscriber?> TrySubscribeAsync(RedisChannel channel, Action<RedisChannel, RedisValue> handler)
        {
            try
            {
                var subscriber = _cache.CacheDatabase().Multiplexer.GetSubscriber();
                await subscriber.SubscribeAsync(channel, handler);
                return subscriber;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Could not subscribe to {Channel}; waiting by polling only: {Message}", channel, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// The runner's own view, from the payload hash: <c>Starting</c>/<c>Running</c> once a
        /// runner has taken it, otherwise null (still queued, or unknown).
        /// </summary>
        private async Task<RunStatus?> TryReadRunnerStatusAsync(string runId)
        {
            try
            {
                var value = await _cache.CacheDatabase().HashGetAsync(FunctionQueueKeys.Run(runId), "status");
                if (value.IsNullOrEmpty) return null;
                var status = FunctionWireMapping.ToRunStatus(value.ToString(), out var recognised);
                if (!recognised) return null;
                return status is RunStatus.Claimed or RunStatus.Starting or RunStatus.Running
                    ? status
                    // Terminal on the runner but not yet in Mongo: it certainly left the queue.
                    : FunctionWireMapping.IsTerminal(status) ? RunStatus.Running : null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Could not read the runner status of {RunId}: {Message}", runId, ex.Message);
                return null;
            }
        }
    }
}
