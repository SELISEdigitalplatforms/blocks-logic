using Microsoft.AspNetCore.Mvc;
using Blocks.Genesis;
using Workflow.DomainService.Dtos;
using Workflow.DomainService.Services;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;

namespace Utilities.Api.Controllers
{
    /// <summary>
    /// Management API for workflows, their versions and their executions, routed as
    /// <c>/api/Workflow/{action}</c>. Every action except the webhook entry points requires a bearer
    /// token; the tenant always comes from <see cref="BlocksContext"/> and is never taken from the payload.
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class WorkflowController : ControllerBase
    {

        private readonly IWorkflowService _workflowService;
        private readonly IWorkflowVersionService _workflowVersionService;
        private readonly IWorkflowExecutionService _workflowExecutionService;

        /// <summary>Takes the workflow, version and execution services.</summary>
        public WorkflowController(
            IWorkflowService workflowService,
            IWorkflowVersionService workflowVersionService,
            IWorkflowExecutionService workflowExecutionService)
        {
            _workflowService = workflowService;
            _workflowVersionService = workflowVersionService;
            _workflowExecutionService = workflowExecutionService;
        }

        /// <summary><c>POST</c> — the tenant's workflows, filtered and paged by the request body.</summary>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> GetAll([FromBody] WorkflowGetsRequestDto dto)
        {
            var tenantId = GetTenantId();
            var workflows = await _workflowService.GetAllAsync(tenantId, dto);
            return Ok(workflows);
        }

        /// <summary><c>GET</c> — one workflow by id, including its node graph.</summary>
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] WorkflowGetRequestDto dto)
        {
            var tenantId = GetTenantId();
            var workflow = await _workflowService.GetAsync(tenantId, dto);
            return Ok(workflow);
        }

        /// <summary><c>POST</c> — creates a workflow. 201 on success.</summary>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] WorkflowCreateRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowService.CreateAsync(tenantId, dto);
            return StatusCode(StatusCodes.Status201Created, result);
        }

        /// <summary><c>POST</c> — copies an existing workflow into a new one. 201 on success.</summary>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Duplicate([FromBody] WorkflowDuplicateRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowService.DuplicateAsync(tenantId, dto);
            return StatusCode(StatusCodes.Status201Created, result);
        }

        /// <summary><c>PUT</c> — rewrites a workflow's name, description and node graph.</summary>
        [Authorize]
        [HttpPut]
        public async Task<IActionResult> Update([FromBody] WorkflowUpdateRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowService.UpdateAsync(tenantId, dto);
            return Ok(result);
        }

        /// <summary><c>DELETE</c> — removes a workflow by id.</summary>
        [Authorize]
        [HttpDelete]
        public async Task<IActionResult> Delete([FromQuery] WorkflowDeleteRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowService.DeleteAsync(tenantId, dto);
            return Ok(result);
        }

        /// <summary><c>POST</c> — snapshots the working workflow as a new version row. 201 on success.</summary>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> CreateVersion([FromBody] WorkflowVersionCreateRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowVersionService.CreateVersionAsync(tenantId, dto);
            return StatusCode(StatusCodes.Status201Created, result);
        }

        /// <summary><c>POST</c> — edits an existing version's metadata.</summary>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> UpdateVersion([FromBody] WorkflowVersionUpdateRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowVersionService.UpdateVersionAsync(tenantId, dto);
            return Ok(result);
        }


        /// <summary><c>POST</c> — the version history for one workflow.</summary>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> GetVersions([FromBody] WorkflowGetVersionsRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowVersionService.GetWorkflowVersionsAsync(tenantId, dto);
            return Ok(result);
        }

        /// <summary><c>POST</c> — the workflow graph as captured by a specific version.</summary>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> GetWorkflowByVersion([FromBody] GetWorkflowByVersionRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowService.GetWorkflowByVersionAsync(tenantId, dto);
            return Ok(result);
        }

        /// <summary><c>POST</c> — snapshots the current graph as a new version and publishes it in one step.</summary>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> PublishNewVersion([FromBody] WorkflowPublishNewVersionRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowService.PublishNewVersionAsync(tenantId, dto);
            return Ok(result);
        }

        /// <summary><c>POST</c> — publishes an existing version, making it the one webhooks execute.</summary>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> PublishVersion([FromBody] WorkflowPublishVersionRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowService.PublishVersionAsync(tenantId, dto);
            return Ok(result);
        }

        /// <summary><c>POST</c> — takes the workflow out of service; its webhooks stop executing.</summary>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Unpublish([FromBody] WorkflowUnpublishRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowService.UnpublishAsync(tenantId, dto);
            return Ok(result);
        }

        /// <summary><c>POST</c> — restores a soft-deleted workflow.</summary>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Restore([FromBody] WorkflowRestoreRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowService.RestoreAsync(tenantId, dto);
            return Ok(result);
        }



        /// <summary>
        /// Data-plane entry point: runs the published workflow behind a webhook trigger.
        /// <c>POST /api/Workflow/Webhook/{tenantId}/{workflowId}/{webhookId}</c>. Anonymous at the framework
        /// level — the trigger's own <c>authType</c> decides whether a caller must be authenticated or
        /// authorized, and a failure surfaces here as 401.
        /// </summary>
        [HttpPost("{tenantId}/{workflowId}/{webhookId}")]
        public async Task<IActionResult> Webhook(string tenantId, string workflowId, string webhookId, [FromBody] JsonElement input)
        {
            try
            {
                var response = await _workflowExecutionService.TriggerWebhookAsync(
                    workflowId,
                    webhookId,
                    tenantId,
                    input
                );

                return Ok(response);
            }
            catch (UnauthorizedAccessException)
            {
                return StatusCode(401, new { message = "Unauthorized" });
            }

        }

        /// <summary>
        /// The <c>webhook-test</c> twin of <see cref="Webhook"/>: runs the DRAFT graph rather than the
        /// published version, for the console's test panel. Same route shape, same 401 behaviour.
        /// </summary>
        [ActionName("webhook-test")]
        [HttpPost("{tenantId}/{workflowId}/{webhookId}")]
        public async Task<IActionResult> TestWebhook(string tenantId, string workflowId, string webhookId, [FromBody] JsonElement input)
        {
            try
            {
                var response = await _workflowExecutionService.TriggerTestWebhookAsync(
                    workflowId,
                    webhookId,
                    tenantId,
                    input
                );

                return Ok(response);
            }
            catch (UnauthorizedAccessException)
            {
                return StatusCode(401, new { message = "Unauthorized" });
            }

        }

        /// <summary>
        /// Header-based twin of <see cref="Webhook"/>: tenant comes from <c>x-blocks-key</c> rather than the
        /// path. <c>POST /api/Workflow/Webhook/{workflowId}/{webhookId}</c>. Anonymous at the framework
        /// level — missing or blank header is 400; trigger <c>authType</c> failures still surface as 401.
        /// </summary>
        [ActionName("Webhook")]
        [HttpPost("{workflowId}/{webhookId}")]
        public async Task<IActionResult> WebhookByHeader(string workflowId, string webhookId, [FromBody] JsonElement input)
        {
            if (!TryGetTenantIdFromBlocksKey(out var tenantId, out var error))
            {
                return error;
            }

            try
            {
                var response = await _workflowExecutionService.TriggerWebhookAsync(
                    workflowId,
                    webhookId,
                    tenantId,
                    input
                );

                return Ok(response);
            }
            catch (UnauthorizedAccessException)
            {
                return StatusCode(401, new { message = "Unauthorized" });
            }
        }

        /// <summary>
        /// Header-based twin of <see cref="TestWebhook"/>: tenant comes from <c>x-blocks-key</c> rather than
        /// the path. <c>POST /api/Workflow/webhook-test/{workflowId}/{webhookId}</c>. Same 400/401 behaviour
        /// as <see cref="WebhookByHeader"/>.
        /// </summary>
        [ActionName("webhook-test")]
        [HttpPost("{workflowId}/{webhookId}")]
        public async Task<IActionResult> TestWebhookByHeader(string workflowId, string webhookId, [FromBody] JsonElement input)
        {
            if (!TryGetTenantIdFromBlocksKey(out var tenantId, out var error))
            {
                return error;
            }

            try
            {
                var response = await _workflowExecutionService.TriggerTestWebhookAsync(
                    workflowId,
                    webhookId,
                    tenantId,
                    input
                );

                return Ok(response);
            }
            catch (UnauthorizedAccessException)
            {
                return StatusCode(401, new { message = "Unauthorized" });
            }
        }

        /// <summary><c>POST</c> — executes a single node in isolation and returns its output, for the node inspector.</summary>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> StepExecute([FromBody] StepExecuteRequestDto dto)
        {
            var tenantId = GetTenantId();
            var executions = await _workflowExecutionService.StepExecuteAsync(tenantId, dto);
            return Ok(executions);
        }

        /// <summary><c>POST</c> — fires a listener-type trigger by hand.</summary>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> TriggerListener([FromBody] TriggerListenerRequestDto dto)
        {
            var tenantId = GetTenantId();
            var executions = await _workflowService.TriggerListenerAsync(tenantId, dto);
            return Ok(executions);
        }


        /// <summary><c>GET</c> — the execution history for one workflow.</summary>
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetExecutions([FromQuery] WorkflowExecutionsGetRequestDto dto)
        {
            var tenantId = GetTenantId();
            var executions = await _workflowExecutionService.GetExecutionsByWorkflowIdAsync(tenantId, dto);
            return Ok(executions);
        }

        /// <summary><c>GET</c> — one execution, including per-node results.</summary>
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetExecution([FromQuery] WorkflowExecutionGetRequestDto dto)
        {
            var tenantId = GetTenantId();
            var execution = await _workflowExecutionService.GetExecutionByIdAsync(tenantId, dto);
            return Ok(execution);
        }

        /// <summary><c>GET</c> — the most recent successful execution, used to prefill node inputs from real data.</summary>
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> LastSuccessfullExecution([FromQuery] LastSuccessfullExecutionRequestDto dto)
        {
            var tenantId = GetTenantId();
            var execution = await _workflowExecutionService.LastSuccessfullExecutionAsync(tenantId, dto);
            return Ok(execution);
        }

        private string GetTenantId()
        {
            var context = BlocksContext.GetContext();
            if (context == null) return "";
            return context.TenantId;
        }

        private bool TryGetTenantIdFromBlocksKey(out string tenantId, out IActionResult error)
        {
            tenantId = Request.Headers["x-blocks-key"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                error = BadRequest(new { message = "x-blocks-key header is required" });
                return false;
            }
            error = null!;
            return true;
        }
    }
}