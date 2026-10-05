namespace DomainService.Notification
{
    public class RegisterWebPushSubscriptionRequest
    {
        public string Endpoint { get; set; } = string.Empty;
        public RegisterWebPushSubscriptionKeys Keys { get; set; } = new();
        public long? ExpirationTime { get; set; }
        public string? UserAgent { get; set; }
    }

    public class RegisterWebPushSubscriptionKeys
    {
        public string P256dh { get; set; } = string.Empty;
        public string Auth { get; set; } = string.Empty;
    }
}
