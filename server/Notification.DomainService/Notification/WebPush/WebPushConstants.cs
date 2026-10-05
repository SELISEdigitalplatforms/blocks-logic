namespace DomainService.Notification
{
    public static class WebPushConstants
    {
        /// <summary>Queue the API publishes delivery commands to and the Worker consumes.</summary>
        public const string DeliveryQueueName = "blocks_logic_webpush_delivery_listener";

        /// <summary>Total attempts per device, including the first.</summary>
        public const int MaxAttempts = 3;

        /// <summary>Error key and message returned by notify() when the bus cannot take the message.</summary>
        public const string QueueErrorKey = "delivery";
        public const string QueueErrorMessage = "Unable to queue notification for delivery.";

        /// <summary>Delay before attempt 2 and attempt 3. Index 0 is the delay before attempt 2.</summary>
        public static readonly IReadOnlyList<TimeSpan> RetryDelays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)];

        /// <summary>Upper bound on one HTTP push attempt. A timeout counts as a retryable failure.</summary>
        public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(30);
    }
}
