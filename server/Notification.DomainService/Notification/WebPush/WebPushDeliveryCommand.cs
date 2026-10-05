namespace DomainService.Notification
{
    /// <summary>
    /// One push to one device, published by <see cref="WebPushNotificationServiceProvider"/> and
    /// delivered by the Worker. Carries everything the Worker needs, so delivery does not depend
    /// on the subscription still being readable when the message is consumed.
    /// </summary>
    public class WebPushDeliveryCommand
    {
        public string SubscriptionEndpoint { get; set; } = string.Empty;
        public string KeysP256dh { get; set; } = string.Empty;
        public string KeysAuth { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string DenormalizedPayload { get; set; } = string.Empty;

        /// <summary>1-based attempt number the Worker starts from.</summary>
        public int Attempt { get; set; } = 1;
    }
}
