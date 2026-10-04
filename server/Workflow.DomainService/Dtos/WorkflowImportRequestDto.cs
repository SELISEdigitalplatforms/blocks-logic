namespace Workflow.DomainService.Dtos;

public class WorkflowImportRequestDto
{
    public string FileId { get; set; } = string.Empty;
    public string MessageCoRelationId { get; set; } = string.Empty;
}
