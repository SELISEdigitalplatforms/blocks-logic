namespace Common.InternalService.Storage
{
    public class SignatureString
    {
        public string? ItemId { get; set; }
        public long? FileVersion { get; set; }
        public string? ConfiguratioName { get; set; }
        public string AccessModifier { get; set; }
        public string? ProjectKey { get; set; }
        public string? ExpiryUtc { get; set; }
    }
}
