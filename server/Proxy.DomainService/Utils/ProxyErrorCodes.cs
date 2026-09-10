namespace Proxy.DomainService.Utils
{
    /// <summary>Stable <c>code</c> values returned to the console for proxy control-plane failures (SPEC &sect;3.4).</summary>
    public static class ProxyErrorCodes
    {
        /// <summary>400 &mdash; a field on the create / update payload failed validation.</summary>
        public const string Validation = "PROXY_VALIDATION";

        /// <summary>409 &mdash; a proxy whose derived slug already exists for the tenant.</summary>
        public const string SlugConflict = "PROXY_SLUG_CONFLICT";

        /// <summary>404 &mdash; the target proxy does not exist for the caller's tenant.</summary>
        public const string NotFound = "PROXY_NOT_FOUND";

        /// <summary>404 &mdash; the target version does not belong to the proxy and tenant.</summary>
        public const string VersionNotFound = "PROXY_VERSION_NOT_FOUND";

        /// <summary>409 &mdash; Revert was called for a proxy whose row has been deleted.</summary>
        public const string Deleted = "PROXY_DELETED";

        /// <summary>409 &mdash; "Revert this change" blocked: a field it targets was changed again by a later version.</summary>
        public const string RevertConflict = "PROXY_REVERT_CONFLICT";

        /// <summary>400 &mdash; the target version has nothing to revert (Create / Delete / empty change set).</summary>
        public const string VersionNotRevertable = "PROXY_VERSION_NOT_REVERTABLE";
    }
}
