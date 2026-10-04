using DomainService.Shared;
using FluentValidation;

namespace DomainService.Notification
{
    /// <summary>
    /// Groups FluentValidation dependencies for <see cref="NotificationService"/> so the
    /// service constructor stays under the Sonar parameter limit.
    /// </summary>
    public sealed class NotificationServiceValidators
    {
        public NotificationServiceValidators(
            IValidator<Subscription> subscription,
            IValidator<NotifyRequest> notifyRequest,
            IValidator<RegisterWebPushSubscriptionRequest> registerWebPush,
            IValidator<UnregisterWebPushSubscriptionRequest> unregisterWebPush)
        {
            Subscription = subscription;
            NotifyRequest = notifyRequest;
            RegisterWebPush = registerWebPush;
            UnregisterWebPush = unregisterWebPush;
        }

        public IValidator<Subscription> Subscription { get; }
        public IValidator<NotifyRequest> NotifyRequest { get; }
        public IValidator<RegisterWebPushSubscriptionRequest> RegisterWebPush { get; }
        public IValidator<UnregisterWebPushSubscriptionRequest> UnregisterWebPush { get; }
    }
}
