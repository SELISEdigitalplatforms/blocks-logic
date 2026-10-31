using Microsoft.AspNetCore.Mvc;
using Blocks.Genesis;
using Workflow.DomainService.BlocksEvents;

namespace Utilities.Api.Controllers
{
    /// <summary>
    /// The tenant's Blocks Event Trigger catalog, routed as <c>/api/BlocksEventTrigger/{action}</c>: which platform
    /// events may start workflows (and, later, functions). Reads need workflow read, changes need workflow manage;
    /// the tenant always comes from <see cref="BlocksContext"/>.
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class BlocksEventTriggerController : ControllerBase
    {
        private readonly IBlocksEventCatalogService _catalogService;

        /// <summary>Takes the catalog service.</summary>
        public BlocksEventTriggerController(IBlocksEventCatalogService catalogService)
        {
            _catalogService = catalogService;
        }

        /// <summary><c>GET</c> — every catalog event, sorted by service then label; <c>isSeeded=false</c> when the tenant has no catalog.</summary>
        [HttpGet]
        [ProtectedEndPoint("blocks-logic::workflow::read")]
        public async Task<IActionResult> GetAll()
        {
            return Ok(await _catalogService.GetAllAsync(GetTenantId()));
        }

        /// <summary><c>GET</c> — the events enabled for workflows: the Blocks Event node's options.</summary>
        [HttpGet]
        [ProtectedEndPoint("blocks-logic::workflow::read")]
        public async Task<IActionResult> GetWorkflowEvents()
        {
            return Ok(await _catalogService.GetWorkflowEventsAsync(GetTenantId()));
        }

        /// <summary><c>GET</c> — how many published workflows use the event; shown before turning Workflow off.</summary>
        [HttpGet]
        [ProtectedEndPoint("blocks-logic::workflow::read")]
        public async Task<IActionResult> GetUsage([FromQuery] string eventName)
        {
            return Ok(await _catalogService.GetUsageAsync(GetTenantId(), eventName));
        }

        /// <summary>
        /// <c>GET</c> — succeeds only for callers who may change the catalog (workflow manage); anyone else gets
        /// 403. The page uses it to show the switches read-only before a change is attempted.
        /// </summary>
        [HttpGet]
        [ProtectedEndPoint("blocks-logic::workflow::manage")]
        public IActionResult CanManage()
        {
            return Ok(new { canManage = true });
        }

        /// <summary>
        /// <c>POST</c> — sets one event's workflow and/or function flag. A missing name or no flag is a 400; an
        /// unknown event or a tenant without a catalog is <c>isSuccess=false</c> with the reason.
        /// </summary>
        [HttpPost]
        [ProtectedEndPoint("blocks-logic::workflow::manage")]
        public async Task<IActionResult> Update([FromBody] BlocksEventFlagUpdateRequest request)
        {
            var result = await _catalogService.UpdateAsync(GetTenantId(), request);
            var isRequestError = result.Errors?.Values.Any(code =>
                code is BlocksEventTriggerErrors.EventNameRequired
                    or BlocksEventTriggerErrors.EventNameTooLong
                    or BlocksEventTriggerErrors.NoFlagProvided) == true;
            return isRequestError ? BadRequest(result) : Ok(result);
        }

        private static string GetTenantId() => BlocksContext.GetContext()?.TenantId ?? string.Empty;
    }
}
