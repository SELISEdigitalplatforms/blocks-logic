namespace DomainService.Notification
{
    /// <summary>Raised when a delivery command cannot be published to the message bus.</summary>
    public class WebPushQueueUnavailableException : Exception
    {
        public WebPushQueueUnavailableException()
            : base(WebPushConstants.QueueErrorMessage)
        {
        }

        public WebPushQueueUnavailableException(string message)
            : base(message)
        {
        }

        public WebPushQueueUnavailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
