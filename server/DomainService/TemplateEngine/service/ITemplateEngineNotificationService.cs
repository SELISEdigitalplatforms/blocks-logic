namespace DomainService.TemplateEngine.service
{
    public interface ITemplateEngineNotificationService
    {
        Task NotifyRenderWithJsonEvent(bool success, string renderedFileId, string? subscriptionFilterId, string? tenantId);
        Task NotifyRenderWithJsonBulkEvent(bool success, string referenceId, string? subscriptionFilterId, string? tenantId, int successCount, int failureCount);
        Task NotifyGenerateRenderedFileEvent(bool success, string fileId, string? subscriptionFilterId, string? tenantId);
        Task NotifyGenerateRenderedFilesBulkEvent(bool success, string? bulkSubscriptionFilterId, string? tenantId, int successCount, int failureCount);
        Task NotifyCreateFileWithFilteredMongoQueryEvent(bool success, string fileId, string? subscriptionFilterId, string? tenantId);
        Task NotifyCreateFileWithFilteredMongoQueryBulkEvent(bool success, string? subscriptionFilterId, string? tenantId, int successCount, int failureCount);
        Task NotifyCreateMultipleFileWithFilteredMongoQueryEvent(bool success, string requestId, string? subscriptionFilterId, string? tenantId, string message);
    }
}


