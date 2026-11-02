using System.ComponentModel.DataAnnotations;
using Blocks.Genesis.Entities.Logic;

namespace Workflow.DomainService.Dtos;

public class WorkflowUpdateSettingsRequestDto
{
    [Required]
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Replaces the workflow's whole settings; null clears them.</summary>
    public WorkflowSettings? Settings { get; set; }
}
