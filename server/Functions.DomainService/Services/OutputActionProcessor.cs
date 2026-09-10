using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Functions.DomainService.Entities;
using Functions.DomainService.Models;
using Microsoft.Extensions.Logging;

namespace Functions.DomainService.Services
{
    /// <summary>The outcome of running a run's whole output-action chain.</summary>
    public sealed record OutputActionChainResult(IReadOnlyList<OutputActionResult> Results, bool AllSucceeded);

    public interface IOutputActionProcessor
    {
        Task<OutputActionChainResult> ProcessAsync(
            string tenantId,
            FunctionRunEntity run,
            IReadOnlyList<OutputAction> actions,
            RetryPolicy retryPolicy,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Delivers a successful run's result to whatever the tenant configured.
    /// <para>
    /// Actions run <b>in order, and stop at the first failure</b>: a chain of actions can
    /// depend on earlier ones having actually happened (e.g. "write the ledger entry, then
    /// notify"), so continuing past a failure would deliver the later steps for a result the
    /// tenant's own first step rejected. The whole run's status reflects that single verdict —
    /// <c>OUTPUT_FAILED</c> — because from the caller's point of view, one broken action is
    /// enough to say the result was not delivered as configured.
    /// </para>
    /// <para>
    /// Retries reuse the function's own <see cref="RetryPolicy"/> (the same Attempts/Backoff a
    /// failed <i>run</i> would use) rather than a second, separate policy — one "Retry" knob
    /// per function instead of two. Because a slow external endpoint can legitimately hold up
    /// to <c>MaxDelaySeconds</c> (5 minutes by default) between attempts, this must never run
    /// on the Worker's single result-consuming loop: see <c>FunctionResultConsumer</c>, which
    /// processes results with bounded concurrency for exactly this reason.
    /// </para>
    /// <para>
    /// The idempotency header carries <c>{runId}-{attempt}</c>, where <c>attempt</c> is the
    /// <i>run's</i> delivery attempt — it stays constant across every HTTP-level retry of one
    /// action within one run attempt, because all of those retries are, from the receiving
    /// endpoint's point of view, the same logical delivery.
    /// </para>
    /// <para>
    /// Only <c>ExternalHttp</c> actions are executed. <c>BlocksProxy</c> is modelled (DECISIONS
    /// open question #2) but shown disabled in the interface until a platform proxy service is
    /// confirmed to exist; if one is ever saved anyway, it is skipped here rather than
    /// attempted, silently, because there is nothing to call.
    /// </para>
    /// </summary>
    public class OutputActionProcessor : IOutputActionProcessor
    {
        // {{secret.<id>}} — ids come from the catalog, so word characters, dash and underscore
        // cover every id the platform can hand out.
        private static readonly Regex SecretPlaceholder = new(@"\{\{secret\.([\w-]+)\}\}", RegexOptions.Compiled);
        private const string ResultPlaceholder = "{{result}}";

        private readonly ISecretResolver _secretResolver;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<OutputActionProcessor> _logger;

        public OutputActionProcessor(
            ISecretResolver secretResolver, IHttpClientFactory httpClientFactory, ILogger<OutputActionProcessor> logger)
        {
            _secretResolver = secretResolver;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<OutputActionChainResult> ProcessAsync(
            string tenantId,
            FunctionRunEntity run,
            IReadOnlyList<OutputAction> actions,
            RetryPolicy retryPolicy,
            CancellationToken cancellationToken = default)
        {
            var results = new List<OutputActionResult>();

            foreach (var action in actions)
            {
                if (!action.Enabled) continue;

                if (action.Kind != Enums.OutputActionKind.ExternalHttp)
                {
                    // BlocksProxy: nothing to call yet (open question #2). Not a failure —
                    // the tenant cannot have configured a working one, so there is nothing
                    // this run's own outcome should be blamed for.
                    continue;
                }

                var result = await ExecuteHttpActionAsync(tenantId, run, action, retryPolicy, cancellationToken);
                results.Add(result);

                if (!result.Ok)
                {
                    return new OutputActionChainResult(results, AllSucceeded: false);
                }
            }

            return new OutputActionChainResult(results, AllSucceeded: true);
        }

        private async Task<OutputActionResult> ExecuteHttpActionAsync(
            string tenantId, FunctionRunEntity run, OutputAction action, RetryPolicy retryPolicy,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var maxAttempts = Math.Max(1, retryPolicy.Attempts);
            var idempotencyKey = $"{run.ItemId}-{run.Attempt}";

            string url;
            IReadOnlyDictionary<string, string> headers;
            string? body;
            try
            {
                (url, headers, body) = await SubstituteAsync(tenantId, action, run.Result, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not prepare output action {ActionId} for run {RunId}", action.Id, run.ItemId);
                return Failure(action, stopwatch, attempts: 0, error: $"could not prepare the request: {ex.Message}");
            }

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var client = _httpClientFactory.CreateClient(nameof(OutputActionProcessor));
                    client.Timeout = TimeSpan.FromSeconds(Math.Max(1, action.TimeoutSeconds));

                    using var request = new HttpRequestMessage(new HttpMethod(action.Method), url);
                    foreach (var (name, value) in headers)
                    {
                        request.Headers.TryAddWithoutValidation(name, value);
                    }
                    request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

                    if (body is not null)
                    {
                        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    }

                    using var response = await client.SendAsync(request, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        stopwatch.Stop();
                        return new OutputActionResult
                        {
                            ActionId = action.Id,
                            Kind = action.Kind,
                            Ok = true,
                            StatusCode = (int)response.StatusCode,
                            DurationMs = stopwatch.ElapsedMilliseconds,
                            Attempts = attempt,
                        };
                    }

                    if (attempt == maxAttempts)
                    {
                        stopwatch.Stop();
                        return new OutputActionResult
                        {
                            ActionId = action.Id,
                            Kind = action.Kind,
                            Ok = false,
                            StatusCode = (int)response.StatusCode,
                            Error = $"received HTTP {(int)response.StatusCode} after {attempt} attempt(s)",
                            DurationMs = stopwatch.ElapsedMilliseconds,
                            Attempts = attempt,
                        };
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (attempt == maxAttempts)
                    {
                        return Failure(action, stopwatch, attempt, ex.Message);
                    }
                    _logger.LogInformation(
                        "Output action {ActionId} for run {RunId} failed on attempt {Attempt}: {Message}",
                        action.Id, run.ItemId, attempt, ex.Message);
                }

                await Task.Delay(retryPolicy.DelayFor(attempt + 1), cancellationToken);
            }

            // Unreachable: the loop above always returns on its last iteration.
            return Failure(action, stopwatch, maxAttempts, "exhausted retries");
        }

        private static OutputActionResult Failure(OutputAction action, Stopwatch stopwatch, int attempts, string error)
        {
            stopwatch.Stop();
            return new OutputActionResult
            {
                ActionId = action.Id,
                Kind = action.Kind,
                Ok = false,
                Error = error,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Attempts = attempts,
            };
        }

        /// <summary>
        /// Resolves every <c>{{secret.&lt;id&gt;}}</c> placeholder across the URL, headers and
        /// body template in one batch, then substitutes. <c>{{result}}</c> in the body template
        /// is replaced with the run's raw result JSON verbatim; a null template means the whole
        /// body <i>is</i> the result JSON.
        /// </summary>
        private async Task<(string Url, IReadOnlyDictionary<string, string> Headers, string? Body)> SubstituteAsync(
            string tenantId, OutputAction action, string? resultJson, CancellationToken cancellationToken)
        {
            var bodyTemplate = action.BodyTemplate ?? resultJson;

            var ids = new HashSet<string>(StringComparer.Ordinal);
            CollectIds(action.Url, ids);
            foreach (var value in action.Headers.Values) CollectIds(value, ids);
            CollectIds(bodyTemplate, ids);

            var secrets = ids.Count == 0
                ? new Dictionary<string, string>()
                : await _secretResolver.ResolveAsync(ids, tenantId, cancellationToken);

            var url = Substitute(action.Url, secrets, resultJson: null);
            var headers = action.Headers.ToDictionary(
                h => h.Key, h => Substitute(h.Value, secrets, resultJson: null), StringComparer.Ordinal);
            var body = bodyTemplate is null ? null : Substitute(bodyTemplate, secrets, resultJson);

            return (url, headers, body);
        }

        private static void CollectIds(string? text, HashSet<string> into)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (Match match in SecretPlaceholder.Matches(text))
            {
                into.Add(match.Groups[1].Value);
            }
        }

        private static string Substitute(string text, IReadOnlyDictionary<string, string> secrets, string? resultJson)
        {
            var substituted = SecretPlaceholder.Replace(text, match =>
                secrets.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);

            if (resultJson is not null)
            {
                substituted = substituted.Replace(ResultPlaceholder, resultJson, StringComparison.Ordinal);
            }

            return substituted;
        }
    }
}
