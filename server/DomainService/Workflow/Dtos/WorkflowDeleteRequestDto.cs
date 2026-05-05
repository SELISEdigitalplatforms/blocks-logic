using System.ComponentModel.DataAnnotations;
using Blocks.Genesis;

namespace DomainService.Workflow.Dtos
{
    public class WorkflowDeleteRequestDto : IProjectKey
    {
        [Required]
        public required string Id { get; set; }

        [Required]
        public required string ProjectKey { get; set; }
    }

}
