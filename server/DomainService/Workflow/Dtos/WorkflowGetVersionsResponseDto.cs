

using Blocks.Genesis;
using DomainService.Workflow.Models;

namespace DomainService.Workflow.Dtos
{
    public class WorkflowGetVersionsResponseDto : BaseQueryListResponse<List<WorkflowSnapshotModel>>
    {
    }
}