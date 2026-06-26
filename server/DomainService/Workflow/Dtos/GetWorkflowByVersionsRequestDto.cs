using System.ComponentModel.DataAnnotations;
using Blocks.Genesis;

namespace DomainService.Workflow.Dtos
{
    public class GetWorkflowByVersionRequestDto : IProjectKey
    {
        [Required]
        public required string ProjectKey { get; set; }
        [Required]
        public required string WorkflowId { get; set; }

        [Required]
        public required string VersionId { get; set; }

    }

}
