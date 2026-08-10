namespace Common.InternalService.Storage
{
    public class DownloadUrlRequest
    {
        public string? ItemId { get; set; }
        public string? FileName { get; set; }
        public string? ConfigurationName { get; set; }
        public TimeSpan ExpiryDuration { get; set; }
        public AccessModifier AccessModifier { get; set; }
        public long? FileVersion { get; set; }
        public string? ProjectKey { get; set; }
        public string? RequestUrl { get; set; }
    }
}
