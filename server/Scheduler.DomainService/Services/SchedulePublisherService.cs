using System.Security.Cryptography;
using System.Text;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Scheduler.DomainService.Models;
using Scheduler.DomainService.Repositories;
using Scheduler.DomainService.Utils;
using Blocks.Genesis.Entities.Logic;

namespace Scheduler.DomainService.Services
{
    public class SchedulePublisherService : IDisposable
    {
        /// <summary>Largest <c>Response.Body</c> stored, in UTF-8 bytes, before <see cref="TruncationMarker"/>.</summary>
        internal const int MaxBodyBytes = 16_384;
        internal const string TruncationMarker = "\n…[truncated]";
        internal const string UnreadableBody = "(response body could not be read)";

        private readonly ILogger<SchedulePublisherService> _logger;
        private readonly IScheduleRepository _scheduleDataContext;
        private readonly IMessageClient _messageClient;
        private readonly ITenants _tenants;
        private readonly IScheduleExecutionRecorder _recorder;
        private readonly TimeProvider _timeProvider;
        private readonly HttpClient _httpClient;

        public SchedulePublisherService(
            ILogger<SchedulePublisherService> logger,
            IScheduleRepository scheduleDataContext,
            IMessageClient messageClient,
            ITenants tenants,
            IScheduleExecutionRecorder? recorder = null,
            TimeProvider? timeProvider = null)
        {
            _logger = logger;
            _scheduleDataContext = scheduleDataContext;
            _messageClient = messageClient;
            _tenants = tenants;
            _recorder = recorder ?? NullScheduleExecutionRecorder.Instance;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _httpClient = CreateHttpClient();
        }

        protected virtual HttpClient CreateHttpClient() => new() { Timeout = TimeSpan.FromSeconds(30) };

        protected virtual string BuildSnapshot(Schedule schedule) => ScheduleSnapshot.Build(schedule);

        public void Dispose()
        {
            _httpClient.Dispose();
        }

        public async Task Publish(string scheduleTaskId, string tenantId)
        {
            var schedule = await _scheduleDataContext.GetByIdAsync(scheduleTaskId, tenantId);
            if (schedule == null)
            {
                return;
            }

            if (!schedule.IsActive)
            {
                _logger.LogInformation("Schedule with taskId: {ScheduleTaskId} is inactive.", scheduleTaskId);
                return;
            }

            var today = DateTime.UtcNow;

            if (!ShouldPublishSchedule(schedule, today))
            {
                _logger.LogInformation(
                    "Schedule with taskId: {ScheduleTaskId} skipped: {CurrentDate} is outside its date window (start {StartDate}, end {EndDate}).",
                    scheduleTaskId, today, schedule.StartDate, schedule.EndDate);
                return;
            }

            if (schedule.TriggerType == ScheduleTriggerType.Webhook)
            {
                await PublishToWebhookAsync(schedule, tenantId);
                return;
            }

            await PublishScheduledTask(schedule, tenantId);
        }

        private static bool ShouldPublishSchedule(Schedule schedule, DateTime currentDate)
        {
            var isWithinStartDate = schedule.StartDate is null ||
                                    currentDate >= schedule.StartDate.Value;

            var isWithinEndDate = schedule.EndDate is null ||
                                  currentDate <= schedule.EndDate.Value;

            return isWithinStartDate && isWithinEndDate;
        }

        private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

        private async Task PublishToWebhookAsync(Schedule schedule, string tenantId)
        {
            var webhook = schedule.Webhook;
            if (webhook is null || string.IsNullOrWhiteSpace(webhook.Url))
            {
                _logger.LogWarning("Schedule {ItemId} is a webhook schedule but has no webhook configuration; skipping.", schedule.ItemId);
                var now = UtcNow();
                await RecordAsync(schedule, tenantId, now, now, 422, "Webhook is not configured.");
                return;
            }

            var firedUtc = UtcNow();
            int status;
            string? body;

            try
            {
                using var request = BuildWebhookRequest(webhook, schedule.Payload ?? string.Empty);

                firedUtc = UtcNow();
                using var response = await _httpClient.SendAsync(request);
                status = (int)response.StatusCode;
                body = await ReadBodyAsync(response);
                LogWebhookResponse(schedule, webhook, response);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    "Webhook delivery for schedule {ItemId} to host {Host} failed: {ErrorMessage}.",
                    schedule.ItemId, WebhookHost(webhook.Url), ex.Message);
                status = 502;
                body = $"Network error: {ex.Message}";
            }
            catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Webhook delivery for schedule {ItemId} to host {Host} timed out after {Timeout} seconds.",
                    schedule.ItemId, WebhookHost(webhook.Url), _httpClient.Timeout.TotalSeconds);
                status = 504;
                body = $"Timed out: no response within {_httpClient.Timeout.TotalSeconds:0} s.";
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "Webhook delivery for schedule {ItemId} to host {Host} was cancelled.",
                    schedule.ItemId, WebhookHost(webhook.Url));
                await RecordAsync(schedule, tenantId, firedUtc, UtcNow(), 503, "Cancelled: the scheduler was stopping.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "Webhook delivery for schedule {ItemId} to host {Host} failed with an unexpected error.",
                    schedule.ItemId, WebhookHost(webhook.Url));
                status = 500;
                body = $"Unexpected error: {ex.GetType().Name}: {ex.Message}";
            }

            // The body was read inside the try (before this point), so the finish time covers it.
            await RecordAsync(schedule, tenantId, firedUtc, UtcNow(), status, body);
        }

        /// <summary>The webhook call: JSON payload, the configured headers (Content-Type excepted) and the signature.</summary>
        private static HttpRequestMessage BuildWebhookRequest(WebhookConfiguration webhook, string payload)
        {
            var request = new HttpRequestMessage(new HttpMethod(webhook.Method), webhook.Url);

            if (!string.IsNullOrWhiteSpace(payload))
            {
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            }

            // Content-Type is set on the StringContent itself; ignore any incoming copy.
            foreach (var header in (webhook.Headers ?? Enumerable.Empty<KeyValuePair<string, string>>()).Where(h => !h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (!string.IsNullOrEmpty(webhook.SigningSecret))
            {
                request.Headers.TryAddWithoutValidation("x-signature-sha256", ComputeSignature(webhook.SigningSecret, payload));
            }

            return request;
        }

        private void LogWebhookResponse(Schedule schedule, WebhookConfiguration webhook, HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // Log only — webhook deliveries are fire-and-forget; Hangfire must not retry.
                _logger.LogWarning(
                    "Webhook for schedule {ItemId} to host {Host} returned non-success status code {StatusCode} ({ReasonPhrase}).",
                    schedule.ItemId, WebhookHost(webhook.Url), (int)response.StatusCode, response.ReasonPhrase);
                return;
            }

            _logger.LogInformation(
                "Webhook for schedule {ItemId} delivered with status code {StatusCode}.",
                schedule.ItemId, (int)response.StatusCode);
        }

        internal static string ComputeSignature(string secret, string payload)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private async Task PublishScheduledTask(Schedule schedule, string tenantId)
        {
            var tenant = _tenants.GetTenantByID(tenantId);
            var applicationDomain = tenant.Applications?.FirstOrDefault()?.Domain ?? string.Empty;
            var securityData = BlocksContext.Create(
                tenantId: tenant.TenantId,
                roles: Array.Empty<string>(),
                userId: string.Empty,
                isAuthenticated: false,
                requestUri: string.Empty,
                organizationId: string.Empty,
                expireOn: DateTime.MinValue,
                email: string.Empty,
                permissions: Array.Empty<string>(),
                userName: string.Empty,
                phoneNumber: string.Empty,
                displayName: string.Empty,
                oauthToken: string.Empty,
                originalTenantId: tenant.TenantId,
                applicationDomain: applicationDomain,
                impersonated: false,
                impersonationSessionId: string.Empty);
            BlocksContext.SetContext(securityData, false);

            var publishScheduleCommand = new PublishScheduleCommand().Build(schedule);

            if (schedule.Queue is null || string.IsNullOrWhiteSpace(schedule.Queue.QueueName))
            {
                _logger.LogWarning("Schedule {ItemId} is a queue schedule but has no queue configuration; skipping.", schedule.ItemId);
                var now = UtcNow();
                await RecordAsync(schedule, tenantId, now, now, 422, "Queue is not configured.");
                return;
            }

            var queueName = schedule.Queue.QueueName;
            var firedUtc = UtcNow();
            try
            {
                await _messageClient.SendToConsumerAsync(new ConsumerMessage<PublishScheduleCommand>
                {
                    ConsumerName = queueName,
                    Payload = publishScheduleCommand
                });
            }
            catch (Exception ex)
            {
                // Recorded, then rethrown: Hangfire's automatic retry is unchanged, and each retry adds a row.
                await RecordAsync(schedule, tenantId, firedUtc, UtcNow(), 503, $"Queue send failed: {ex.Message}");
                throw;
            }

            await RecordAsync(schedule, tenantId, firedUtc, UtcNow(), 202, $"Sent to queue {queueName}.");
        }

        /// <summary>
        /// One history row for an Application schedule, written after delivery has finished. Internal (workflow)
        /// schedules are never recorded. Never throws: the snapshot is built in its own try, and the recorder
        /// swallows its own failures.
        /// </summary>
        private async Task RecordAsync(Schedule schedule, string tenantId, DateTime firedUtc, DateTime finishedUtc, int status, string? body)
        {
            if (schedule.Kind != ScheduleKind.Application)
            {
                return;
            }

            string? snapshot = null;
            try
            {
                snapshot = BuildSnapshot(schedule);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Schedule execution snapshot failed for schedule {ScheduleId}; recording without it.", schedule.ItemId);
            }

            var response = new Dictionary<string, object> { [ScheduleExecution.ResponseStatusKey] = status };
            var limited = LimitBody(body);
            if (limited is not null)
            {
                response[ScheduleExecution.ResponseBodyKey] = limited;
            }

            await _recorder.RecordAsync(new ScheduleExecution
            {
                ItemId = Guid.NewGuid().ToString("N"),
                ScheduleId = schedule.ItemId,
                TenantId = tenantId,
                ScheduleName = schedule.Name,
                FiredUtc = firedUtc,
                FinishedUtc = finishedUtc,
                Response = response,
                Snapshot = snapshot,
            });
        }

        private static async Task<string?> ReadBodyAsync(HttpResponseMessage response)
        {
            try
            {
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception)
            {
                return UnreadableBody;
            }
        }

        /// <summary>
        /// Null for empty content (no <c>Body</c> key); otherwise the text, cut to its first
        /// <see cref="MaxBodyBytes"/> UTF-8 bytes at a character boundary plus <see cref="TruncationMarker"/>.
        /// </summary>
        internal static string? LimitBody(string? body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return null;
            }

            if (Encoding.UTF8.GetByteCount(body) <= MaxBodyBytes)
            {
                return body;
            }

            var bytes = 0;
            var index = 0;
            while (index < body.Length)
            {
                var length = char.IsHighSurrogate(body[index]) && index + 1 < body.Length && char.IsLowSurrogate(body[index + 1]) ? 2 : 1;
                var size = Encoding.UTF8.GetByteCount(body.AsSpan(index, length));
                if (bytes + size > MaxBodyBytes)
                {
                    break;
                }

                bytes += size;
                index += length;
            }

            return body[..index] + TruncationMarker;
        }

        /// <summary>
        /// The host only: a webhook URL often carries its secret in the path or query (Slack, Teams, a
        /// <c>?token=</c>), and never belongs in a log (user rule 2026-10-08).
        /// </summary>
        internal static string WebhookHost(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "(invalid url)";
    }
}
