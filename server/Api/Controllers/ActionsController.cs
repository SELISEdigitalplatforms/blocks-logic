using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Workflow.DomainService.Actions;

namespace Utilities.Api.Controllers
{
    /// <summary>
    /// The tenant's actions the editors may pick, routed as <c>/api/Actions/{action}</c>. blocks-os owns the list and its
    /// switches; logic only reads it. The tenant always comes from <see cref="BlocksContext"/>.
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class ActionsController : ControllerBase
    {
        private readonly IActionsService _actionsService;

        /// <summary>Takes the actions service.</summary>
        public ActionsController(IActionsService actionsService)
        {
            _actionsService = actionsService;
        }

        /// <summary><c>GET</c> — actions with the Workflow switch on: the Action trigger node's options.</summary>
        [HttpGet]
        [ProtectedEndPoint("blocks-logic::workflow::read")]
        [ProducesResponseType(typeof(IReadOnlyList<ActionOptionDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetForWorkflow()
        {
            return Ok(await _actionsService.GetForWorkflowAsync(HttpContext.RequestAborted));
        }

        /// <summary><c>GET</c> — actions with the Function switch on: the function Trigger section's options.</summary>
        [HttpGet]
        [ProtectedEndPoint("blocks-logic::function::read")]
        [ProducesResponseType(typeof(IReadOnlyList<ActionOptionDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetForFunction()
        {
            return Ok(await _actionsService.GetForFunctionAsync(HttpContext.RequestAborted));
        }
    }
}
