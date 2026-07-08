using Microsoft.AspNetCore.Mvc;
using Blocks.Genesis;
using DomainService.Workflow.Dtos;
using DomainService.Workflow.Services;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;

namespace Utilities.Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class WorkflowController : ControllerBase
    {

        private readonly IWorkflowService _workflowService;
        private readonly IWorkflowExecutionService _workflowExecutionService;

        public WorkflowController(

            IWorkflowService workflowService,
            IWorkflowExecutionService workflowExecutionService)
        {

            _workflowService = workflowService;
            _workflowExecutionService = workflowExecutionService;
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> GetAll([FromBody] WorkflowGetsRequestDto dto)
        {
            var tenantId = GetTenantId();
            var workflows = await _workflowService.GetAllAsync(tenantId, dto);
            return Ok(workflows);
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] WorkflowGetRequestDto dto)
        {
            var tenantId = GetTenantId();
            var workflow = await _workflowService.GetAsync(tenantId, dto);
            return Ok(workflow);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] WorkflowCreateRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowService.CreateAsync(tenantId, dto);
            return StatusCode(StatusCodes.Status201Created, result);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Duplicate([FromBody] WorkflowDuplicateRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowService.DuplicateAsync(tenantId, dto);
            return StatusCode(StatusCodes.Status201Created, result);
        }

        [Authorize]
        [HttpPut]
        public async Task<IActionResult> Update([FromBody] WorkflowUpdateRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowService.UpdateAsync(tenantId, dto);
            return Ok(result);
        }

        [Authorize]
        [HttpDelete]
        public async Task<IActionResult> Delete([FromQuery] WorkflowDeleteRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowService.DeleteAsync(tenantId, dto);
            return Ok(result);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> CreateVersion([FromBody] WorkflowVersionCreateRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowService.CreateVersionAsync(tenantId, dto);
            return StatusCode(StatusCodes.Status201Created, result);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> UpdateVersion([FromBody] WorkflowVersionUpdateRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowService.UpdateVersionAsync(tenantId, dto);
            return Ok(result);
        }


        [Authorize]
        [HttpPost]
        public async Task<IActionResult> GetVersions([FromBody] WorkflowGetVersionsRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowService.GetVersionsAsync(tenantId, dto);
            return Ok(result);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> GetWorkflowByVersion([FromBody] GetWorkflowByVersionRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowService.GetWorkflowByVersionAsync(tenantId, dto);
            return Ok(result);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> PublishNewVersion([FromBody] WorkflowPublishNewVersionRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowService.PublishNewVersionAsync(tenantId, dto);
            return Ok(result);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> PublishVersion([FromBody] WorkflowPublishVersionRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowService.PublishVersionAsync(tenantId, dto);
            return Ok(result);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Unpublish([FromBody] WorkflowUnpublishRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowService.UnpublishAsync(tenantId, dto);
            return Ok(result);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Restore([FromBody] WorkflowRestoreRequestDto dto)
        {
            var tenantId = GetTenantId();
            var result = await _workflowService.RestoreAsync(tenantId, dto);
            return Ok(result);
        }



        [HttpPost("{projectKey}/{workflowId}/{webhookId}")]
        public async Task<IActionResult> Webhook(string projectKey, string workflowId, string webhookId, [FromBody] JsonElement input)
        {
            var dto = new WorkflowWebhookRequestDto
            {
                ProjectKey = projectKey,
                Input = input
            };
            ApplyContext(dto);

            try
            {
                var response = await _workflowExecutionService.TriggerWebhookAsync(
                    workflowId,
                    webhookId,
                    projectKey,
                    input
                );

                return Ok(response);
            }
            catch (UnauthorizedAccessException)
            {
                return StatusCode(401, new { message = "Unauthorized" });
            }

        }

        [ActionName("webhook-test")]
        [HttpPost("{projectKey}/{workflowId}/{webhookId}")]
        public async Task<IActionResult> TestWebhook(string projectKey, string workflowId, string webhookId, [FromBody] JsonElement input)
        {
            var dto = new WorkflowWebhookRequestDto
            {
                ProjectKey = projectKey,
                Input = input
            };
            ApplyContext(dto);

            try
            {
                var response = await _workflowExecutionService.TriggerTestWebhookAsync(
                    workflowId,
                    webhookId,
                    projectKey,
                    input
                );

                return Ok(response);
            }
            catch (UnauthorizedAccessException)
            {
                return StatusCode(401, new { message = "Unauthorized" });
            }

        }
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> StepExecute([FromBody] StepExecuteRequestDto dto)
        {
            var tenantId = GetTenantId();
            var executions = await _workflowExecutionService.StepExecuteAsync(tenantId, dto);
            return Ok(executions);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> TriggerListener([FromBody] TriggerListenerRequestDto dto)
        {
            var tenantId = GetTenantId();
            var executions = await _workflowService.TriggerListenerAsync(tenantId, dto);
            return Ok(executions);
        }


        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetExecutions([FromQuery] WorkflowExecutionsGetRequestDto dto)
        {
            var tenantId = GetTenantId();
            var executions = await _workflowExecutionService.GetExecutionsByWorkflowIdAsync(tenantId, dto);
            return Ok(executions);
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetExecution([FromQuery] WorkflowExecutionGetRequestDto dto)
        {
            var tenantId = GetTenantId();
            var execution = await _workflowExecutionService.GetExecutionByIdAsync(tenantId, dto);
            return Ok(execution);
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> LastSuccessfullExecution([FromQuery] LastSuccessfullExecutionRequestDto dto)
        {
            var tenantId = GetTenantId();
            var execution = await _workflowExecutionService.LastSuccessfullExecutionAsync(tenantId, dto);
            return Ok(execution);
        }

        private void ApplyContext(IProjectKey request)
        {

        }

        private string GetTenantId()
        {
            var context = BlocksContext.GetContext();
            if (context == null) return "";
            return context.TenantId;
        }
    }
}