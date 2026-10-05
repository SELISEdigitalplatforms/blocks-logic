using Blocks.Genesis;
using DomainService.Notification;

namespace Worker.Consumers.Notification
{
    /// <summary>
    /// Receives one <see cref="WebPushDeliveryCommand"/> per device and hands it to the delivery
    /// dispatcher, which runs the send and its retries off the queue's processing path.
    /// </summary>
    public class WebPushDeliveryConsumer : IConsumer<WebPushDeliveryCommand>
    {
        private readonly IWebPushDeliveryDispatcher _dispatcher;

        public WebPushDeliveryConsumer(IWebPushDeliveryDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public Task Consume(WebPushDeliveryCommand context)
        {
            _dispatcher.Dispatch(context);
            return Task.CompletedTask;
        }
    }
}
