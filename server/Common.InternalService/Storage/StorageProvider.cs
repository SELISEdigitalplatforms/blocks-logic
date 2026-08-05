namespace Common.InternalService.Storage
{
    public static class StorageProvider
    {
        public static string ConnectionString { get; set; } = string.Empty;
        public static string AccessKey { get; set; } = string.Empty;
        public static string SecretKey { get; set; } = string.Empty;
        public static string Region { get; set; } = string.Empty;

        #region Local Storage
        public static string Host { get; set; } = string.Empty;
        public static string Port { get; set; } = string.Empty;
        public static string UserName { get; set; } = string.Empty;
        public static string Password { get; set; } = string.Empty;
        public static string RemoteBasePath { get; set; } = string.Empty;
        public static string SftpSecretKey { get; set; } = string.Empty;
        #endregion
    }
}
