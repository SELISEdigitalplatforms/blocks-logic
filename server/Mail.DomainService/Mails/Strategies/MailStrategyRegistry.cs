using Mail.DomainService.Shared.Enums;

namespace Mail.DomainService.Mails.Strategies
{
    /// <summary>Resolves the outbound sender registered for a provider.</summary>
    public interface IOutboundMailSenderRegistry
    {
        /// <summary>
        /// The sender for <paramref name="provider"/>, or false when none is registered.
        /// </summary>
        /// <remarks>
        /// Returns rather than throws so the caller can answer with the existing failure event
        /// instead of an exception escaping its send-result path — and, crucially, can do so
        /// before any secret or network access.
        /// </remarks>
        bool TryResolve(MailServiceProvider provider, out IOutboundMailSender sender);

        IReadOnlyCollection<MailServiceProvider> RegisteredProviders { get; }
    }

    /// <summary>Resolves the inbound poller registered for a provider.</summary>
    public interface IInboundMailPollerRegistry
    {
        bool TryResolve(MailServiceProvider provider, out IInboundMailPoller poller);

        IReadOnlyCollection<MailServiceProvider> RegisteredProviders { get; }
    }

    /// <inheritdoc />
    public sealed class OutboundMailSenderRegistry : IOutboundMailSenderRegistry
    {
        private readonly IReadOnlyDictionary<MailServiceProvider, IOutboundMailSender> _senders;

        public OutboundMailSenderRegistry(IEnumerable<IOutboundMailSender> senders)
        {
            ArgumentNullException.ThrowIfNull(senders);

            _senders = senders.ToDictionary(sender => sender.Provider);
        }

        public IReadOnlyCollection<MailServiceProvider> RegisteredProviders => _senders.Keys.ToList();

        public bool TryResolve(MailServiceProvider provider, out IOutboundMailSender sender)
        {
            // Enum.IsDefined first: an undefined numeric value deserializes onto the enum
            // unchallenged, so without this a record carrying provider 7 would simply miss the
            // dictionary and be indistinguishable from an unregistered one.
            if (Enum.IsDefined(provider) && _senders.TryGetValue(provider, out var resolved))
            {
                sender = resolved;
                return true;
            }

            sender = null!;
            return false;
        }
    }

    /// <inheritdoc />
    public sealed class InboundMailPollerRegistry : IInboundMailPollerRegistry
    {
        private readonly IReadOnlyDictionary<MailServiceProvider, IInboundMailPoller> _pollers;

        public InboundMailPollerRegistry(IEnumerable<IInboundMailPoller> pollers)
        {
            ArgumentNullException.ThrowIfNull(pollers);

            _pollers = pollers.ToDictionary(poller => poller.Provider);
        }

        public IReadOnlyCollection<MailServiceProvider> RegisteredProviders => _pollers.Keys.ToList();

        public bool TryResolve(MailServiceProvider provider, out IInboundMailPoller poller)
        {
            if (Enum.IsDefined(provider) && _pollers.TryGetValue(provider, out var resolved))
            {
                poller = resolved;
                return true;
            }

            poller = null!;
            return false;
        }
    }
}
