using Microsoft.AspNetCore.Mvc;
using Blocks.Genesis;
using DomainService.Workflow.Dtos;
using DomainService.Workflow.Services;
using MongoDB.Bson;
using System.Text.Json;

namespace Utilities.Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class WorkflowController : ControllerBase
    {
        private readonly ChangeControllerContext _changeControllerContext;
        private readonly IWorkflowService _workflowService;
        private readonly IWorkflowExecutionService _workflowExecutionService;

        public WorkflowController(
            ChangeControllerContext changeControllerContext,
            IWorkflowService workflowService,
            IWorkflowExecutionService workflowExecutionService)
        {
            _changeControllerContext = changeControllerContext;
            _workflowService = workflowService;
            _workflowExecutionService = workflowExecutionService;
        }

        [HttpPost]
        public async Task<IActionResult> GetAll([FromBody] WorkflowGetsRequestDto dto)
        {
            ApplyContext(dto);
            var workflows = await _workflowService.GetAllAsync(dto);
            return Ok(workflows);
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] WorkflowGetRequestDto dto)
        {
            ApplyContext(dto);
            var workflow = await _workflowService.GetAsync(dto);
            return Ok(workflow);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] WorkflowCreateRequestDto dto)
        {
            ApplyContext(dto);
            var result = await _workflowService.CreateAsync(dto);
            return StatusCode(StatusCodes.Status201Created, result);
        }

        [HttpPost]
        public async Task<IActionResult> Duplicate([FromBody] WorkflowDuplicateRequestDto dto)
        {
            ApplyContext(dto);
            var result = await _workflowService.DuplicateAsync(dto);
            return StatusCode(StatusCodes.Status201Created, result);
        }

        [HttpPut]
        public async Task<IActionResult> Update([FromBody] WorkflowUpdateRequestDto dto)
        {
            ApplyContext(dto);
            var result = await _workflowService.UpdateAsync(dto);
            return Ok(result);
        }

        [HttpDelete]
        public async Task<IActionResult> Delete([FromQuery] WorkflowDeleteRequestDto dto)
        {
            ApplyContext(dto);
            var result = await _workflowService.DeleteAsync(dto);
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
                var response = await _workflowExecutionService.WebhookStartAsync(
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

        [HttpGet]
        public async Task<IActionResult> GetExecutions([FromQuery] WorkflowExecutionsGetRequestDto dto)
        {
            ApplyContext(dto);
            var executions = await _workflowExecutionService.GetExecutionsByWorkflowIdAsync(dto);
            return Ok(executions);
        }

        [HttpGet]
        public async Task<IActionResult> GetExecution([FromQuery] WorkflowExecutionGetRequestDto dto)
        {
            ApplyContext(dto);
            var execution = await _workflowExecutionService.GetExecutionByIdAsync(dto);
            return Ok(execution);
        }

        private void ApplyContext(IProjectKey request)
        {
            _changeControllerContext.ChangeContext(request);
        }
    }
}