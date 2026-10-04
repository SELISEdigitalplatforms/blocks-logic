namespace Common.InternalService.Secret.Services
{
    /// <summary>
    /// Read-only catalog of the caller-tenant's <b>platform</b> secrets, shared by every module that offers a
    /// secret picker (Proxy's <c>{{$VAR.name}}</c> menu, Workflow, and whatever comes next). Identity and tags
    /// only &mdash; a secret VALUE never leaves the server, and is resolved by each module's own runtime seam
    /// (e.g. <c>IProxyVariableResolver</c>) at execution time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>SeliseBlocks.Secrets.OS</c> ships <b>no HTTP surface</b> &mdash; it is a purely in-process library
    /// (<c>ISecretService</c>, supplied by <c>AddBlocksSecrets()</c>). This service is the one control-plane
    /// wrapper over it, so the wrapper does not get re-implemented per module.
    /// </para>
    /// <para>
    /// It is only ever called from an <c>[Authorize]</c> action, so the ambient <c>BlocksContext</c> is already
    /// the caller's tenant and identity &mdash; no tenant swap is needed here.
    /// </para>
    /// </remarks>
    public interface ISecretCatalogService
    {
        Task<SecretListResponse> GetAllAsync(GetSecretsRequest request, CancellationToken cancellationToken = default);
    }
}
