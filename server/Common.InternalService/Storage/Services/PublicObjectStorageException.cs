namespace Common.InternalService.Storage
{
    /// <summary>
    /// A public-object operation (<c>IStorageService.UploadPublicObjectAsync</c> or
    /// <c>IStorageService.OpenReadAsync</c>) failed. <see cref="ErrorCode"/> is a short code a caller can
    /// return to its own client: the provider's own error code when it gave one, or one of the
    /// <c>public_object_*</c> and <c>public_objects_not_supported</c> codes below.
    /// </summary>
    public sealed class PublicObjectStorageException : Exception
    {
        /// <summary>The configured provider has no public read (SFTP).</summary>
        public const string NotSupportedCode = "public_objects_not_supported";

        public const string InvalidContainerCode = "public_object_invalid_container";
        public const string InvalidKeyCode = "public_object_invalid_key";
        public const string ConfigurationMissingCode = "configuration_not_found";
        public const string FailedCode = "public_object_failed";

        public PublicObjectStorageException(string errorCode, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }

        public string ErrorCode { get; }
    }
}
