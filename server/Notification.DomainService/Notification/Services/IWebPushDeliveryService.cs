namespace DomainService.Notification
{
    public interface IWebPushDeliveryService
    {
        /// <summary>
        /// Delivers one command with the fixed retry policy. Never throws for delivery failures;
        /// they are logged.
        /// </summary>
        Task DeliverAsync(WebPushDeliveryCommand command, CancellationToken cancellationToken = default);
    }
}
