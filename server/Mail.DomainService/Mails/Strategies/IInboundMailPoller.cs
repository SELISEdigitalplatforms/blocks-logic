using Mail.DomainService.Entities;
using Mail.DomainService.Shared.Enums;

namespace Mail.DomainService.Mails.Strategies
{
    /// <summary>How an inbound provider receives mail.</summary>
    public enum InboundMailMode
    {
        None = 0,

        /// <summary>The worker asks the provider on a timer.</summary>
        Poll = 1,

        /// <summary>The provider calls us. No such provider is registered yet.</summary>
        Push = 2,
    }

    /// <summary>
    /// Pulls inbound mail for one provider.
    /// </summary>
    public interface IInboundMailPoller
    {
        MailServiceProvider Provider { get; }

        InboundMailMode Mode { get; }

        /// <summary>
        /// Reads whatever is waiting for this configuration.
        /// </summary>
        /// <remarks>
        /// The tenant is passed explicitly rather than read from ambient context, because the
        /// polling worker iterates tenants itself and is not running inside any one tenant's
        /// request scope.
        /// </remarks>
        Task PollAsync(MailServerConfiguration configuration, string tenantId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Receives an authenticated provider callback, for a push provider.
    /// </summary>
    /// <remarks>
    /// Declared now and implemented by nobody. It exists so that adding a push provider later is a
    /// new ingress adapter plus a registration, rather than a change to the polling worker — the
    /// two have nothing in common except the normalized messages they hand on. Registering one
    /// does not give it a polling loop, and the worker never resolves this contract.
    /// </remarks>
    public interface IInboundMailWebhookHandler
    {
        MailServiceProvider Provider { get; }

        Task HandleAsync(MailServerConfiguration configuration, string payload, CancellationToken cancellationToken = default);
    }
}
