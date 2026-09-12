using Common.InternalService.Secret;
using Common.InternalService.Secret.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlocksTemplate.Api.Controllers
{
    /// <summary>
    /// Cross-module read-only API over the caller-tenant's <b>platform</b> secrets, routed as
    /// <c>/api/Secret/{action}</c>. It is deliberately module-agnostic: Proxy's <c>{{$VAR.name}}</c> picker,
    /// Workflow, and any later module share this one endpoint instead of each shipping its own wrapper.
    /// </summary>
    /// <remarks>
    /// <c>SeliseBlocks.Secrets.OS</c> is an in-process library with no HTTP surface of its own, so this is the
    /// only route the console has onto <c>ISecretService</c>. A secret VALUE is never returned here &mdash; it
    /// is resolved server-side at execution time by the owning module and never reaches a client.
    /// </remarks>
    [ApiController]
    [Route("[controller]/[action]")]
    public sealed class SecretController : ControllerBase
    {
        private readonly ISecretCatalogService _secretCatalogService;

        /// <summary>Takes the secret catalog service.</summary>
        public SecretController(ISecretCatalogService secretCatalogService)
        {
            _secretCatalogService = secretCatalogService;
        }

        /// <summary>
        /// The tenant's platform secrets: <c>id</c> / <c>name</c> / <c>tags</c> only, never a value and never
        /// the Blocks Secrets type. Optionally narrowed by <c>name</c>, <c>tag</c> and <c>search</c>. The
        /// tenant and caller identity come from the ambient <c>BlocksContext</c> (this is an
        /// <c>[Authorize]</c> route).
        /// </summary>
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] GetSecretsRequest request)
        {
            var result = await _secretCatalogService.GetAllAsync(request, HttpContext.RequestAborted);
            return Ok(result);
        }
    }
}
