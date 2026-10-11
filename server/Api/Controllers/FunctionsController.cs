using Blocks.Genesis;
using Common.InternalService.Access;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Models;
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
    /// reached by callers who are not necessarily Blocks users at all. It is built the way the
    /// proxy gateway is: a catch-all route on every method, anonymous at the framework level, the
    /// tenant resolved from <c>x-blocks-key</c> by the shared access authorizer, and the stored
    /// "Who can call it" enforced per call. Its routes are absolute (<c>~/api/…</c>) because they
    /// do not follow the controller's template.
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
        private readonly IEndpointAccessAuthorizer _accessAuthorizer;
        private readonly IFunctionPollTokenService _pollTokens;
        private readonly ILogger<FunctionsController> _logger;

        /// <summary>
        /// The management services, plus the shared access authorizer that resolves the tenant on
        /// the anonymous <see cref="Invoke"/> route — the same one the proxy gateway uses — and the
        /// poll-token service behind the anonymous <see cref="PollRunResult"/>.
        /// </summary>
        public FunctionsController(
            IFunctionService functionService,
            IFunctionDeploymentService deploymentService,
            IFunctionInvocationService invocationService,
            IFunctionRunService runService,
            IFunctionBuildService buildService,
            IFunctionAuditService auditService,
            IEndpointAccessAuthorizer accessAuthorizer,
            IFunctionPollTokenService pollTokens,
            ILogger<FunctionsController> logger)
        {
            _functionService = functionService;
            _deploymentService = deploymentService;
            _invocationService = invocationService;
            _runService = runService;
            _buildService = buildService;
            _auditService = auditService;
            _accessAuthorizer = accessAuthorizer;
            _pollTokens = pollTokens;
            _logger = logger;
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
        public async Task<ActionResult<InvokeResultDto>> Test([FromBody] TestFunctionRequestDto request)
            => await MapInvokeRefusalsAsync(() => _invocationService.TestAsync(GetTenantId(), request.FunctionId, request));

        [HttpPost]
        [ProtectedEndPoint("blocks-logic::function::manage")]
        public async Task<IActionResult> Deploy([FromBody] DeployFunctionRequestDto request)
        {
            var outcome = await _deploymentService.DeployAsync(GetTenantId(), request, GetUserId(), GetEmail());
            // 200 + the version, as before; 202 + the build when it is still running (F-4). The
            // console watches GetBuild and then deploys that build with `buildId`.
            return outcome.Version is { } version
                ? Ok(version)
                : Accepted(new DeployPendingDto { BuildId = outcome.BuildId!, Status = outcome.BuildStatus! });
        }

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
        public async Task<ActionResult<InvokeResultDto>> ReplayRun([FromBody] RunIdRequestDto request)
            => await MapInvokeRefusalsAsync(() => _runService.ReplayAsync(GetTenantId(), request.RunId, GetUserId(), GetEmail()));

        /// <summary>
        /// The two refusals a Studio-side invocation (Test, Replay) can hit that are the caller's
        /// to act on, answered as such instead of escaping as a 500: a credential-shaped key the
        /// control plane refused to put in an envelope (400), the function's own rate limit
        /// (429 with <c>Retry-After</c>), and a run queue that refused the run (503 with <c>Retry-After</c>). Everything else keeps the pipeline's existing handling.
        /// </summary>
        private async Task<ActionResult<InvokeResultDto>> MapInvokeRefusalsAsync(Func<Task<InvokeResultDto>> invoke)
        {
            try
            {
                return await invoke();
            }
            catch (FunctionEnvelopeBuilder.ForbiddenContentException ex)
            {
                return StatusCode(400, new { code = "FUNCTION_FORBIDDEN_CONTENT", message = ex.Message });
            }
            catch (FunctionRateLimitedException ex)
            {
                Response.Headers.RetryAfter = ex.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return StatusCode(429, new { code = "FUNCTION_RATE_LIMITED", message = ex.Message });
            }
            catch (FunctionUnavailableException ex)
            {
                Response.Headers.RetryAfter = ex.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return StatusCode(503, new { code = "FUNCTION_UNAVAILABLE", message = ex.Message, runId = ex.RunId });
            }
        }

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
        /// Same shape as the proxy gateway's own cap: a little above the service's ceiling, so the
        /// service — not Kestrel's generic 413 — is what answers an oversized body, with the
        /// documented error shape. Kestrel still stops a runaway upload here.
        /// </summary>
        private const long InvokeHardBodyLimitBytes = FunctionLimits.Ceiling.InputBytes + 64 * 1024;

        /// <summary>
        /// The public function endpoint (DECISIONS D5): <c>{METHOD} /api/fn/{functionId}/{**path}</c>.
        /// A tenant's client calls this instead of hosting the code. The route is registered for
        /// GET, POST, PUT, PATCH and DELETE; the trigger lists the ones it answers (or, when it
        /// lists none, its single legacy method), and every other verb is refused with 405 and an
        /// <c>Allow</c> header once the caller is authorized. Anything after the id is passed to the handler as
        /// <c>input.path</c>, with the method, query, allow-listed headers and body alongside it
        /// (see <c>FunctionHttpInputBuilder</c>).
        /// <para>
        /// The tenant comes from <c>x-blocks-key</c> (or the tenant claim of a presented token),
        /// resolved by the shared <see cref="IEndpointAccessAuthorizer"/>, never from the route, the
        /// query or the body. There is exactly one tenant in this request and it is the one whose
        /// certificate then has to validate any token presented — a second, unvalidated tenant id
        /// in the path would let any valid key reach a <c>Public</c> function in whatever other
        /// tenant the caller could name.
        /// </para>
        /// <para>
        /// <see cref="AllowAnonymousAttribute"/> at the framework level, like the proxy gateway: a
        /// tenant key identifies a tenant but authenticates no user, and whether a caller may
        /// proceed is the deployed version's own "Who can call it" (public, or a Blocks token
        /// optionally narrowed by roles / permissions), decided per stored policy inside
        /// <c>FunctionInvocationService.InvokeHttpAsync</c>. The Genesis bearer handler skips
        /// anonymous actions entirely, so that service is where the token is validated. Saying
        /// anonymous explicitly also keeps the endpoint public if this class ever acquires a
        /// type-level authorization attribute.
        /// </para>
        /// <para>
        /// The route is absolute (<c>~/</c>) so it stays <c>/api/fn/…</c> whatever this controller
        /// or method is called: it is a published contract clients hard-code. That also puts it
        /// outside <c>GlobalApiRoutePrefixConvention</c>, which only prefixes controller-level
        /// templates — hence the <c>api</c> segment written out here, once.
        /// </para>
        /// <para>
        /// Errors use the gateway's body shape, <c>{ code, message, instance }</c>, with
        /// <c>FUNCTION_INVOKE_*</c> codes: 401 for no tenant or no usable token, 403 when the token
        /// validates but the rules refuse it (or HTTP is switched off), 404 for an unknown or
        /// undeployed function — but only to a caller who could have reached it, so 401 comes
        /// first — 405 with an <c>Allow</c> header for the method the trigger does not take, 413
        /// over the body ceiling, 400 for input the sandbox cannot be given, 429 with
        /// <c>Retry-After</c> when the function's own rate limit refuses the call (only with
        /// <c>Functions:RateLimits:Enabled</c>).
        /// </para>
        /// <para>
        /// <b>Asynchronous by default.</b> The answer is 202 with a run id and a <c>pollToken</c>; the
        /// caller collects the outcome from <see cref="PollRunResult"/>. That stays byte-for-byte
        /// what a caller gets unless it asks for more (sandbox/REUSE.md).
        /// </para>
        /// <para>
        /// <b>Synchronous only for a function deployed as sync.</b> When the deployed trigger's
        /// <c>ResponseMode</c> is <c>sync</c>, the request is held until the run finishes and the
        /// function's own answer is the HTTP response (<see cref="FunctionHttpResponseMapper"/>: its
        /// <c>{ statusCode, headers, body }</c> through a header allow-list, or 200 JSON; 502 for a
        /// failed run, 504 for a timed-out one, both with the run id). The caller may shorten the
        /// wait with <c>Prefer: wait=&lt;seconds&gt;</c> or skip it with <c>Prefer: respond-async</c>;
        /// on an async function <c>Prefer</c> changes nothing. A failure that is about to be retried
        /// is not an answer — the caller gets the 202. Sync was removed once, for three reasons, and
        /// each is answered now:
        /// <list type="bullet">
        /// <item><i>Latency bought too little.</i> A run was always a fresh sandbox (~2.6 s of
        /// platform start). With <c>ReuseSandbox</c> a warm sandbox answers in milliseconds, so
        /// holding the request is worth it.</item>
        /// <item><i>The connection was held for the whole run, past ingress timeouts.</i> The wait is
        /// bounded: never longer than <c>Functions:HttpSyncWaitMaxSeconds</c> (30 s by default, under
        /// typical 60 s ingress idle timeouts), never longer than the caller's own <c>wait=</c>, and
        /// at most <c>Functions:MaxConcurrentSyncWaits</c> (256) requests are held per process —
        /// beyond that a call is answered async at once.</item>
        /// <item><i>A dropped connection lost the result.</i> Not any more: the run is queued, with
        /// its record written, before the wait starts, and nothing in the wait can cancel it. A run
        /// that has not finished in the window is answered with exactly the async 202 + poll token,
        /// so the caller can always collect the result — and a caller that went away simply never
        /// reads an answer the run still produces and records.</item>
        /// </list>
        /// The <c>wait</c> query parameter is still accepted and ignored (and not passed to the
        /// handler), so an older client gets an answer rather than a 400.
        /// </para>
        /// </summary>
        [AllowAnonymous]
        [RequestSizeLimit(InvokeHardBodyLimitBytes)]
        [AcceptVerbs("GET", "POST", "PUT", "PATCH", "DELETE", Route = "~/api/fn/{functionId}/{**path}")]
        public async Task<IActionResult> Invoke(string functionId, string? path)
        {
            var instance = Request.Path.Value ?? $"/api/fn/{functionId}/{path}";
            var aborted = HttpContext.RequestAborted;

            var tenantId = await _accessAuthorizer.ResolveTenantIdAsync(Request);
            if (string.IsNullOrEmpty(tenantId))
            {
                return InvokeError(401, "UNAUTHORIZED", "Missing or invalid credentials for this tenant.", instance);
            }

            // Buffered under the ceiling before anything is queued: the runtime refuses an envelope
            // over 1 MB outright, so accepting more would only create a run that can fail.
            var (body, tooLarge) = await ReadBodyAsync(aborted);
            var (preferAsync, preferWaitSeconds) = ParsePrefer(Request.Headers["Prefer"]);

            var request = new InvokeFunctionRequestDto
            {
                Method = Request.Method,
                Path = path,
                // `wait` is the platform's, not the caller's payload to the function, so the
                // handler does not see it; everything else in the query is theirs and passes through.
                Query = Request.Query
                    .Where(q => !string.Equals(q.Key, "wait", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(q => q.Key, q => q.Value.Where(v => v is not null).Select(v => v!).ToArray(), StringComparer.Ordinal),
                Headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase),
                ContentType = string.IsNullOrWhiteSpace(Request.ContentType) ? null : Request.ContentType,
                Body = tooLarge ? null : body,
                BodyTooLarge = tooLarge,
                // Never set here. Whether this call waits is the service's decision — it alone
                // knows the deployed trigger's response mode — from the two Prefer values below.
                // (A workflow step waits through InvokeFromWorkflowAsync, not through this route.)
                Wait = false,
                PreferAsync = preferAsync,
                PreferWaitSeconds = preferWaitSeconds,
            };

            try
            {
                var result = await _invocationService.InvokeHttpAsync(tenantId, functionId, request, aborted);

                // The function streams its answer (F-5): the pieces, as they come.
                if (result.Streaming)
                {
                    var sse = Request.Headers.Accept.Any(a => a?.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) == true);
                    return new FunctionStreamResult(result.RunId, sse);
                }

                // 202 for any run not yet finished — queued, or claimed/starting/running/processing
                // outputs when Wait's window lapsed; 200 once a terminal outcome is known. Only a
                // recognised run status counts as in flight: a build status (BuildId set) keeps
                // its existing 200 shape.
                var runStatus = FunctionWireMapping.ToRunStatus(result.Status, out var isRunStatus);
                if (result.RespondSynchronously && isRunStatus && FunctionWireMapping.IsTerminal(runStatus))
                {
                    // Sync mode, finished in time: the function's own answer is the response.
                    return SyncAnswer(FunctionHttpResponseMapper.Map(result));
                }

                if (!isRunStatus || FunctionWireMapping.IsTerminal(runStatus))
                {
                    return Ok(result);
                }

                // Not finished: the caller gets a poll token, because a caller of a Public
                // function has no Blocks identity to read the run with (see PollRunResult).
                result.PollToken = await TryIssuePollTokenAsync(tenantId, result.RunId, aborted);
                return Accepted(result);
            }
            catch (OperationCanceledException) when (aborted.IsCancellationRequested)
            {
                // The caller hung up (typically during a sync wait). Nobody reads an answer, and
                // the run itself is unaffected — it was queued before the wait began and nothing in
                // the wait can cancel it. Not an error, so not logged as one.
                return new EmptyResult();
            }
            catch (FunctionRateLimitedException ex)
            {
                Response.Headers.RetryAfter = ex.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return InvokeError(429, "RATE_LIMITED", ex.Message, instance);
            }
            catch (FunctionUnavailableException ex)
            {
                // The run queue refused; nothing ran and the run record (if any) is closed as
                // EnqueueFailed. The same request is expected to work shortly.
                Response.Headers.RetryAfter = ex.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return InvokeError(503, "UNAVAILABLE", ex.Message, instance);
            }
            catch (FunctionAuthorizationException ex)
            {
                return InvokeError(401, "UNAUTHORIZED", ex.Message, instance);
            }
            catch (FunctionForbiddenException ex)
            {
                return InvokeError(403, "FORBIDDEN", ex.Message, instance);
            }
            catch (FunctionNotFoundException ex)
            {
                return InvokeError(404, "NOT_FOUND", ex.Message, instance);
            }
            catch (FunctionMethodNotAllowedException ex)
            {
                Response.Headers.Allow = ex.Allowed;
                return InvokeError(405, "METHOD_NOT_ALLOWED", ex.Message, instance);
            }
            catch (FunctionRequestTooLargeException ex)
            {
                return InvokeError(413, "REQUEST_TOO_LARGE", ex.Message, instance);
            }
            catch (FunctionValidationException ex)
            {
                return InvokeError(400, "INVALID_REQUEST", ex.Message, instance);
            }
            catch (FunctionEnvelopeBuilder.ForbiddenContentException ex)
            {
                // A credential-shaped key in the query or body. The sandbox never receives it;
                // the caller is told why rather than getting a 500.
                return InvokeError(400, "FORBIDDEN_CONTENT", ex.Message, instance);
            }
        }

        /// <summary>The mapped synchronous answer as an action result; see <see cref="FunctionSyncAnswerResult"/>.</summary>
        private static IActionResult SyncAnswer(FunctionHttpResponseMapper.Response answer) => new FunctionSyncAnswerResult(answer);

        /// <summary>
        /// Reads RFC 7240 <c>Prefer</c>: <c>respond-async</c> and <c>wait=&lt;seconds&gt;</c>, in
        /// any order, across any number of header lines, case-insensitively. Anything else in the
        /// header — and a <c>wait</c> that is not a whole number — is ignored, as the RFC asks of
        /// preferences a server does not understand.
        /// </summary>
        [NonAction]
        public static (bool RespondAsync, int? WaitSeconds) ParsePrefer(Microsoft.Extensions.Primitives.StringValues values)
        {
            var respondAsync = false;
            int? wait = null;

            foreach (var line in values)
            {
                if (string.IsNullOrEmpty(line)) continue;
                foreach (var raw in line.Split(','))
                {
                    // A preference may carry parameters after ';' — only its own name=value matters.
                    var preference = raw.Split(';')[0].Trim();
                    if (preference.Length == 0) continue;

                    var eq = preference.IndexOf('=');
                    var name = (eq < 0 ? preference : preference[..eq]).Trim();
                    var value = eq < 0 ? null : preference[(eq + 1)..].Trim().Trim('"');

                    if (string.Equals(name, "respond-async", StringComparison.OrdinalIgnoreCase))
                    {
                        respondAsync = true;
                    }
                    else if (string.Equals(name, "wait", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(value, System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                    {
                        wait = seconds;
                    }
                }
            }

            return (respondAsync, wait);
        }

        /// <summary>
        /// Reads the body up to <see cref="FunctionHttpInputBuilder.MaxBodyBytes"/>, the same way the
        /// proxy gateway buffers its own. Returns <c>(null, true)</c> the moment the cap is passed —
        /// by the declared length, or by the bytes actually read — so nothing over it is held.
        /// </summary>
        private async Task<(byte[]? Body, bool TooLarge)> ReadBodyAsync(CancellationToken cancellationToken)
        {
            var cap = FunctionHttpInputBuilder.MaxBodyBytes;

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

        /// <summary>Blocks-generated error body, the gateway's shape: <c>{ code: "FUNCTION_INVOKE_&lt;OUTCOME&gt;", message, instance }</c>.</summary>
        private IActionResult InvokeError(int statusCode, string outcome, string message, string instance) =>
            StatusCode(statusCode, new { code = "FUNCTION_INVOKE_" + outcome, message, instance });

        /// <summary>
        /// Mints the 202's poll token. A failure here (Redis unavailable) must not fail the call:
        /// the run is already queued, and a 5xx would only invite a retry that queues it twice.
        /// The caller then gets a 202 without a token — the same as before tokens existed.
        /// </summary>
        private async Task<string?> TryIssuePollTokenAsync(string tenantId, string runId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(runId)) return null;
            try
            {
                return await _pollTokens.IssueAsync(tenantId, runId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not issue a poll token for run {RunId}; returning 202 without one", runId);
                return null;
            }
        }

        /// <summary>
        /// The poll half of Fire/Poll for a Blocks user: <c>GET /api/fn/runs/{runId}</c>. The full
        /// run record — input included — so it takes the same <c>function::read</c> permission as
        /// <see cref="GetRun"/>, and reads only within the caller's own tenant. A signed-in user
        /// without that permission polls with the run's token instead (<see cref="PollRunResult"/>).
        /// </summary>
        [HttpGet("~/api/fn/runs/{runId}")]
        [ProtectedEndPoint("blocks-logic::function::read")]
        public async Task<RunDetailDto> PollRun(string runId)
            => await _runService.GetAsync(GetTenantId(), runId);

        /// <summary>
        /// The anonymous poll: <c>GET /api/fn/runs/{runId}/result</c> with the <c>x-poll-token</c>
        /// header from the 202, and the same <c>x-blocks-key</c> the invoke used. Answers status,
        /// result and error only — never the input or headers.
        /// <para>
        /// The token rides in a header, not the query, so it does not land in access logs. Every
        /// refusal after the tenant check — missing, malformed, wrong or expired token, a token for
        /// another tenant's run, a run that no longer exists — is the same 404, so the route says
        /// nothing about which run ids exist. Anonymous at the framework level for the same reason
        /// <see cref="Invoke"/> is.
        /// </para>
        /// </summary>
        [AllowAnonymous]
        [HttpGet("~/api/fn/runs/{runId}/result")]
        public async Task<IActionResult> PollRunResult(string runId)
        {
            var instance = Request.Path.Value ?? $"/api/fn/runs/{runId}/result";
            var aborted = HttpContext.RequestAborted;

            var tenantId = await _accessAuthorizer.ResolveTenantIdAsync(Request);
            if (string.IsNullOrEmpty(tenantId))
            {
                return InvokeError(401, "UNAUTHORIZED", "Missing or invalid credentials for this tenant.", instance);
            }

            var token = Request.Headers.TryGetValue(PollTokenHeader, out var values) ? values.ToString() : null;
            const string notFound = "No run with that id and poll token.";

            // Run ids are GUIDs. Anything else is refused before it becomes a Redis key.
            if (!Guid.TryParse(runId, out _))
            {
                return InvokeError(404, "NOT_FOUND", notFound, instance);
            }

            bool verified;
            try
            {
                verified = await _pollTokens.VerifyAsync(tenantId, runId, token, aborted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not verify the poll token for run {RunId}", runId);
                return InvokeError(503, "UNAVAILABLE", "The run status is temporarily unavailable; try again.", instance);
            }

            if (!verified)
            {
                return InvokeError(404, "NOT_FOUND", notFound, instance);
            }

            try
            {
                return Ok(await _runService.GetPollResultAsync(tenantId, runId, aborted));
            }
            catch (FunctionNotFoundException)
            {
                return InvokeError(404, "NOT_FOUND", notFound, instance);
            }
        }

        /// <summary>The request header that carries the 202's poll token.</summary>
        public const string PollTokenHeader = "x-poll-token";

        // ----------------------------------------------------------------- helpers ----

        private static string GetTenantId() => BlocksContext.GetContext()?.TenantId ?? string.Empty;
        private static string? GetUserId() => BlocksContext.GetContext()?.UserId;
        private static string? GetEmail() => BlocksContext.GetContext()?.Email;
    }

    /// <summary>
    /// Writes a function's synchronous answer exactly: its status, its filtered headers (a repeated
    /// name, e.g. several <c>Set-Cookie</c>, is appended, not replaced) and its body as raw bytes,
    /// so a string body reaches the caller as the function returned it with no second JSON
    /// encoding and no output formatter in between.
    /// </summary>
    public sealed class FunctionSyncAnswerResult(FunctionHttpResponseMapper.Response answer) : IActionResult
    {
        public FunctionHttpResponseMapper.Response Answer { get; } = answer;

        public async Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;
            response.StatusCode = Answer.StatusCode;
            foreach (var (name, value) in Answer.Headers)
            {
                response.Headers.Append(name, value);
            }
            if (Answer.ContentType is not null)
            {
                response.ContentType = Answer.ContentType;
            }
            if (Answer.Body.Length > 0)
            {
                response.ContentLength = Answer.Body.Length;
                await response.Body.WriteAsync(Answer.Body, context.HttpContext.RequestAborted);
            }
        }
    }

    /// <summary>
    /// A streamed answer (F-5): 200, then each piece the moment the runner relays it — as plain
    /// chunked text, or as Server-Sent Events when the caller accepts <c>text/event-stream</c>
    /// (each piece one <c>data:</c> event; a final <c>event: done</c>, or <c>event: error</c>
    /// with the run's status). A plain-text stream whose run fails after it started has no way to
    /// say so but to stop short: the connection is aborted, so the client sees an incomplete body
    /// rather than a clean end. The run id is in <c>x-blocks-run-id</c> for looking it up.
    /// </summary>
    public sealed class FunctionStreamResult(string runId, bool sse) : IActionResult
    {
        public string RunId { get; } = runId;
        public bool Sse { get; } = sse;

        public async Task ExecuteResultAsync(ActionContext context)
        {
            var http = context.HttpContext;
            var reader = http.RequestServices.GetRequiredService<IFunctionStreamReader>();
            var response = http.Response;
            var aborted = http.RequestAborted;

            response.StatusCode = 200;
            response.ContentType = Sse ? "text/event-stream; charset=utf-8" : "text/plain; charset=utf-8";
            response.Headers.CacheControl = "no-cache";
            response.Headers["X-Accel-Buffering"] = "no";   // nginx-style proxies: do not hold pieces back
            response.Headers["x-blocks-run-id"] = RunId;
            http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();
            await response.StartAsync(aborted);

            try
            {
                await foreach (var piece in reader.ReadAsync(RunId, aborted))
                {
                    if (!piece.IsEnd)
                    {
                        await response.WriteAsync(Sse ? SseData(piece.Data!) : piece.Data!, aborted);
                        await response.Body.FlushAsync(aborted);
                        continue;
                    }

                    if (piece.EndStatus == FunctionQueueKeys.Wire.Succeeded)
                    {
                        if (Sse) await response.WriteAsync("event: done\ndata: {}\n\n", aborted);
                        return;
                    }

                    if (Sse)
                    {
                        var error = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            runId = RunId, status = piece.EndStatus, errorCode = piece.ErrorCode, message = piece.ErrorMessage,
                        });
                        await response.WriteAsync($"event: error\ndata: {error}\n\n", aborted);
                        return;
                    }
                    http.Abort();
                    return;
                }
            }
            catch (OperationCanceledException) when (aborted.IsCancellationRequested)
            {
                // The caller left; the run goes on and is recorded as usual.
            }
        }

        /// <summary>One SSE event: every line of the piece as its own <c>data:</c> line.</summary>
        internal static string SseData(string data) =>
            string.Concat(data.Replace("\r\n", "\n").Split('\n').Select(line => "data: " + line + "\n")) + "\n";
    }

    public sealed class RunIdRequestDto
    {
        public string RunId { get; set; } = string.Empty;
    }

    /// <summary>A deploy whose build is still running: watch <see cref="BuildId"/>, then deploy it.</summary>
    public sealed class DeployPendingDto
    {
        public string BuildId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
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
