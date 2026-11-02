using Workflow.DomainService.Entities;
using Blocks.Genesis.Entities.Logic;

namespace Workflow.DomainService.Repositories
{
    public interface IWorkflowRepository
    {
        Task CreateWorkflowAsync(WorkflowEntity workflow);

        /// <param name="hasErrorTrigger">True: only published workflows whose published trigger nodes include an Error Trigger.</param>
        /// <param name="excludeItemId">When set, this workflow is left out.</param>
        Task<List<WorkflowEntity>> GetAllWorkflowsAsync(string tenantId, int pageSize, int pageNumber, string? search, bool? isPublished, bool? hasErrorTrigger, string? excludeItemId);

        /// <summary>
        /// Workflows whose draft OR published trigger nodes (PublishedMeta.TriggerNodes, published only) use the
        /// mailbox. Test mode matches on the draft; production must be confirmed against the published snapshot.
        /// </summary>
        Task<List<WorkflowEntity>> GetWorkflowsByMailServerConfigurationIdAsync(string tenantId, string mailServerConfigurationId);

        Task<List<WorkflowEntity>> GetWorkflowsByDataCollectionAsync(string tenantId, string collectionName, string operation);

        Task<List<WorkflowEntity>> GetPublishWorkflowsByDataCollectionAsync(string tenantId, string collectionName, string operation);
        /// <summary>Published workflows whose published trigger nodes include a Blocks Event trigger for <paramref name="eventName"/>.</summary>
        Task<List<WorkflowEntity>> GetPublishWorkflowsByBlocksEventAsync(string tenantId, string eventName);
        /// <summary>How many published workflows <see cref="GetPublishWorkflowsByBlocksEventAsync"/> would return.</summary>
        Task<long> CountPublishWorkflowsByBlocksEventAsync(string tenantId, string eventName);

        Task<long> GetWorkflowsCountAsync(string tenantId, string? search, bool? isPublished, bool? hasErrorTrigger, string? excludeItemId);

        /// <summary>
        /// Workflows with a function step pointing at <paramref name="functionId"/>, so deleting a
        /// function can say what it would break. Projected to identity only — the caller wants
        /// names, not definitions.
        /// </summary>
        Task<List<WorkflowEntity>> GetWorkflowsUsingFunctionAsync(string tenantId, string functionId, int limit = 50);

        Task<WorkflowEntity> GetWorkflowAsync(string tenantId, string workflowId);
        /// <summary>As the caller may see it: properties a View column rule hides are cleared. Never save it back.</summary>
        Task<WorkflowEntity> GetWorkflowForViewAsync(string tenantId, string workflowId);

        Task UpdateWorkflowAsync(WorkflowEntity workflow);

        /// <summary>The workflow's live settings only (projected); null when the workflow is not found.</summary>
        Task<WorkflowSettings?> GetWorkflowSettingsAsync(string tenantId, string workflowId);

        /// <summary>
        /// Replaces only <c>Settings</c> (and sets IsDirty and the last-updated fields) with a targeted update, so it never races with a
        /// whole-document save of the graph. False when the workflow is not found or not editable.
        /// </summary>
        Task<bool> UpdateWorkflowSettingsAsync(string tenantId, string workflowId, WorkflowSettings settings, string userId);

        Task DeleteWorkflowAsync(string tenantId, string workflowId);
    }
}