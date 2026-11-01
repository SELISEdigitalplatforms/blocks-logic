using Blocks.Genesis;
using Blocks.Genesis.Notifications;

namespace DomainService.Notification.Push
{
    /// <summary>Consumer of <see cref="NotificationQueues.Push"/> in the logic Worker.</summary>
    public sealed class PushRequestedConsumer : IConsumer<PushRequested>
    {
        private readonly IPushRequestHandler _handler;

        public PushRequestedConsumer(IPushRequestHandler handler)
        {
            _handler = handler;
        }

        public Task Consume(PushRequested context) => _handler.HandleAsync(context, CancellationToken.None);
    }
}
