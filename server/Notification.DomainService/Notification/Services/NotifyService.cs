using Blocks.Genesis;
using Blocks.Genesis.Notifications;
using DomainService.Shared;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace DomainService.Notification
{
    /// <inheritdoc cref="INotifyService"/>
    /// <remarks>
    /// No caller needs a synchronous delivery result: <c>ConnectionId</c> was never used to pick receivers, and
    /// <c>ResponseKey</c>/<c>ResponseValue</c> (how pages match job results and progress streams) travel with the
    /// published notification to the live message and the inbox row.
    /// </remarks>
    public sealed class NotifyService : INotifyService
    {
        private readonly IValidator<NotifyRequest> _validator;
        private readonly INotificationPublisher _publisher;
        private readonly ILogger<NotifyService> _logger;

        public NotifyService(IValidator<NotifyRequest> validator, INotificationPublisher publisher, ILogger<NotifyService> logger)
        {
            _validator = validator;
            _publisher = publisher;
            _logger = logger;
        }

        public async Task<BaseResponse> NotifyAsync(NotifyRequest notifyRequest)
        {
            var validation = await _validator.ValidateAsync(notifyRequest);
            if (!validation.IsValid)
            {
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = validation.Errors
                        .GroupBy(e => e.PropertyName)
                        .ToDictionary(g => g.Key, g => g.First().ErrorMessage),
                };
            }

            _publisher.Publish(ToRequest(notifyRequest));
            _logger.LogInformation("Notify: {ConfigurationName} queued", notifyRequest.ConfigurationName);
            return new BaseResponse { IsSuccess = true };
        }

        /// <summary>
        /// The named users are the users the event is about; Roles are no longer used (the configuration's audience
        /// decides). The first organization id is the send's organization for a send without a user token.
        /// </summary>
        internal static NotificationRequest ToRequest(NotifyRequest request) => new()
        {
            Name = request.ConfigurationName.Trim(),
            NamedUserIds = request.UserIds ?? [],
            OrganizationId = request.OrganizationIds?.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)),
            Payload = request.DenormalizedPayload,
            SubscriptionFilters = request.SubscriptionFilters,
            ResponseKey = request.ResponseKey,
            ResponseValue = request.ResponseValue,
        };
    }
}
