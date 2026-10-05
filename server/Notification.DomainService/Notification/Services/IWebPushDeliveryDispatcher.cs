namespace DomainService.Notification
{
    public interface IWebPushDeliveryDispatcher
    {
        /// <summary>
        /// Starts delivery of <paramref name="command"/> in the background and returns at once, so
        /// a message waiting out its retry delay never holds up the next queued message.
        /// </summary>
        void Dispatch(WebPushDeliveryCommand command);
    }
}
