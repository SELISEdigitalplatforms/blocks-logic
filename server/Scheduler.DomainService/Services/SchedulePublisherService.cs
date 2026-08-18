using System.Security.Cryptography;
using System.Text;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Scheduler.DomainService.Entities;
using Scheduler.DomainService.Enums;
using Scheduler.DomainService.Models;
using Scheduler.DomainService.Repositories;
using Scheduler.DomainService.Utils;

namespace Scheduler.DomainService.Services
{
    public class SchedulePublisherService : IDisposable
    {
        private readonly ILogger<SchedulePublisherService> _logger;
        private readonly IScheduleRepository _scheduleDataContext;
        private readonly IMessageClient _messageClient;
        private readonly ITenants _tenants;
        private readonly HttpClient _httpClient;

        public SchedulePublisherService(
            ILogger<SchedulePublisherService> logger,
            IScheduleRepository scheduleDataContext,
            IMessageClient messageClient,
            ITenants tenants)
        {
            _logger = logger;
            _scheduleDataContext = scheduleDataContext;
            _messageClient = messageClient;
            _tenants = tenants;
            _httpClient = CreateHttpClient();
        }

        protected virtual HttpClient CreateHttpClient() => new() { Timeout = TimeSpan.FromSeconds(30) };

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

            if (ShouldPublishSchedule(schedule, today))
            {
                if (schedule.TriggerType == ScheduleTriggerType.Webhook)
                {
                    await PublishToWebhookAsync(schedule);
                    return;
                }

                await PublishScheduledTask(schedule, tenantId);
            }
        }

        private bool ShouldPublishSchedule(Schedule schedule, DateTime today)
        {
            var startDate = schedule.StartDate ?? DateTime.UtcNow;
            return today >= startDate &&
                   (schedule.EndDate is null || today <= schedule.EndDate.Value);
        }

        private async Task PublishToWebhookAsync(Schedule schedule)
        {
            var webhook = schedule.Webhook;
            if (webhook is null || string.IsNullOrWhiteSpace(webhook.Url))
            {
                _logger.LogWarning("Schedule {ItemId} is a webhook schedule but has no webhook configuration; skipping.", schedule.ItemId);
                return;
            }

            try
            {
                var payload = schedule.Payload ?? string.Empty;

                using var request = new HttpRequestMessage(new HttpMethod(webhook.Method), webhook.Url);

                if (!string.IsNullOrWhiteSpace(payload))
                {
                    request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                }

                if (webhook.Headers is not null)
                {
                    foreach (var header in webhook.Headers)
                    {
                        // Content-Type is set on the StringContent itself; ignore any incoming copy.
                        if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                if (!string.IsNullOrEmpty(webhook.SigningSecret))
                {
                    request.Headers.TryAddWithoutValidation("x-signature-sha256", ComputeSignature(webhook.SigningSecret, payload));
                }

                using var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    // Log only — webhook deliveries are fire-and-forget; Hangfire must not retry.
                    _logger.LogWarning(
                        "Webhook for schedule {ItemId} to {Url} returned non-success status code {StatusCode} ({ReasonPhrase}).",
                        schedule.ItemId, webhook.Url, (int)response.StatusCode, response.ReasonPhrase);
                }
                else
                {
                    _logger.LogInformation(
                        "Webhook for schedule {ItemId} delivered with status code {StatusCode}.",
                        schedule.ItemId, (int)response.StatusCode);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    "Webhook delivery for schedule {ItemId} to {Url} failed: {ErrorMessage}.",
                    schedule.ItemId, webhook.Url, ex.Message);
            }
            catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Webhook delivery for schedule {ItemId} to {Url} timed out after {Timeout} seconds.",
                    schedule.ItemId, webhook.Url, _httpClient.Timeout.TotalSeconds);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "Webhook delivery for schedule {ItemId} to {Url} was cancelled.",
                    schedule.ItemId, webhook.Url);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "Webhook delivery for schedule {ItemId} to {Url} failed with an unexpected error.",
                    schedule.ItemId, webhook.Url);
            }
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
                return;
            }

            await _messageClient.SendToConsumerAsync(new ConsumerMessage<PublishScheduleCommand>
            {
                ConsumerName = schedule.Queue.QueueName,
                Payload = publishScheduleCommand
            });
        }
    }
}
