namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// The single seam through which a configured header / query value is materialised before it is sent
    /// upstream. Real <c>${SECRET.NAME}</c> resolution is deferred to a later spec; isolating it here keeps
    /// that change to a one-class swap.
    /// </summary>
    public interface IProxySecretResolver
    {
        /// <summary>
        /// Returns the value to send upstream for <paramref name="rawValue"/>. Phase 2 is the identity
        /// function: values (including any literal <c>${SECRET.X}</c>) go upstream verbatim.
        /// </summary>
        string Resolve(string rawValue);
    }

    /// <summary>
    /// Phase-2 <see cref="IProxySecretResolver"/>: identity. A future spec swaps this for an implementation
    /// that looks up <c>${SECRET.NAME}</c> tokens in the tenant's secret store.
    /// </summary>
    public sealed class IdentityProxySecretResolver : IProxySecretResolver
    {
        public string Resolve(string rawValue) => rawValue ?? string.Empty;
    }
}
