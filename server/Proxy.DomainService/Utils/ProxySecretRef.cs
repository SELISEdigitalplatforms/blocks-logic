using System.Text.RegularExpressions;

namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// Detects <c>${SECRET.NAME}</c> references in a stored header / query value. Phase 1 records only
    /// that a reference is present (via <c>IsSecretRef</c>); it never resolves the reference.
    /// </summary>
    public static partial class ProxySecretRef
    {
        [GeneratedRegex(@"\$\{SECRET\.[A-Za-z0-9_]+\}")]
        private static partial Regex SecretReferencePattern();

        public static bool IsSecretReference(string? value) =>
            !string.IsNullOrEmpty(value) && SecretReferencePattern().IsMatch(value);

        /// <summary>
        /// Yields every <c>${SECRET.NAME}</c> token literally present in <paramref name="value"/>, in the
        /// order they appear (with duplicates). Phase 3's overview endpoint de-duplicates across a proxy's
        /// header/query values; the reference is still never resolved.
        /// </summary>
        public static IEnumerable<string> Tokens(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                yield break;
            }

            foreach (Match match in SecretReferencePattern().Matches(value))
            {
                yield return match.Value;
            }
        }
    }
}
