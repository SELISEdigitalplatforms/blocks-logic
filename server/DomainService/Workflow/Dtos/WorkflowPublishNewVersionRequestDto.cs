using System.ComponentModel.DataAnnotations;
using Blocks.Genesis;

namespace DomainService.Workflow.Dtos
{
    public class WorkflowPublishNewVersionRequestDto : IProjectKey
    {
        [Required]
        public required string ProjectKey { get; set; }

        [Required]
        public required string WorkflowId { get; set; }

        [Required]
        public required string Name { get; set; }

        public string Description { get; set; } = string.Empty;

    }

}
